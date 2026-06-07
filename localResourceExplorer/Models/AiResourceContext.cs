namespace LocalResourceExplorer.Models;

public sealed class AiResourceContext
{
    public long ResourceId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string? Extension { get; set; }

    public long SizeBytes { get; set; }

    public DateTime? ModifiedAt { get; set; }

    public long? DurationMs { get; set; }

    public string? Note { get; set; }

    public IReadOnlyList<ResourcePlacement> ExistingPlacements { get; set; } = [];
}
