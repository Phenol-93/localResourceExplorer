namespace LocalResourceExplorer.Models;

public sealed class Tag
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Color { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
