namespace LocalResourceExplorer.Models;

public sealed class AiCollectionContext
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public IReadOnlyList<AiNamedContext> Categories { get; set; } = [];

    public IReadOnlyList<AiNamedContext> Tags { get; set; } = [];
}

public sealed class AiNamedContext
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class AiSuggestedEntity
{
    public long? Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsNew { get; set; }
}
