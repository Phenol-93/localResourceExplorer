using System.IO;
using Microsoft.Data.Sqlite;

namespace LocalResourceExplorer.Services;

public sealed class DatabaseService
{
    private const string AppFolderName = "LocalResourceExplorer";
    private const string DatabaseFileName = "library.db";
    private const string SchemaRelativePath = "Data/schema.sql";
    private const string SchemaVersionSettingKey = "database_schema_version";
    private const int CurrentSchemaVersion = 4;

    public DatabaseService()
    {
        DatabaseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppFolderName);
        DatabasePath = Path.Combine(DatabaseDirectory, DatabaseFileName);
    }

    public string DatabaseDirectory { get; }

    public string DatabasePath { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(DatabaseDirectory);

            await using var connection = GetConnection();
            await connection.OpenAsync(cancellationToken);

            var schemaPath = FindSchemaPath();
            var schemaSql = await File.ReadAllTextAsync(schemaPath, cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = schemaSql;
            await command.ExecuteNonQueryAsync(cancellationToken);

            await RunMigrationsAsync(connection, cancellationToken);
        }
        catch (Exception ex)
        {
            AppLog.DatabaseError(ex, "Database initialization failed", DatabasePath);
            throw;
        }
    }

    public SqliteConnection GetConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            ForeignKeys = true
        };

        return new SqliteConnection(builder.ToString());
    }

    private static string FindSchemaPath()
    {
        var baseDirectoryPath = Path.Combine(AppContext.BaseDirectory, SchemaRelativePath);
        if (File.Exists(baseDirectoryPath))
        {
            return baseDirectoryPath;
        }

        var currentDirectoryPath = Path.Combine(Environment.CurrentDirectory, SchemaRelativePath);
        if (File.Exists(currentDirectoryPath))
        {
            return currentDirectoryPath;
        }

        throw new FileNotFoundException("Could not find the SQLite schema file.", SchemaRelativePath);
    }

    private static async Task RunMigrationsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var schemaVersion = await GetSchemaVersionAsync(connection, cancellationToken);
        if (schemaVersion >= CurrentSchemaVersion)
        {
            return;
        }

        await using var transaction = connection.BeginTransaction();
        try
        {
            if (schemaVersion < 2)
            {
                await MigrateToVersion2Async(connection, transaction, cancellationToken);
                schemaVersion = 2;
            }

            if (schemaVersion < 3)
            {
                await MigrateToVersion3Async(connection, transaction, cancellationToken);
                schemaVersion = 3;
            }

            if (schemaVersion < 4)
            {
                await MigrateToVersion4Async(connection, transaction, cancellationToken);
                schemaVersion = 4;
            }

            await SetSchemaVersionAsync(connection, transaction, schemaVersion, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            AppLog.MigrationInfo("Database schema migrated to version {SchemaVersion}.", schemaVersion);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            AppLog.DatabaseError(ex, "Database migration failed");
            throw;
        }
    }

    private static async Task MigrateToVersion2Async(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var migratedPlacements = await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            INSERT OR IGNORE INTO resource_placements (
                resource_id,
                collection_id,
                category_id,
                created_at,
                updated_at
            )
            SELECT
                rc.resource_id,
                rc.collection_id,
                NULL,
                COALESCE(r.imported_at, @Now),
                @Now
            FROM resource_collections rc
            INNER JOIN resources r ON r.id = rc.resource_id
            INNER JOIN collections c ON c.id = rc.collection_id;
            """,
            cancellationToken,
            ("@Now", now));

        var migratedCollectionTags = await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            INSERT OR IGNORE INTO collection_tags (
                collection_id,
                name,
                color,
                created_at,
                updated_at
            )
            SELECT
                c.id,
                t.name,
                t.color,
                @Now,
                @Now
            FROM collections c
            CROSS JOIN tags t
            WHERE EXISTS (
                SELECT 1
                FROM resource_tags
            );
            """,
            cancellationToken,
            ("@Now", now));

        var migratedPlacementTags = await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            INSERT OR IGNORE INTO resource_placement_tags (
                placement_id,
                tag_id
            )
            SELECT DISTINCT
                rp.id,
                ct.id
            FROM resource_placements rp
            INNER JOIN resource_tags rt ON rt.resource_id = rp.resource_id
            INNER JOIN tags t ON t.id = rt.tag_id
            INNER JOIN collection_tags ct
                ON ct.collection_id = rp.collection_id
                AND ct.name = t.name;
            """,
            cancellationToken);

        var unmappedLegacyTagRelations = await ExecuteScalarLongAsync(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM resource_tags rt
            WHERE NOT EXISTS (
                SELECT 1
                FROM resource_placements rp
                WHERE rp.resource_id = rt.resource_id
            );
            """,
            cancellationToken);

        AppLog.MigrationInfo(
            "Migration v2 completed. Placements inserted: {PlacementCount}, collection tags inserted: {CollectionTagCount}, placement tag links inserted: {PlacementTagCount}.",
            migratedPlacements,
            migratedCollectionTags,
            migratedPlacementTags);

        if (unmappedLegacyTagRelations > 0)
        {
            AppLog.MigrationWarning(
                "Migration v2 left {UnmappedCount} legacy resource_tags rows unmapped because their resources are not in any collection placement. Legacy rows were preserved.",
                unmappedLegacyTagRelations);
        }
    }

    private static async Task MigrateToVersion3Async(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var hasSortOrder = await ColumnExistsAsync(
            connection,
            transaction,
            "collection_tags",
            "sort_order",
            cancellationToken);

        if (!hasSortOrder)
        {
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                ALTER TABLE collection_tags
                ADD COLUMN sort_order INTEGER NOT NULL DEFAULT 0;
                """,
                cancellationToken);

            AppLog.MigrationInfo("Migration v3 added collection_tags.sort_order.");
        }

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            CREATE INDEX IF NOT EXISTS idx_collection_tags_sort
                ON collection_tags(collection_id, sort_order, name);
            """,
            cancellationToken);
    }

    private static async Task MigrateToVersion4Async(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            CREATE INDEX IF NOT EXISTS idx_resources_path ON resources(path);
            CREATE INDEX IF NOT EXISTS idx_resources_modified_at ON resources(modified_at);
            CREATE INDEX IF NOT EXISTS idx_resources_imported_at ON resources(imported_at);
            CREATE INDEX IF NOT EXISTS idx_resources_size_bytes ON resources(size_bytes);

            CREATE INDEX IF NOT EXISTS idx_collection_categories_collection_id
                ON collection_categories(collection_id);
            CREATE INDEX IF NOT EXISTS idx_collection_categories_sort
                ON collection_categories(collection_id, sort_order, name);

            CREATE INDEX IF NOT EXISTS idx_collection_tags_collection_id
                ON collection_tags(collection_id);
            CREATE INDEX IF NOT EXISTS idx_collection_tags_sort
                ON collection_tags(collection_id, sort_order, name);

            CREATE INDEX IF NOT EXISTS idx_resource_placements_resource_id
                ON resource_placements(resource_id);
            CREATE INDEX IF NOT EXISTS idx_resource_placements_collection_id
                ON resource_placements(collection_id);
            CREATE INDEX IF NOT EXISTS idx_resource_placements_category_id
                ON resource_placements(category_id);
            CREATE INDEX IF NOT EXISTS idx_resource_placements_collection_category_resource
                ON resource_placements(collection_id, category_id, resource_id);
            CREATE INDEX IF NOT EXISTS idx_resource_placements_resource_collection_category
                ON resource_placements(resource_id, collection_id, category_id);

            CREATE INDEX IF NOT EXISTS idx_resource_placement_tags_placement_id
                ON resource_placement_tags(placement_id);
            CREATE INDEX IF NOT EXISTS idx_resource_placement_tags_tag_id
                ON resource_placement_tags(tag_id);
            CREATE INDEX IF NOT EXISTS idx_resource_placement_tags_tag_placement
                ON resource_placement_tags(tag_id, placement_id);
            """,
            cancellationToken);

        AppLog.MigrationInfo("Migration v4 added indexes for collection categories, scoped tags, placements, placement tags, and resource sorting.");
    }

    private static async Task<int> GetSchemaVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT value
            FROM app_settings
            WHERE key = @Key;
            """;
        command.Parameters.AddWithValue("@Key", SchemaVersionSettingKey);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null && int.TryParse(result.ToString(), out var version) ? version : 1;
    }

    private static Task SetSchemaVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int schemaVersion,
        CancellationToken cancellationToken)
    {
        return ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            INSERT INTO app_settings (key, value, updated_at)
            VALUES (@Key, @Value, @UpdatedAt)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                updated_at = excluded.updated_at;
            """,
            cancellationToken,
            ("@Key", SchemaVersionSettingKey),
            ("@Value", schemaVersion.ToString()),
            ("@UpdatedAt", DateTime.UtcNow));
    }

    private static async Task<int> ExecuteNonQueryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ExecuteScalarLongAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null ? 0 : Convert.ToInt64(result);
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({tableName});";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
