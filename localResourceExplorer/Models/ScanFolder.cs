namespace LocalResourceExplorer.Models;

public sealed class ScanFolder
{
    public long Id { get; set; }

    public string Path { get; set; } = string.Empty;

    public DateTime? LastScanAt { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
