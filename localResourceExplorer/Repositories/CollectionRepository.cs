using Dapper;
using LocalResourceExplorer.Services;
using CollectionModel = LocalResourceExplorer.Models.Collection;

namespace LocalResourceExplorer.Repositories;

public sealed class CollectionRepository
{
    private const string CollectionColumns = """
        id AS Id,
        name AS Name,
        description AS Description,
        sort_order AS SortOrder,
        created_at AS CreatedAt,
        updated_at AS UpdatedAt
        """;

    private readonly DatabaseService _databaseService;

    public CollectionRepository(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<long> CreateAsync(string name, string? description = null, int sortOrder = 0)
    {
        const string sql = """
            INSERT INTO collections (
                name,
                description,
                sort_order,
                created_at,
                updated_at
            )
            VALUES (
                @Name,
                @Description,
                @SortOrder,
                @CreatedAt,
                @UpdatedAt
            );

            SELECT last_insert_rowid();
            """;

        var now = DateTime.UtcNow;

        await using var connection = _databaseService.GetConnection();
        return await connection.ExecuteScalarAsync<long>(sql, new
        {
            Name = name,
            Description = description,
            SortOrder = sortOrder,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    public async Task<int> RenameAsync(long collectionId, string name)
    {
        const string sql = """
            UPDATE collections
            SET name = @Name,
                updated_at = @UpdatedAt
            WHERE id = @CollectionId;
            """;

        await using var connection = _databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new
        {
            CollectionId = collectionId,
            Name = name,
            UpdatedAt = DateTime.UtcNow
        });
    }

    public async Task<int> DeleteAsync(long collectionId)
    {
        const string sql = """
            DELETE FROM collections
            WHERE id = @CollectionId;
            """;

        await using var connection = _databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new { CollectionId = collectionId });
    }

    public async Task<IReadOnlyList<CollectionModel>> GetAllAsync()
    {
        var sql = $"""
            SELECT
                {CollectionColumns}
            FROM collections
            ORDER BY sort_order ASC, name COLLATE NOCASE ASC, id ASC;
            """;

        await using var connection = _databaseService.GetConnection();
        var collections = await connection.QueryAsync<CollectionModel>(sql);
        return collections.AsList();
    }

    public async Task<int> AddResourceAsync(long resourceId, long collectionId)
    {
        const string sql = """
            INSERT OR IGNORE INTO resource_collections (
                resource_id,
                collection_id
            )
            VALUES (
                @ResourceId,
                @CollectionId
            );
            """;

        await using var connection = _databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new
        {
            ResourceId = resourceId,
            CollectionId = collectionId
        });
    }

    public async Task<int> RemoveResourceAsync(long resourceId, long collectionId)
    {
        const string sql = """
            DELETE FROM resource_collections
            WHERE resource_id = @ResourceId
                AND collection_id = @CollectionId;
            """;

        await using var connection = _databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new
        {
            ResourceId = resourceId,
            CollectionId = collectionId
        });
    }

    public async Task<IReadOnlyList<CollectionModel>> GetByResourceAsync(long resourceId)
    {
        var sql = $"""
            SELECT
                {CollectionColumns}
            FROM collections c
            INNER JOIN resource_collections rc ON rc.collection_id = c.id
            WHERE rc.resource_id = @ResourceId
            ORDER BY c.sort_order ASC, c.name COLLATE NOCASE ASC, c.id ASC;
            """;

        await using var connection = _databaseService.GetConnection();
        var collections = await connection.QueryAsync<CollectionModel>(sql, new { ResourceId = resourceId });
        return collections.AsList();
    }
}
