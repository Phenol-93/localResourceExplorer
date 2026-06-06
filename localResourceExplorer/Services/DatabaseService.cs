using System.IO;
using Microsoft.Data.Sqlite;

namespace LocalResourceExplorer.Services;

public sealed class DatabaseService
{
    private const string AppFolderName = "LocalResourceExplorer";
    private const string DatabaseFileName = "library.db";
    private const string SchemaRelativePath = "Data/schema.sql";

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
}
