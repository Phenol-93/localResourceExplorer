using System.Data;
using Dapper;
using LocalResourceExplorer.Models;
using LocalResourceExplorer.Services;
using Microsoft.Data.Sqlite;

namespace LocalResourceExplorer.Repositories;

public sealed class ResourcePlacementRepository
{
    private const string PlacementColumns = """
        rp.id AS Id,
        rp.resource_id AS ResourceId,
        rp.collection_id AS CollectionId,
        rp.category_id AS CategoryId,
        c.name AS CollectionName,
        cc.name AS CategoryName,
        rp.created_at AS CreatedAt,
        rp.updated_at AS UpdatedAt
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

    public ResourcePlacementRepository(DatabaseService databaseService)
    {
        this.databaseService = databaseService;
    }

    public Task<long> AddAsync(long resourceId, long collectionId, long? categoryId = null)
    {
        return AddToCollectionAsync(resourceId, collectionId, categoryId);
    }

    public async Task<long> AddToCollectionAsync(long resourceId, long collectionId, long? categoryId = null)
    {
        const string sql = """
            INSERT OR IGNORE INTO resource_placements (
                resource_id,
                collection_id,
                category_id,
                created_at,
                updated_at
            )
            VALUES (
                @ResourceId,
                @CollectionId,
                @CategoryId,
                @CreatedAt,
                @UpdatedAt
            );

            SELECT id
            FROM resource_placements
            WHERE resource_id = @ResourceId
                AND collection_id = @CollectionId
                AND (
                    (@CategoryId IS NULL AND category_id IS NULL)
                    OR category_id = @CategoryId
                )
            LIMIT 1;
            """;

        var now = DateTime.UtcNow;

        await using var connection = databaseService.GetConnection();
        return await connection.ExecuteScalarAsync<long>(sql, new
        {
            ResourceId = resourceId,
            CollectionId = collectionId,
            CategoryId = categoryId,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    public Task<long> AddToCategoryAsync(long resourceId, long collectionId, long categoryId)
    {
        return AddToCollectionAsync(resourceId, collectionId, categoryId);
    }

    public async Task<long> UpdateCategoryAsync(long resourceId, long collectionId, long? categoryId)
    {
        await using var connection = databaseService.GetConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var targetPlacementId = await FindPlacementIdAsync(connection, transaction, resourceId, collectionId, categoryId);
            if (targetPlacementId is not null)
            {
                await connection.ExecuteAsync(
                    """
                    DELETE FROM resource_placements
                    WHERE resource_id = @ResourceId
                        AND collection_id = @CollectionId
                        AND id <> @TargetPlacementId;
                    """,
                    new
                    {
                        ResourceId = resourceId,
                        CollectionId = collectionId,
                        TargetPlacementId = targetPlacementId.Value
                    },
                    transaction);

                await transaction.CommitAsync();
                return targetPlacementId.Value;
            }

            var existingPlacementId = await connection.ExecuteScalarAsync<long?>(
                """
                SELECT id
                FROM resource_placements
                WHERE resource_id = @ResourceId
                    AND collection_id = @CollectionId
                ORDER BY category_id IS NOT NULL, id
                LIMIT 1;
                """,
                new
                {
                    ResourceId = resourceId,
                    CollectionId = collectionId
                },
                transaction);

            var now = DateTime.UtcNow;
            if (existingPlacementId is null)
            {
                existingPlacementId = await connection.ExecuteScalarAsync<long>(
                    """
                    INSERT INTO resource_placements (
                        resource_id,
                        collection_id,
                        category_id,
                        created_at,
                        updated_at
                    )
                    VALUES (
                        @ResourceId,
                        @CollectionId,
                        @CategoryId,
                        @CreatedAt,
                        @UpdatedAt
                    );

