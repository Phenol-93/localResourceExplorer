namespace LocalResourceExplorer.Models;

public sealed class ResourceItem
{
    public long Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string OriginalName { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string? Extension { get; set; }

    public long SizeBytes { get; set; }

    public DateTime? ModifiedAt { get; set; }

    public DateTime ImportedAt { get; set; }

    public long? DurationMs { get; set; }

    public string? Note { get; set; }

    public bool IsFavorite { get; set; }

    public bool IsMissing { get; set; }

    public DateTime? LastOpenedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
