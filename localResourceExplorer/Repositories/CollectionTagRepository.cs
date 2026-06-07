using Dapper;
using LocalResourceExplorer.Models;
using LocalResourceExplorer.Services;

namespace LocalResourceExplorer.Repositories;

public sealed class CollectionTagRepository
{
    private const string CollectionTagColumns = """
        id AS Id,
        collection_id AS CollectionId,
        name AS Name,
        color AS Color,
        sort_order AS SortOrder,
        created_at AS CreatedAt,
        updated_at AS UpdatedAt
        """;

    private readonly DatabaseService databaseService;

    public CollectionTagRepository(DatabaseService databaseService)
    {
        this.databaseService = databaseService;
    }

    public async Task<IReadOnlyList<CollectionTag>> GetByCollectionAsync(long collectionId)
    {
        var sql = $"""
            SELECT
                {CollectionTagColumns}
            FROM collection_tags
            WHERE collection_id = @CollectionId
            ORDER BY sort_order ASC, name COLLATE NOCASE ASC, id ASC;
            """;

        await using var connection = databaseService.GetConnection();
        var tags = await connection.QueryAsync<CollectionTag>(sql, new { CollectionId = collectionId });
        return tags.AsList();
    }

    public async Task<long> CreateAsync(long collectionId, string name, string? color = null, int sortOrder = 0)
    {
        const string sql = """
            INSERT INTO collection_tags (
                collection_id,
                name,
                color,
                sort_order,
                created_at,
                updated_at
            )
            VALUES (
                @CollectionId,
                @Name,
                @Color,
                @SortOrder,
                @CreatedAt,
                @UpdatedAt
            );

            SELECT last_insert_rowid();
            """;

        var now = DateTime.UtcNow;

        await using var connection = databaseService.GetConnection();
        return await connection.ExecuteScalarAsync<long>(sql, new
        {
            CollectionId = collectionId,
            Name = name,
            Color = color,
            SortOrder = sortOrder,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    public async Task<int> RenameAsync(long tagId, string name)
    {
        const string sql = """
            UPDATE collection_tags
            SET name = @Name,
                updated_at = @UpdatedAt
            WHERE id = @TagId;
            """;

        await using var connection = databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new
        {
            TagId = tagId,
            Name = name,
            UpdatedAt = DateTime.UtcNow
        });
    }

    public async Task<int> DeleteAsync(long tagId)
    {
        const string sql = """
            DELETE FROM collection_tags
            WHERE id = @TagId;
            """;

        await using var connection = databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new { TagId = tagId });
    }

    public async Task<int> UpdateColorAsync(long tagId, string? color)
    {
        const string sql = """
            UPDATE collection_tags
            SET color = @Color,
                updated_at = @UpdatedAt
            WHERE id = @TagId;
            """;

        await using var connection = databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new
        {
            TagId = tagId,
            Color = color,
            UpdatedAt = DateTime.UtcNow
        });
    }

    public async Task<int> UpdateSortOrderAsync(long tagId, int sortOrder)
    {
        const string sql = """
            UPDATE collection_tags
            SET sort_order = @SortOrder,
                updated_at = @UpdatedAt
            WHERE id = @TagId;
            """;

        await using var connection = databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new
        {
            TagId = tagId,
            SortOrder = sortOrder,
            UpdatedAt = DateTime.UtcNow
        });
    }

    public async Task<bool> ExistsByNameAsync(long collectionId, string name, long? excludeTagId = null)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM collection_tags
            WHERE collection_id = @CollectionId
                AND name = @Name COLLATE NOCASE
                AND (@ExcludeTagId IS NULL OR id <> @ExcludeTagId);
            """;

        await using var connection = databaseService.GetConnection();
        var count = await connection.ExecuteScalarAsync<int>(sql, new
        {
            CollectionId = collectionId,
            Name = name,
            ExcludeTagId = excludeTagId
        });
        return count > 0;
    }
}