                    SELECT last_insert_rowid();
                    """,
                    new
                    {
                        ResourceId = resourceId,
                        CollectionId = collectionId,
                        CategoryId = categoryId,
                        CreatedAt = now,
                        UpdatedAt = now
                    },
                    transaction);
            }
            else
            {
                await connection.ExecuteAsync(
                    """
                    UPDATE resource_placements
                    SET category_id = @CategoryId,
                        updated_at = @UpdatedAt
                    WHERE id = @PlacementId;
                    """,
                    new
                    {
                        PlacementId = existingPlacementId.Value,
                        CategoryId = categoryId,
                        UpdatedAt = now
                    },
                    transaction);
            }

            await connection.ExecuteAsync(
                """
                DELETE FROM resource_placements
                WHERE resource_id = @ResourceId
                    AND collection_id = @CollectionId
                    AND id <> @PlacementId;
                """,
                new
                {
                    ResourceId = resourceId,
                    CollectionId = collectionId,
                    PlacementId = existingPlacementId.Value
                },
                transaction);

            await transaction.CommitAsync();
            return existingPlacementId.Value;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<int> RemoveAsync(long placementId)
    {
        const string sql = """
            DELETE FROM resource_placements
            WHERE id = @PlacementId;
            """;

        await using var connection = databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new { PlacementId = placementId });
    }

    public async Task<int> RemoveFromCollectionAsync(long resourceId, long collectionId)
    {
        const string sql = """
            DELETE FROM resource_placements
            WHERE resource_id = @ResourceId
                AND collection_id = @CollectionId;
            """;

        await using var connection = databaseService.GetConnection();
        return await connection.ExecuteAsync(sql, new
        {
            ResourceId = resourceId,
            CollectionId = collectionId
        });
    }

    public async Task<int> BatchRemoveFromCollectionAsync(IEnumerable<long> resourceIds, long collectionId)
    {
        const string sql = """
            DELETE FROM resource_placements
            WHERE resource_id = @ResourceId
                AND collection_id = @CollectionId;
            """;

        var distinctResourceIds = resourceIds.Distinct().ToArray();
        if (distinctResourceIds.Length == 0)
        {
            return 0;
        }

        await using var connection = databaseService.GetConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var changedCount = 0;
            foreach (var resourceId in distinctResourceIds)
            {
                changedCount += await connection.ExecuteAsync(
                    sql,
                    new
                    {
                        ResourceId = resourceId,
                        CollectionId = collectionId
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

    public async Task<IReadOnlyList<ResourcePlacement>> GetByResourceAsync(long resourceId)
    {
        var sql = $"""
            SELECT
                {PlacementColumns}
            FROM resource_placements rp
            INNER JOIN collections c ON c.id = rp.collection_id
            LEFT JOIN collection_categories cc ON cc.id = rp.category_id
            WHERE rp.resource_id = @ResourceId
            ORDER BY c.sort_order ASC, c.name COLLATE NOCASE ASC, cc.sort_order ASC, cc.name COLLATE NOCASE ASC, rp.id ASC;
            """;

        await using var connection = databaseService.GetConnection();
        var placements = await connection.QueryAsync<ResourcePlacement>(sql, new { ResourceId = resourceId });
        return placements.AsList();
    }

    public async Task<ResourcePlacement?> GetByResourceAndCollectionAsync(long resourceId, long collectionId)
    {
        var sql = $"""
            SELECT
                {PlacementColumns}
            FROM resource_placements rp
            INNER JOIN collections c ON c.id = rp.collection_id
            LEFT JOIN collection_categories cc ON cc.id = rp.category_id
            WHERE rp.resource_id = @ResourceId
                AND rp.collection_id = @CollectionId
            ORDER BY rp.category_id IS NOT NULL DESC, rp.id ASC
            LIMIT 1;
            """;

        await using var connection = databaseService.GetConnection();
        return await connection.QuerySingleOrDefaultAsync<ResourcePlacement>(sql, new
        {
            ResourceId = resourceId,
            CollectionId = collectionId
        });
    }

    public async Task<IReadOnlyList<ResourcePlacement>> GetPlacementsByCollectionAsync(long collectionId, long? categoryId = null)
    {
        var sql = $"""
            SELECT
                {PlacementColumns}
            FROM resource_placements rp
            INNER JOIN collections c ON c.id = rp.collection_id
            LEFT JOIN collection_categories cc ON cc.id = rp.category_id
            WHERE rp.collection_id = @CollectionId
                AND (
                    @CategoryId IS NULL
                    OR rp.category_id = @CategoryId
                )
            ORDER BY rp.updated_at DESC, rp.id DESC;
            """;

        await using var connection = databaseService.GetConnection();
        var placements = await connection.QueryAsync<ResourcePlacement>(sql, new
        {
            CollectionId = collectionId,
            CategoryId = categoryId
        });
        return placements.AsList();
    }

    public Task<IReadOnlyList<ResourceItem>> GetResourcesByCollectionAsync(long collectionId)
    {
        return GetResourcesByCollectionInternalAsync(collectionId, categoryId: null);
    }

    public Task<IReadOnlyList<ResourceItem>> GetResourcesByCollectionCategoryAsync(long collectionId, long categoryId)
    {
        return GetResourcesByCollectionInternalAsync(collectionId, categoryId);
    }

    public async Task<bool> ExistsAsync(long resourceId, long collectionId)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM resource_placements
            WHERE resource_id = @ResourceId
                AND collection_id = @CollectionId;
            """;

