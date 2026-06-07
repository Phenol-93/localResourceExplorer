namespace LocalResourceExplorer.Models;

public sealed class AiSuggestion
{
    public long ResourceId { get; set; }

    public string ResourceName { get; set; } = string.Empty;

    public string? SuggestedTitle { get; set; }

    public AiSuggestedEntity? Collection { get; set; }

    public AiSuggestedEntity? Category { get; set; }

    public IReadOnlyList<AiSuggestedEntity> Tags { get; set; } = [];

    public IReadOnlyList<string> SuggestedCollections { get; set; } = [];

    public IReadOnlyList<string> SuggestedTags { get; set; } = [];

    public string? SuggestedNote { get; set; }

    public string? Reason { get; set; }
}
