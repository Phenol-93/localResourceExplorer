namespace LocalResourceExplorer.Models;

public sealed class TagSummary
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Color { get; set; } = "#EEF6FF";

    public int ResourceCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
