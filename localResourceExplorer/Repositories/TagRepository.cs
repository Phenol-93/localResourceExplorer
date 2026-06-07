using Dapper;
using LocalResourceExplorer.Models;
using LocalResourceExplorer.Services;

namespace LocalResourceExplorer.Repositories;

public sealed class TagRepository
{
    private const string TagColumns = """
        id AS Id,
        name AS Name,
        color AS Color,
        created_at AS CreatedAt,
        updated_at AS UpdatedAt
        """;

    private readonly DatabaseService _databaseService;

    public TagRepository(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<long> CreateAsync(string name, string? color = null)
    {
        const string sql = """
            INSERT INTO tags (
                name,
                color,
                created_at,
                updated_at
            )
            VALUES (
                @Name,
                @Color,
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
            Color = color,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    public async Task<int> DeleteAsync(long tagId)
    {
        const string sql = """
            DELETE FROM tags
            WHERE id = @TagId;
            """;

        await using var connection = _databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new { TagId = tagId });
    }

    public async Task<int> UpdateColorAsync(long tagId, string? color)
    {
        const string sql = """
            UPDATE tags
            SET color = @Color,
                updated_at = @UpdatedAt
            WHERE id = @TagId;
            """;

        await using var connection = _databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new
        {
            TagId = tagId,
            Color = color,
            UpdatedAt = DateTime.UtcNow
        });
    }

    public async Task<IReadOnlyList<Tag>> GetAllAsync()
    {
        var sql = $"""
            SELECT
                {TagColumns}
            FROM tags
            ORDER BY name COLLATE NOCASE ASC, id ASC;
            """;

        await using var connection = _databaseService.GetConnection();
        var tags = await connection.QueryAsync<Tag>(sql);
        return tags.AsList();
    }

    public async Task<IReadOnlyList<TagSummary>> GetAllWithResourceCountsAsync()
    {
        const string sql = """
            SELECT
                t.id AS Id,
                t.name AS Name,
                COALESCE(t.color, '#EEF6FF') AS Color,
                COUNT(rt.resource_id) AS ResourceCount,
                t.created_at AS CreatedAt,
                t.updated_at AS UpdatedAt
            FROM tags t
            LEFT JOIN resource_tags rt ON rt.tag_id = t.id
            GROUP BY t.id, t.name, t.color, t.created_at, t.updated_at
            ORDER BY t.name COLLATE NOCASE ASC, t.id ASC;
            """;

        await using var connection = _databaseService.GetConnection();
        var tags = await connection.QueryAsync<TagSummary>(sql);
        return tags.AsList();
    }

    public async Task<int> AddResourceAsync(long resourceId, long tagId)
    {
        const string sql = """
            INSERT OR IGNORE INTO resource_tags (
                resource_id,
                tag_id
            )
            VALUES (
                @ResourceId,
                @TagId
            );
            """;

        await using var connection = _databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new
        {
            ResourceId = resourceId,
            TagId = tagId
        });
    }

    public async Task<int> RemoveResourceAsync(long resourceId, long tagId)
    {
        const string sql = """
            DELETE FROM resource_tags
            WHERE resource_id = @ResourceId
                AND tag_id = @TagId;
            """;

        await using var connection = _databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new
        {
            ResourceId = resourceId,
            TagId = tagId
        });
    }

    public async Task<IReadOnlyList<Tag>> GetByResourceAsync(long resourceId)
    {
        var sql = $"""
            SELECT
                {TagColumns}
            FROM tags t
            INNER JOIN resource_tags rt ON rt.tag_id = t.id
            WHERE rt.resource_id = @ResourceId
            ORDER BY t.name COLLATE NOCASE ASC, t.id ASC;
            """;

        await using var connection = _databaseService.GetConnection();
        var tags = await connection.QueryAsync<Tag>(sql, new { ResourceId = resourceId });
        return tags.AsList();
    }
}
