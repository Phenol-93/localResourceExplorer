using System.IO;

namespace LocalResourceExplorer.Services;

public sealed class MediaInfoService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3",
        ".flac",
        ".wav",
        ".m4a",
        ".mp4",
        ".mkv",
        ".mov",
        ".avi",
        ".wmv"
    };

    public bool IsSupported(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return SupportedExtensions.Contains(Path.GetExtension(path));
    }

    public Task<long?> TryReadDurationMsAsync(string path, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (cancellationToken.IsCancellationRequested || !IsSupported(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var file = TagLib.File.Create(path);
                var duration = file.Properties.Duration;

                if (duration <= TimeSpan.Zero)
                {
                    return null;
                }

                return (long?)duration.TotalMilliseconds;
            }
            catch
            {
                return null;
            }
        }, cancellationToken);
    }
}
