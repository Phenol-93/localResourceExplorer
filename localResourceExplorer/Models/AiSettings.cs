namespace LocalResourceExplorer.Models;

public sealed class AiSettings
{
    public bool IsEnabled { get; set; }

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;

    public DateTime? UpdatedAt { get; set; }
}
