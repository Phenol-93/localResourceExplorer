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

    public async Task<IReadOnlyList<ResourceItem>> GetByPathsAsync(IEnumerable<string> paths)
    {
        var pathArray = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (pathArray.Length == 0)
        {
            return [];
        }

        var sql = $"""
            SELECT
                {ResourceColumns}
            FROM resources
            WHERE path IN @Paths
            ORDER BY imported_at DESC, id DESC;
            """;

        await using var connection = _databaseService.GetConnection();
        var resources = await connection.QueryAsync<ResourceItem>(sql, new { Paths = pathArray });
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

    public async Task<IReadOnlyList<ResourceItem>> SearchWithPlacementSummaryAsync(
        string keyword,
        string sortBy,
        bool descending,
        long? collectionId = null,
        long? categoryId = null,
        long? collectionTagId = null,
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
                    FROM resource_placements selected_rp
                    WHERE selected_rp.resource_id = r.id
                        AND selected_rp.collection_id = @CollectionId
                ))
                AND (@CategoryId IS NULL OR EXISTS (
                    SELECT 1
                    FROM resource_placements selected_category_rp
                    WHERE selected_category_rp.resource_id = r.id
                        AND selected_category_rp.collection_id = @CollectionId
                        AND selected_category_rp.category_id = @CategoryId
                ))
                AND (@CollectionTagId IS NULL OR EXISTS (
                    SELECT 1
                    FROM resource_placements selected_tag_rp
                    INNER JOIN resource_placement_tags selected_rpt
                        ON selected_rpt.placement_id = selected_tag_rp.id
                    INNER JOIN collection_tags selected_ct
                        ON selected_ct.id = selected_rpt.tag_id
                    WHERE selected_tag_rp.resource_id = r.id
                        AND selected_tag_rp.collection_id = @CollectionId
                        AND selected_ct.collection_id = @CollectionId
                        AND selected_ct.id = @CollectionTagId
                ))
                AND (@FavoritesOnly = 0 OR r.is_favorite = 1)
                AND (@UnorganizedOnly = 0 OR NOT EXISTS (
                    SELECT 1
                    FROM resource_placements inbox_rp
                    WHERE inbox_rp.resource_id = r.id
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
                        FROM resource_placements keyword_rp
                        INNER JOIN collections keyword_c
                            ON keyword_c.id = keyword_rp.collection_id
                        LEFT JOIN collection_categories keyword_cc
                            ON keyword_cc.id = keyword_rp.category_id
                        WHERE keyword_rp.resource_id = r.id
                            AND (@CollectionId IS NULL OR keyword_rp.collection_id = @CollectionId)
                            AND (@CategoryId IS NULL OR keyword_rp.category_id = @CategoryId)
                            AND (
                                keyword_c.name LIKE @Keyword
                                OR keyword_cc.name LIKE @Keyword
                            )
                    )
                    OR EXISTS (
                        SELECT 1
                        FROM resource_placements keyword_tag_rp
                        INNER JOIN resource_placement_tags keyword_rpt
                            ON keyword_rpt.placement_id = keyword_tag_rp.id
                        INNER JOIN collection_tags keyword_ct
                            ON keyword_ct.id = keyword_rpt.tag_id
                        WHERE keyword_tag_rp.resource_id = r.id
                            AND (@CollectionId IS NULL OR keyword_tag_rp.collection_id = @CollectionId)
                            AND (@CategoryId IS NULL OR keyword_tag_rp.category_id = @CategoryId)
                            AND (@CollectionTagId IS NULL OR keyword_ct.id = @CollectionTagId)
                            AND keyword_ct.collection_id = keyword_tag_rp.collection_id
                            AND keyword_ct.name LIKE @Keyword
                    )
                )
            ORDER BY {sortColumn} {sortDirection}, r.id DESC;
            """;

        var parameters = new
        {
            HasKeyword = hasKeyword ? 1 : 0,
            Keyword = $"%{trimmedKeyword}%",
            CollectionId = collectionId,
            CategoryId = categoryId,
            CollectionTagId = collectionTagId,
            FavoritesOnly = favoritesOnly ? 1 : 0,
            UnorganizedOnly = unorganizedOnly ? 1 : 0,
            RecentOpenedOnly = recentOpenedOnly ? 1 : 0
        };

        await using var connection = _databaseService.GetConnection();
        var resourceList = (await connection.QueryAsync<ResourceItem>(sql, parameters)).AsList();
        if (resourceList.Count == 0)
        {
            return resourceList;
        }

        var resourceIds = resourceList.Select(resource => resource.Id).ToArray();
        var placementRows = (await connection.QueryAsync<ResourcePlacementSummaryRow>(
            """
            SELECT
                rp.id AS PlacementId,
                rp.resource_id AS ResourceId,
                rp.collection_id AS CollectionId,
                c.name AS CollectionName,
                rp.category_id AS CategoryId,
                cc.name AS CategoryName
            FROM resource_placements rp
            INNER JOIN collections c ON c.id = rp.collection_id
            LEFT JOIN collection_categories cc ON cc.id = rp.category_id
            WHERE rp.resource_id IN @ResourceIds
                AND (@ContextCollectionId IS NULL OR rp.collection_id = @ContextCollectionId)
            ORDER BY c.name COLLATE NOCASE ASC,
                cc.sort_order ASC,
                cc.name COLLATE NOCASE ASC,
                rp.id ASC;
            """,
            new
            {
                ResourceIds = resourceIds,
                ContextCollectionId = collectionId
            })).AsList();

        var tagRows = (await connection.QueryAsync<ResourcePlacementTagSummaryRow>(
            """
            SELECT
                rp.resource_id AS ResourceId,
                rp.id AS PlacementId,
                ct.collection_id AS CollectionId,
                ct.name AS TagName
            FROM resource_placements rp
            INNER JOIN resource_placement_tags rpt ON rpt.placement_id = rp.id
            INNER JOIN collection_tags ct ON ct.id = rpt.tag_id
            WHERE rp.resource_id IN @ResourceIds
                AND (@ContextCollectionId IS NULL OR rp.collection_id = @ContextCollectionId)
            ORDER BY ct.sort_order ASC,
                ct.name COLLATE NOCASE ASC,
                ct.id ASC;
            """,
            new
            {
                ResourceIds = resourceIds,
                ContextCollectionId = collectionId
            })).AsList();

        ApplyPlacementSummaries(resourceList, placementRows, tagRows, collectionId);
        return resourceList;
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

    private static void ApplyPlacementSummaries(
        IReadOnlyList<ResourceItem> resources,
        IReadOnlyList<ResourcePlacementSummaryRow> placements,
        IReadOnlyList<ResourcePlacementTagSummaryRow> tags,
        long? contextCollectionId)
    {
        var placementsByResource = placements
            .GroupBy(placement => placement.ResourceId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var tagsByResource = tags
            .GroupBy(tag => tag.ResourceId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        foreach (var resource in resources)
        {
            var resourcePlacements = placementsByResource.GetValueOrDefault(resource.Id) ?? [];
            var resourceTags = tagsByResource.GetValueOrDefault(resource.Id) ?? [];

            resource.CollectionDisplay = BuildCollectionDisplay(resourcePlacements);
            resource.CategoryDisplay = BuildCategoryDisplay(resourcePlacements, contextCollectionId);
            resource.TagDisplay = BuildTagDisplay(resourceTags, contextCollectionId);
        }
    }

    private static string BuildCollectionDisplay(IReadOnlyList<ResourcePlacementSummaryRow> placements)
    {
        var collectionNames = placements
            .Select(placement => placement.CollectionName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return collectionNames.Length == 0 ? "—" : string.Join("、", collectionNames);
    }

    private static string BuildCategoryDisplay(
        IReadOnlyList<ResourcePlacementSummaryRow> placements,
        long? contextCollectionId)
    {
        var categoryNames = placements
            .Select(placement => placement.CategoryName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (categoryNames.Length == 0)
        {
            return "—";
        }

        if (categoryNames.Length == 1)
        {
            return categoryNames[0]!;
        }

        return "多个";
    }

    private static string BuildTagDisplay(
        IReadOnlyList<ResourcePlacementTagSummaryRow> tags,
        long? contextCollectionId)
    {
        if (tags.Count == 0)
        {
            return "—";
        }

        if (contextCollectionId is null)
        {
            var collectionCount = tags
                .Select(tag => tag.CollectionId)
                .Distinct()
                .Count();
            if (collectionCount > 1)
            {
                return "多个集合标签";
            }
        }

        var tagNames = tags
            .Select(tag => tag.TagName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return tagNames.Length == 0 ? "—" : string.Join("、", tagNames);
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

    private sealed class ResourcePlacementSummaryRow
    {
        public long PlacementId { get; set; }

        public long ResourceId { get; set; }

        public long CollectionId { get; set; }

        public string CollectionName { get; set; } = string.Empty;

        public long? CategoryId { get; set; }

        public string? CategoryName { get; set; }
    }

    private sealed class ResourcePlacementTagSummaryRow
    {
        public long ResourceId { get; set; }

        public long PlacementId { get; set; }

        public long CollectionId { get; set; }

        public string TagName { get; set; } = string.Empty;
    }
}
