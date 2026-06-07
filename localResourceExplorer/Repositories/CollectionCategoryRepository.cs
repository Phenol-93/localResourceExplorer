using Dapper;
using LocalResourceExplorer.Models;
using LocalResourceExplorer.Services;

namespace LocalResourceExplorer.Repositories;

public sealed class CollectionCategoryRepository
{
    private const string CategoryColumns = """
        id AS Id,
        collection_id AS CollectionId,
        name AS Name,
        description AS Description,
        sort_order AS SortOrder,
        created_at AS CreatedAt,
        updated_at AS UpdatedAt
        """;

    private readonly DatabaseService databaseService;

    public CollectionCategoryRepository(DatabaseService databaseService)
    {
        this.databaseService = databaseService;
    }

    public async Task<IReadOnlyList<CollectionCategory>> GetByCollectionAsync(long collectionId)
    {
        var sql = $"""
            SELECT
                {CategoryColumns}
            FROM collection_categories
            WHERE collection_id = @CollectionId
            ORDER BY sort_order ASC, name COLLATE NOCASE ASC, id ASC;
            """;

        await using var connection = databaseService.GetConnection();
        var categories = await connection.QueryAsync<CollectionCategory>(sql, new { CollectionId = collectionId });
        return categories.AsList();
    }

    public async Task<long> CreateAsync(long collectionId, string name, string? description = null, int sortOrder = 0)
    {
        const string sql = """
            INSERT INTO collection_categories (
                collection_id,
                name,
                description,
                sort_order,
                created_at,
                updated_at
            )
            VALUES (
                @CollectionId,
                @Name,
                @Description,
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
            Description = description,
            SortOrder = sortOrder,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    public async Task<int> RenameAsync(long categoryId, string name)
    {
        const string sql = """
            UPDATE collection_categories
            SET name = @Name,
                updated_at = @UpdatedAt
            WHERE id = @CategoryId;
            """;

        await using var connection = databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new
        {
            CategoryId = categoryId,
            Name = name,
            UpdatedAt = DateTime.UtcNow
        });
    }

    public async Task<int> DeleteAsync(long categoryId)
    {
        const string sql = """
            DELETE FROM collection_categories
            WHERE id = @CategoryId;
            """;

        await using var connection = databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new { CategoryId = categoryId });
    }

    public async Task<int> UpdateSortOrderAsync(long categoryId, int sortOrder)
    {
        const string sql = """
            UPDATE collection_categories
            SET sort_order = @SortOrder,
                updated_at = @UpdatedAt
            WHERE id = @CategoryId;
            """;

        await using var connection = databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new
        {
            CategoryId = categoryId,
            SortOrder = sortOrder,
            UpdatedAt = DateTime.UtcNow
        });
    }

    public async Task<bool> ExistsByNameAsync(long collectionId, string name, long? excludeCategoryId = null)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM collection_categories
            WHERE collection_id = @CollectionId
                AND name = @Name COLLATE NOCASE
                AND (@ExcludeCategoryId IS NULL OR id <> @ExcludeCategoryId);
            """;

        await using var connection = databaseService.GetConnection();
        var count = await connection.ExecuteScalarAsync<int>(sql, new
        {
            CollectionId = collectionId,
            Name = name,
            ExcludeCategoryId = excludeCategoryId
        });
        return count > 0;
    }
}
