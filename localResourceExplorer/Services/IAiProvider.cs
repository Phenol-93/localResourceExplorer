using LocalResourceExplorer.Models;

namespace LocalResourceExplorer.Services;

public interface IAiProvider
{
    Task<IReadOnlyList<AiSuggestion>> SuggestOrganizationAsync(
        AiSuggestionRequest request,
        CancellationToken cancellationToken = default);
}
