namespace LocalResourceExplorer.Models;

public sealed class ResourcePlacement
{
    public long Id { get; set; }

    public long ResourceId { get; set; }

    public long CollectionId { get; set; }

    public long? CategoryId { get; set; }

    public string CollectionName { get; set; } = string.Empty;

    public string? CategoryName { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
