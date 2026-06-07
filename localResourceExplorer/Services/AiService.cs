using LocalResourceExplorer.Models;
using LocalResourceExplorer.Repositories;

namespace LocalResourceExplorer.Services;

public sealed class AiService
{
    private const string AiBaseUrlSettingKey = "ai_base_url";
    private const string AiApiKeySettingKey = "ai_api_key";
    private const string AiModelNameSettingKey = "ai_model_name";

    private readonly AppSettingsRepository appSettingsRepository;

    public AiService(AppSettingsRepository appSettingsRepository)
    {
        this.appSettingsRepository = appSettingsRepository;
    }

    public async Task<IReadOnlyList<AiSuggestion>> SuggestOrganizationAsync(
        IReadOnlyCollection<ResourceItem> resources,
        IReadOnlyCollection<AiCollectionContext> existingCollections,
        IReadOnlyDictionary<long, IReadOnlyList<ResourcePlacement>> existingPlacementsByResource,
        AiCollectionContext? targetCollection = null,
        bool includeNotes = true,
        CancellationToken cancellationToken = default)
    {
        if (resources.Count == 0)
        {
            throw new AiServiceException("请先选择至少一个资源。");
        }

        var settings = await LoadSettingsAsync();
        var request = new AiSuggestionRequest
        {
            ExistingCollections = existingCollections.ToArray(),
            TargetCollectionId = targetCollection?.Id,
            TargetCollectionName = targetCollection?.Name,
            ExistingCollectionNames = existingCollections
                .Select(collection => collection.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Resources = resources
                .Select(resource => new AiResourceContext
                {
                    ResourceId = resource.Id,
                    FileName = resource.OriginalName,
                    Extension = resource.Extension,
                    SizeBytes = resource.SizeBytes,
                    ModifiedAt = resource.ModifiedAt,
                    DurationMs = resource.DurationMs,
                    Note = includeNotes ? resource.Note : null,
                    ExistingPlacements = existingPlacementsByResource.GetValueOrDefault(resource.Id) ?? []
                })
                .ToArray()
        };

        IAiProvider provider = new OpenAiCompatibleProvider(settings);
        return await provider.SuggestOrganizationAsync(request, cancellationToken);
    }

    private async Task<AiSettings> LoadSettingsAsync()
    {
        var settings = await appSettingsRepository.GetAllAsync();
        var aiSettings = new AiSettings
        {
            BaseUrl = settings.GetValueOrDefault(AiBaseUrlSettingKey) ?? string.Empty,
            ApiKey = settings.GetValueOrDefault(AiApiKeySettingKey) ?? string.Empty,
            ModelName = settings.GetValueOrDefault(AiModelNameSettingKey) ?? string.Empty
        };

        if (string.IsNullOrWhiteSpace(aiSettings.BaseUrl))
        {
            throw new AiServiceException("请先在设置中填写 AI Base URL。");
        }

        if (string.IsNullOrWhiteSpace(aiSettings.ModelName))
        {
            throw new AiServiceException("请先在设置中填写 AI 模型名称。");
        }

        return aiSettings;
    }
}
