using Dapper;
using LocalResourceExplorer.Models;
using LocalResourceExplorer.Services;

namespace LocalResourceExplorer.Repositories;

public sealed class ResourceRepository
{
    private const string ResourceColumns = """
        id AS Id,
        title AS Title,
        original_name AS OriginalName,
        path AS Path,
        extension AS Extension,
        size_bytes AS SizeBytes,
        modified_at AS ModifiedAt,
        imported_at AS ImportedAt,
        duration_ms AS DurationMs,
        note AS Note,
        is_favorite AS IsFavorite,
        is_missing AS IsMissing,
        last_opened_at AS LastOpenedAt,
        created_at AS CreatedAt,
        updated_at AS UpdatedAt
        """;

    private static readonly IReadOnlyDictionary<string, string> SortColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = "title",
            ["name"] = "title",
            ["original_name"] = "original_name",
            ["originalname"] = "original_name",
            ["path"] = "path",
            ["size_bytes"] = "size_bytes",
            ["sizebytes"] = "size_bytes",
            ["modified_at"] = "modified_at",
            ["modifiedat"] = "modified_at",
            ["imported_at"] = "imported_at",
            ["importedat"] = "imported_at",
            ["duration_ms"] = "duration_ms",
            ["durationms"] = "duration_ms",
            ["last_opened_at"] = "last_opened_at",
            ["lastopenedat"] = "last_opened_at"
        };

    private readonly DatabaseService _databaseService;

    public ResourceRepository(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<int> InsertOrIgnoreAsync(ResourceItem item)
    {
        const string sql = """
            INSERT OR IGNORE INTO resources (
                title,
                original_name,
                path,
                extension,
                size_bytes,
                modified_at,
                imported_at,
                duration_ms,
                note,
                is_favorite,
                is_missing,
                last_opened_at,
                created_at,
                updated_at
            )
            VALUES (
                @Title,
                @OriginalName,
                @Path,
                @Extension,
                @SizeBytes,
                @ModifiedAt,
                @ImportedAt,
                @DurationMs,
                @Note,
                @IsFavorite,
                @IsMissing,
                @LastOpenedAt,
                @CreatedAt,
                @UpdatedAt
            );
            """;

        await using var connection = _databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, ToParameters(item));
    }

    public async Task<IReadOnlyList<ResourceItem>> GetAllAsync()
    {
        var sql = $"""
            SELECT
                {ResourceColumns}
            FROM resources
            ORDER BY imported_at DESC, id DESC;
            """;

        await using var connection = _databaseService.GetConnection();
        var resources = await connection.QueryAsync<ResourceItem>(sql);
        return resources.AsList();
    }

    public async Task<IReadOnlyList<ResourceItem>> GetMissingCheckItemsAsync()
    {
        const string sql = """
            SELECT
                id AS Id,
                path AS Path,
                is_missing AS IsMissing
            FROM resources;
            """;

        await using var connection = _databaseService.GetConnection();
        var resources = await connection.QueryAsync<ResourceItem>(sql);
        return resources.AsList();
    }

    public async Task<IReadOnlyList<ResourceItem>> SearchAsync(
        string keyword,
        string sortBy,
        bool descending,
        long? collectionId = null,
        long? tagId = null,
        bool favoritesOnly = false,
        bool unorganizedOnly = false,
        bool recentOpenedOnly = false)
    {
        var sortColumn = GetSortColumn(sortBy);
        var sortDirection = descending ? "DESC" : "ASC";
        var trimmedKeyword = keyword.Trim();
        var hasKeyword = !string.IsNullOrWhiteSpace(trimmedKeyword);

        var sql = $"""
            SELECT
                {ResourceColumns}
            FROM resources r
            WHERE (@CollectionId IS NULL OR EXISTS (
                    SELECT 1
                    FROM resource_collections selected_rc
                    WHERE selected_rc.resource_id = r.id
                        AND selected_rc.collection_id = @CollectionId
                ))
                AND (@TagId IS NULL OR EXISTS (
                    SELECT 1
                    FROM resource_tags selected_rt
                    WHERE selected_rt.resource_id = r.id
                        AND selected_rt.tag_id = @TagId
                ))
                AND (@FavoritesOnly = 0 OR r.is_favorite = 1)
                AND (@UnorganizedOnly = 0 OR NOT EXISTS (
                    SELECT 1
                    FROM resource_collections inbox_rc
                    WHERE inbox_rc.resource_id = r.id
                ))
                AND (@RecentOpenedOnly = 0 OR r.last_opened_at IS NOT NULL)
                AND (
                    @HasKeyword = 0
                    OR r.title LIKE @Keyword
                    OR r.original_name LIKE @Keyword
                    OR r.path LIKE @Keyword
                    OR r.note LIKE @Keyword
                    OR EXISTS (
                        SELECT 1
                        FROM resource_tags rt
                        INNER JOIN tags t ON t.id = rt.tag_id
                        WHERE rt.resource_id = r.id
                            AND t.name LIKE @Keyword
                    )
                    OR EXISTS (
                        SELECT 1
                        FROM resource_collections rc
                        INNER JOIN collections c ON c.id = rc.collection_id
                        WHERE rc.resource_id = r.id
                            AND c.name LIKE @Keyword
                    )
                )
            ORDER BY {sortColumn} {sortDirection}, r.id DESC;
            """;

        var parameters = new
        {
            HasKeyword = hasKeyword ? 1 : 0,
            Keyword = $"%{trimmedKeyword}%",
            CollectionId = collectionId,
            TagId = tagId,
            FavoritesOnly = favoritesOnly ? 1 : 0,
            UnorganizedOnly = unorganizedOnly ? 1 : 0,
            RecentOpenedOnly = recentOpenedOnly ? 1 : 0
        };

        await using var connection = _databaseService.GetConnection();
        var resources = await connection.QueryAsync<ResourceItem>(sql, parameters);
        return resources.AsList();
    }

    public async Task<int> UpdateNoteAsync(long resourceId, string note)
    {
        const string sql = """
            UPDATE resources
            SET note = @Note,
                updated_at = @UpdatedAt
            WHERE id = @ResourceId;
            """;

        await using var connection = _databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new
        {
            ResourceId = resourceId,
            Note = note,
            UpdatedAt = DateTime.UtcNow
        });
    }

    public async Task<int> UpdateTitleAsync(long resourceId, string title)
    {
        const string sql = """
            UPDATE resources
            SET title = @Title,
                updated_at = @UpdatedAt
            WHERE id = @ResourceId;
            """;

        await using var connection = _databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new
        {
            ResourceId = resourceId,
            Title = title,
            UpdatedAt = DateTime.UtcNow
        });
    }

    public async Task<int> UpdateFavoriteAsync(long resourceId, bool isFavorite)
    {
        const string sql = """
            UPDATE resources
            SET is_favorite = @IsFavorite,
                updated_at = @UpdatedAt
            WHERE id = @ResourceId;
            """;

        await using var connection = _databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new
        {
            ResourceId = resourceId,
            IsFavorite = isFavorite ? 1 : 0,
            UpdatedAt = DateTime.UtcNow
        });
    }

    public async Task<int> UpdateLastOpenedAsync(long resourceId, DateTime lastOpenedAt)
    {
        const string sql = """
            UPDATE resources
            SET last_opened_at = @LastOpenedAt,
                updated_at = @UpdatedAt
            WHERE id = @ResourceId;
            """;

        await using var connection = _databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new
        {
            ResourceId = resourceId,
            LastOpenedAt = lastOpenedAt,
            UpdatedAt = DateTime.UtcNow
        });
    }

    public async Task<int> UpdateDurationByPathAsync(string path, long durationMs)
    {
        const string sql = """
            UPDATE resources
            SET duration_ms = @DurationMs,
                updated_at = @UpdatedAt
            WHERE path = @Path;
            """;

        await using var connection = _databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new
        {
            Path = path,
            DurationMs = durationMs,
            UpdatedAt = DateTime.UtcNow
        });
    }

    public async Task<int> MarkMissingAsync(long resourceId, bool isMissing)
    {
        const string sql = """
            UPDATE resources
            SET is_missing = @IsMissing,
                updated_at = @UpdatedAt
            WHERE id = @ResourceId;
            """;

        await using var connection = _databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new
        {
            ResourceId = resourceId,
            IsMissing = isMissing ? 1 : 0,
            UpdatedAt = DateTime.UtcNow
        });
    }

    public async Task<int> DeleteRecordAsync(long resourceId)
    {
        const string sql = """
            DELETE FROM resources
            WHERE id = @ResourceId;
            """;

        await using var connection = _databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new { ResourceId = resourceId });
    }

    private static string GetSortColumn(string sortBy)
    {
        if (SortColumns.TryGetValue(sortBy.Trim(), out var sortColumn))
        {
            return $"r.{sortColumn}";
        }

        return "r.imported_at";
    }

    private static object ToParameters(ResourceItem item)
    {
        var now = DateTime.UtcNow;

        return new
        {
            item.Title,
            item.OriginalName,
            item.Path,
            item.Extension,
            item.SizeBytes,
            item.ModifiedAt,
            ImportedAt = item.ImportedAt == default ? now : item.ImportedAt,
            item.DurationMs,
            item.Note,
            IsFavorite = item.IsFavorite ? 1 : 0,
            IsMissing = item.IsMissing ? 1 : 0,
            item.LastOpenedAt,
            CreatedAt = item.CreatedAt == default ? now : item.CreatedAt,
            UpdatedAt = item.UpdatedAt == default ? now : item.UpdatedAt
        };
    }
}
