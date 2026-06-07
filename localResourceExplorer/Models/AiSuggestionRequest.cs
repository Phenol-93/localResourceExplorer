namespace LocalResourceExplorer.Models;

public sealed class AiSuggestionRequest
{
    public IReadOnlyList<AiResourceContext> Resources { get; set; } = [];

    public IReadOnlyList<AiCollectionContext> ExistingCollections { get; set; } = [];

    public long? TargetCollectionId { get; set; }

    public string? TargetCollectionName { get; set; }

    public IReadOnlyList<string> ExistingCollectionNames { get; set; } = [];

    public IReadOnlyList<string> ExistingTagNames { get; set; } = [];
}
