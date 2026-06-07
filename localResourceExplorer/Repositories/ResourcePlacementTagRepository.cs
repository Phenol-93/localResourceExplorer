using Dapper;
using LocalResourceExplorer.Models;
using LocalResourceExplorer.Services;

namespace LocalResourceExplorer.Repositories;

public sealed class ResourcePlacementTagRepository
{
    private const string PlacementTagColumns = """
        rpt.placement_id AS PlacementId,
        ct.id AS TagId,
        ct.name AS TagName,
        ct.color AS Color
        """;

    private const string ResourceColumns = """
        r.id AS Id,
        r.title AS Title,
        r.original_name AS OriginalName,
        r.path AS Path,
        r.extension AS Extension,
        r.size_bytes AS SizeBytes,
        r.modified_at AS ModifiedAt,
        r.imported_at AS ImportedAt,
        r.duration_ms AS DurationMs,
        r.note AS Note,
        r.is_favorite AS IsFavorite,
        r.is_missing AS IsMissing,
        r.last_opened_at AS LastOpenedAt,
        r.created_at AS CreatedAt,
        r.updated_at AS UpdatedAt
        """;

    private readonly DatabaseService databaseService;

    public ResourcePlacementTagRepository(DatabaseService databaseService)
    {
        this.databaseService = databaseService;
    }

    public async Task<int> AddTagAsync(long placementId, long tagId)
    {
        const string sql = """
            INSERT OR IGNORE INTO resource_placement_tags (
                placement_id,
                tag_id
            )
            VALUES (
                @PlacementId,
                @TagId
            );
            """;

        await using var connection = databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new
        {
            PlacementId = placementId,
            TagId = tagId
        });
    }

    public async Task<int> RemoveTagAsync(long placementId, long tagId)
    {
        const string sql = """
            DELETE FROM resource_placement_tags
            WHERE placement_id = @PlacementId
                AND tag_id = @TagId;
            """;

        await using var connection = databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new
        {
            PlacementId = placementId,
            TagId = tagId
        });
    }

    public async Task<int> AddTagToResourcesInCollectionAsync(
        IEnumerable<long> resourceIds,
        long collectionId,
        long tagId)
    {
        const string sql = """
            INSERT OR IGNORE INTO resource_placement_tags (
                placement_id,
                tag_id
            )
            SELECT
                rp.id,
                @TagId
            FROM resource_placements rp
            WHERE rp.resource_id IN @ResourceIds
                AND rp.collection_id = @CollectionId;
            """;

        var distinctResourceIds = resourceIds.Distinct().ToArray();
        if (distinctResourceIds.Length == 0)
        {
            return 0;
        }

        await using var connection = databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new
        {
            ResourceIds = distinctResourceIds,
            CollectionId = collectionId,
            TagId = tagId
        });
    }

    public async Task<int> RemoveTagFromResourcesInCollectionAsync(
        IEnumerable<long> resourceIds,
        long collectionId,
        long tagId)
    {
        const string sql = """
            DELETE FROM resource_placement_tags
            WHERE tag_id = @TagId
                AND placement_id IN (
                    SELECT id
                    FROM resource_placements
                    WHERE resource_id IN @ResourceIds
                        AND collection_id = @CollectionId
                );
            """;

        var distinctResourceIds = resourceIds.Distinct().ToArray();
        if (distinctResourceIds.Length == 0)
        {
            return 0;
        }

        await using var connection = databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new
        {
            ResourceIds = distinctResourceIds,
            CollectionId = collectionId,
            TagId = tagId
        });
    }

    public async Task<IReadOnlyList<ResourcePlacementTag>> GetTagsAsync(long placementId)
    {
        var sql = $"""
            SELECT
                {PlacementTagColumns}
            FROM resource_placement_tags rpt
            INNER JOIN collection_tags ct ON ct.id = rpt.tag_id
            WHERE rpt.placement_id = @PlacementId
            ORDER BY ct.sort_order ASC, ct.name COLLATE NOCASE ASC, ct.id ASC;
            """;

        await using var connection = databaseService.GetConnection();
        var tags = await connection.QueryAsync<ResourcePlacementTag>(sql, new { PlacementId = placementId });
        return tags.AsList();
    }

    public async Task<int> ReplaceTagsAsync(long placementId, IEnumerable<long> tagIds)
    {
        const string deleteSql = """
            DELETE FROM resource_placement_tags
            WHERE placement_id = @PlacementId;
            """;

        const string insertSql = """
            INSERT OR IGNORE INTO resource_placement_tags (
                placement_id,
                tag_id
            )
            VALUES (
                @PlacementId,
                @TagId
            );
            """;

        var distinctTagIds = tagIds.Distinct().ToArray();

        await using var connection = databaseService.GetConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var changedCount = await connection.ExecuteAsync(deleteSql, new { PlacementId = placementId }, transaction);
            foreach (var tagId in distinctTagIds)
            {
                changedCount += await connection.ExecuteAsync(
                    insertSql,
                    new
                    {
                        PlacementId = placementId,
                        TagId = tagId
                    },
                    transaction);
            }

            await transaction.CommitAsync();
            return changedCount;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<ResourceItem>> GetResourcesByCollectionTagAsync(long collectionId, long tagId)
    {
        var sql = $"""
            SELECT DISTINCT
                {ResourceColumns}
            FROM resources r
            INNER JOIN resource_placements rp ON rp.resource_id = r.id
            INNER JOIN resource_placement_tags rpt ON rpt.placement_id = rp.id
            INNER JOIN collection_tags ct ON ct.id = rpt.tag_id
            WHERE rp.collection_id = @CollectionId
                AND ct.collection_id = @CollectionId
                AND ct.id = @TagId
            ORDER BY r.imported_at DESC, r.id DESC;
            """;

        await using var connection = databaseService.GetConnection();
        var resources = await connection.QueryAsync<ResourceItem>(sql, new
        {
            CollectionId = collectionId,
            TagId = tagId
        });
        return resources.AsList();
    }
}