        await using var connection = databaseService.GetConnection();
        var count = await connection.ExecuteScalarAsync<int>(sql, new
        {
            ResourceId = resourceId,
            CollectionId = collectionId
        });
        return count > 0;
    }

    public async Task<int> BatchAddAsync(IEnumerable<long> resourceIds, long collectionId, long? categoryId = null)
    {
        const string sql = """
            INSERT OR IGNORE INTO resource_placements (
                resource_id,
                collection_id,
                category_id,
                created_at,
                updated_at
            )
            VALUES (
                @ResourceId,
                @CollectionId,
                @CategoryId,
                @CreatedAt,
                @UpdatedAt
            );
            """;

        var distinctResourceIds = resourceIds.Distinct().ToArray();
        if (distinctResourceIds.Length == 0)
        {
            return 0;
        }

        await using var connection = databaseService.GetConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var now = DateTime.UtcNow;
            var changedCount = 0;
            foreach (var resourceId in distinctResourceIds)
            {
                changedCount += await connection.ExecuteAsync(
                    sql,
                    new
                    {
                        ResourceId = resourceId,
                        CollectionId = collectionId,
                        CategoryId = categoryId,
                        CreatedAt = now,
                        UpdatedAt = now
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

    public async Task<int> BatchUpsertCategoryAsync(IEnumerable<long> resourceIds, long collectionId, long? categoryId)
    {
        var distinctResourceIds = resourceIds.Distinct().ToArray();
        if (distinctResourceIds.Length == 0)
        {
            return 0;
        }

        await using var connection = databaseService.GetConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var changedCount = 0;
            foreach (var resourceId in distinctResourceIds)
            {
                var placementId = await FindPlacementIdAsync(connection, transaction, resourceId, collectionId, categoryId);
                var now = DateTime.UtcNow;
                if (placementId is null)
                {
                    placementId = await connection.ExecuteScalarAsync<long?>(
                        """
                        SELECT id
                        FROM resource_placements
                        WHERE resource_id = @ResourceId
                            AND collection_id = @CollectionId
                        ORDER BY category_id IS NOT NULL, id
                        LIMIT 1;
                        """,
                        new
                        {
                            ResourceId = resourceId,
                            CollectionId = collectionId
                        },
                        transaction);

                    if (placementId is null)
                    {
                        placementId = await connection.ExecuteScalarAsync<long>(
                            """
                            INSERT INTO resource_placements (
                                resource_id,
                                collection_id,
                                category_id,
                                created_at,
                                updated_at
                            )
                            VALUES (
                                @ResourceId,
                                @CollectionId,
                                @CategoryId,
                                @CreatedAt,
                                @UpdatedAt
                            );

                            SELECT last_insert_rowid();
                            """,
                            new
                            {
                                ResourceId = resourceId,
                                CollectionId = collectionId,
                                CategoryId = categoryId,
                                CreatedAt = now,
                                UpdatedAt = now
                            },
                            transaction);
                        changedCount++;
                    }
                    else
                    {
                        changedCount += await connection.ExecuteAsync(
                            """
                            UPDATE resource_placements
                            SET category_id = @CategoryId,
                                updated_at = @UpdatedAt
                            WHERE id = @PlacementId;
                            """,
                            new
                            {
                                PlacementId = placementId.Value,
                                CategoryId = categoryId,
                                UpdatedAt = now
                            },
                            transaction);
                    }
                }

                await connection.ExecuteAsync(
                    """
                    DELETE FROM resource_placements
                    WHERE resource_id = @ResourceId
                        AND collection_id = @CollectionId
                        AND id <> @PlacementId;
                    """,
                    new
                    {
                        ResourceId = resourceId,
                        CollectionId = collectionId,
                        PlacementId = placementId.Value
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

    public async Task<int> BatchUpdateExistingCategoryAsync(IEnumerable<long> resourceIds, long collectionId, long? categoryId)
    {
        var distinctResourceIds = resourceIds.Distinct().ToArray();
        if (distinctResourceIds.Length == 0)
        {
            return 0;
        }

        await using var connection = databaseService.GetConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var changedCount = 0;
            foreach (var resourceId in distinctResourceIds)
            {
                var existingPlacementId = await connection.ExecuteScalarAsync<long?>(
                    """
                    SELECT id
                    FROM resource_placements
                    WHERE resource_id = @ResourceId
                        AND collection_id = @CollectionId
                    ORDER BY category_id IS NOT NULL, id
                    LIMIT 1;
                    """,
                    new
                    {
                        ResourceId = resourceId,
                        CollectionId = collectionId
                    },
                    transaction);

                if (existingPlacementId is null)
                {
                    continue;
                }

                changedCount += await connection.ExecuteAsync(
                    """
                    UPDATE resource_placements
                    SET category_id = @CategoryId,
                        updated_at = @UpdatedAt
                    WHERE id = @PlacementId;
                    """,
                    new
                    {
                        PlacementId = existingPlacementId.Value,
                        CategoryId = categoryId,
                        UpdatedAt = DateTime.UtcNow
                    },
                    transaction);

                await connection.ExecuteAsync(
                    """
                    DELETE FROM resource_placements
                    WHERE resource_id = @ResourceId
                        AND collection_id = @CollectionId
                        AND id <> @PlacementId;
                    """,
                    new
                    {
                        ResourceId = resourceId,
                        CollectionId = collectionId,
                        PlacementId = existingPlacementId.Value
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

    private async Task<IReadOnlyList<ResourceItem>> GetResourcesByCollectionInternalAsync(long collectionId, long? categoryId)
    {
        var sql = $"""
            SELECT DISTINCT
                {ResourceColumns}
            FROM resources r
            INNER JOIN resource_placements rp ON rp.resource_id = r.id
            WHERE rp.collection_id = @CollectionId
                AND (
                    @CategoryId IS NULL
                    OR rp.category_id = @CategoryId
                )
            ORDER BY r.imported_at DESC, r.id DESC;
            """;

        await using var connection = databaseService.GetConnection();
        var resources = await connection.QueryAsync<ResourceItem>(sql, new
        {
            CollectionId = collectionId,
            CategoryId = categoryId
        });
        return resources.AsList();
    }

    private static async Task<long?> FindPlacementIdAsync(
        SqliteConnection connection,
        IDbTransaction transaction,
        long resourceId,
        long collectionId,
        long? categoryId)
    {
        return await connection.ExecuteScalarAsync<long?>(
            """
            SELECT id
            FROM resource_placements
            WHERE resource_id = @ResourceId
                AND collection_id = @CollectionId
                AND (
                    (@CategoryId IS NULL AND category_id IS NULL)
                    OR category_id = @CategoryId
                )
            LIMIT 1;
            """,
            new
            {
                ResourceId = resourceId,
                CollectionId = collectionId,
                CategoryId = categoryId
            },
            transaction);
    }
}
