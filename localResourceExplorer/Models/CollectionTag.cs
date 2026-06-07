namespace LocalResourceExplorer.Models;

public sealed class CollectionTag
{
    public long Id { get; set; }

    public long CollectionId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Color { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
