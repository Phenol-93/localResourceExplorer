using System.IO;
using LocalResourceExplorer.Models;

namespace LocalResourceExplorer.Services;

public sealed class ResourceScanner
{
    public Task<IReadOnlyList<ResourceItem>> ScanFilesAsync(
        IEnumerable<string> filePaths,
        IProgress<ResourceScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => ScanFiles(filePaths, progress, cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<ResourceItem>> ScanFolderAsync(
        string folderPath,
        bool recursive,
        IProgress<ResourceScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => ScanFolder(folderPath, recursive, progress, cancellationToken),
            cancellationToken);
    }

    private static IReadOnlyList<ResourceItem> ScanFiles(
        IEnumerable<string> filePaths,
        IProgress<ResourceScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var resources = new List<ResourceItem>();
        var scannedCount = 0;
        var skippedCount = 0;
        var importedAt = DateTime.Now;

        foreach (var filePath in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var resource = CreateResourceItem(filePath, importedAt);
                resources.Add(resource);

                scannedCount++;
                progress?.Report(ResourceScanProgress.Scanned(scannedCount, skippedCount, resource.Path));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsSkippableFileException(ex))
            {
                skippedCount++;
                AppLog.ScanWarning(ex, "Skipped file while scanning selected files", filePath);
                progress?.Report(ResourceScanProgress.Skipped(scannedCount, skippedCount, filePath, ex.Message));
            }
        }

        return resources;
    }

    private static IReadOnlyList<ResourceItem> ScanFolder(
        string folderPath,
        bool recursive,
        IProgress<ResourceScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException("Folder path cannot be empty.", nameof(folderPath));
        }

        var rootDirectory = new DirectoryInfo(folderPath);
        if (!rootDirectory.Exists)
        {
            throw new DirectoryNotFoundException($"Folder does not exist: {folderPath}");
        }

        var resources = new List<ResourceItem>();
        var scannedCount = 0;
        var skippedCount = 0;
        var importedAt = DateTime.Now;

        foreach (var filePath in EnumerateFilePaths(rootDirectory.FullName, recursive, progress, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var resource = CreateResourceItem(filePath, importedAt);
                resources.Add(resource);

                scannedCount++;
                progress?.Report(ResourceScanProgress.Scanned(scannedCount, skippedCount, resource.Path));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsSkippableFileException(ex))
            {
                skippedCount++;
                AppLog.ScanWarning(ex, "Skipped file while scanning folder", filePath);
                progress?.Report(ResourceScanProgress.Skipped(scannedCount, skippedCount, filePath, ex.Message));
            }
        }

        return resources;
    }

    private static ResourceItem CreateResourceItem(string filePath, DateTime importedAt)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("文件不存在或已移动。", filePath);
        }

        var originalName = fileInfo.Name;
        var title = Path.GetFileNameWithoutExtension(originalName);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = originalName;
        }

        return new ResourceItem
        {
            Title = title,
            OriginalName = originalName,
            Path = fileInfo.FullName,
            Extension = fileInfo.Extension,
            SizeBytes = fileInfo.Length,
            ModifiedAt = fileInfo.LastWriteTime,
            ImportedAt = importedAt,
            CreatedAt = importedAt,
            UpdatedAt = importedAt
        };
    }

    private static IEnumerable<string> EnumerateFilePaths(
        string rootFolderPath,
        bool recursive,
        IProgress<ResourceScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var pendingFolders = new Stack<string>();
        pendingFolders.Push(rootFolderPath);

        while (pendingFolders.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentFolder = pendingFolders.Pop();

            foreach (var filePath in SafeEnumerateFiles(currentFolder, progress))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return filePath;
            }

            if (!recursive)
            {
                continue;
            }

            foreach (var childFolder in SafeEnumerateDirectories(currentFolder, progress))
            {
                cancellationToken.ThrowIfCancellationRequested();
                pendingFolders.Push(childFolder);
            }
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(
        string folderPath,
        IProgress<ResourceScanProgress>? progress)
    {
        try
        {
            return Directory.EnumerateFiles(folderPath).ToArray();
        }
        catch (Exception ex) when (IsSkippableFolderException(ex))
        {
            AppLog.ScanWarning(ex, "Skipped folder while enumerating files", folderPath);
            progress?.Report(ResourceScanProgress.Skipped(0, 0, folderPath, ex.Message));
            return [];
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(
        string folderPath,
        IProgress<ResourceScanProgress>? progress)
    {
        try
        {
            return Directory.EnumerateDirectories(folderPath).ToArray();
        }
        catch (Exception ex) when (IsSkippableFolderException(ex))
        {
            AppLog.ScanWarning(ex, "Skipped folder while enumerating child folders", folderPath);
            progress?.Report(ResourceScanProgress.Skipped(0, 0, folderPath, ex.Message));
            return [];
        }
    }

    private static bool IsSkippableFileException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or PathTooLongException
            or NotSupportedException
            or System.Security.SecurityException;
    }

    private static bool IsSkippableFolderException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or DirectoryNotFoundException
            or PathTooLongException
            or NotSupportedException
            or System.Security.SecurityException;
    }
}

public sealed record ResourceScanProgress(
    int ScannedCount,
    int SkippedCount,
    string CurrentPath,
    string? ErrorMessage)
{
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public static ResourceScanProgress Scanned(int scannedCount, int skippedCount, string currentPath)
    {
        return new ResourceScanProgress(scannedCount, skippedCount, currentPath, null);
    }

    public static ResourceScanProgress Skipped(int scannedCount, int skippedCount, string currentPath, string errorMessage)
    {
        return new ResourceScanProgress(scannedCount, skippedCount, currentPath, errorMessage);
    }
}
