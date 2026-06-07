namespace LocalResourceExplorer.Models;

public sealed class ResourcePlacementTag
{
    public long PlacementId { get; set; }

    public long TagId { get; set; }

    public string TagName { get; set; } = string.Empty;

    public string? Color { get; set; }
}
