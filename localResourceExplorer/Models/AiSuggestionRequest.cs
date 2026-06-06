namespace LocalResourceExplorer.Models;

public sealed class AiSuggestionRequest
{
    public IReadOnlyList<AiResourceContext> Resources { get; set; } = [];

    public IReadOnlyList<string> ExistingCollectionNames { get; set; } = [];

    public IReadOnlyList<string> ExistingTagNames { get; set; } = [];
}
