using System.IO;
using LocalResourceExplorer.Repositories;

namespace LocalResourceExplorer.Services;

public sealed class MissingFileChecker
{
    private readonly ResourceRepository resourceRepository;

    public MissingFileChecker(ResourceRepository resourceRepository)
    {
        this.resourceRepository = resourceRepository;
    }

    public async Task<MissingFileCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var resources = await resourceRepository.GetMissingCheckItemsAsync();
        var checkedCount = 0;
        var missingCount = 0;
        var restoredCount = 0;

        foreach (var resource in resources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var exists = File.Exists(resource.Path);
            var shouldBeMissing = !exists;

            if (shouldBeMissing)
            {
                missingCount++;
            }

            if (resource.IsMissing == shouldBeMissing)
            {
                checkedCount++;
                continue;
            }

            await resourceRepository.MarkMissingAsync(resource.Id, shouldBeMissing);
            if (!shouldBeMissing)
            {
                restoredCount++;
            }

            checkedCount++;
        }

        return new MissingFileCheckResult(checkedCount, missingCount, restoredCount);
    }
}

public sealed record MissingFileCheckResult(int CheckedCount, int MissingCount, int RestoredCount);
