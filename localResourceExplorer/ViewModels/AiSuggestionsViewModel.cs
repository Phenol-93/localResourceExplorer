using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalResourceExplorer.Models;
using LocalResourceExplorer.Repositories;
using CollectionModel = LocalResourceExplorer.Models.Collection;

namespace LocalResourceExplorer.ViewModels;

public sealed partial class AiSuggestionsViewModel : ObservableObject
{
    private readonly CollectionCategoryRepository collectionCategoryRepository;
    private readonly CollectionRepository collectionRepository;
    private readonly CollectionTagRepository collectionTagRepository;
    private readonly ResourcePlacementRepository resourcePlacementRepository;
    private readonly ResourcePlacementTagRepository resourcePlacementTagRepository;
    private readonly ResourceRepository resourceRepository;

    private Dictionary<string, CollectionModel> collectionsByName = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<long, IReadOnlyList<CollectionCategory>> categoriesByCollectionId = [];
    private Dictionary<long, IReadOnlyList<CollectionTag>> tagsByCollectionId = [];

    public AiSuggestionsViewModel(
        IEnumerable<AiSuggestion> suggestions,
        ResourceRepository resourceRepository,
        CollectionRepository collectionRepository,
        CollectionCategoryRepository collectionCategoryRepository,
        CollectionTagRepository collectionTagRepository,
        ResourcePlacementRepository resourcePlacementRepository,
        ResourcePlacementTagRepository resourcePlacementTagRepository)
    {
        this.resourceRepository = resourceRepository;
        this.collectionRepository = collectionRepository;
        this.collectionCategoryRepository = collectionCategoryRepository;
        this.collectionTagRepository = collectionTagRepository;
        this.resourcePlacementRepository = resourcePlacementRepository;
        this.resourcePlacementTagRepository = resourcePlacementTagRepository;

        Suggestions = new ObservableCollection<AiSuggestionItemViewModel>(
            suggestions.Select(suggestion => new AiSuggestionItemViewModel(suggestion)));

        _ = LoadExistingMetadataAsync();
    }

    public ObservableCollection<AiSuggestionItemViewModel> Suggestions { get; }

    [ObservableProperty]
    private bool confirmCreateMissingItems;

    [ObservableProperty]
    private bool hasMissingItems;

    [ObservableProperty]
    private bool hasApplied;

    [ObservableProperty]
    private string missingItemsText = string.Empty;

    [ObservableProperty]
    private string statusText = "请选择要应用的建议。不存在的集合、二级分类或集合内标签需要额外确认后才会创建。";

    [RelayCommand]
    private async Task ApplySelectedSuggestionsAsync()
    {
        await LoadExistingMetadataAsync();

        var selectedSuggestions = Suggestions
            .Where(item => item.IsSelected)
            .ToArray();

        if (selectedSuggestions.Length == 0)
        {
            StatusText = "请至少勾选一条建议。";
            return;
        }

        var missingItems = selectedSuggestions
            .SelectMany(item => item.MissingItemDescriptions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missingItems.Length > 0 && !ConfirmCreateMissingItems)
        {
            StatusText = "存在尚未创建的集合、二级分类或集合内标签。请勾选确认创建，或取消对应建议后再应用。";
            return;
        }

        try
        {
            var changedCount = 0;
            foreach (var item in selectedSuggestions)
            {
                changedCount += await ApplySuggestionAsync(item);
            }

            HasApplied = changedCount > 0;
            StatusText = $"已应用选中建议，写入 {changedCount} 项修改。";
            MessageBox.Show("选中建议已应用。", "AI 整理建议", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadExistingMetadataAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"应用建议失败：{ex.Message}";
            MessageBox.Show($"应用建议失败：{ex.Message}", "AI 整理建议", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task<int> ApplySuggestionAsync(AiSuggestionItemViewModel item)
    {
        var suggestion = item.Suggestion;
        var changedCount = 0;

        if (item.ApplyTitle && !string.IsNullOrWhiteSpace(suggestion.SuggestedTitle))
        {
            changedCount += await resourceRepository.UpdateTitleAsync(
                suggestion.ResourceId,
                suggestion.SuggestedTitle.Trim());
        }

        if (item.ApplyNote && !string.IsNullOrWhiteSpace(suggestion.SuggestedNote))
        {
            changedCount += await resourceRepository.UpdateNoteAsync(
                suggestion.ResourceId,
                suggestion.SuggestedNote.Trim());
        }

        if (!item.ApplyPlacement || suggestion.Collection is null)
        {
            return changedCount;
        }

        var collectionId = await GetOrCreateCollectionIdAsync(suggestion.Collection);
        if (collectionId is null)
        {
            return changedCount;
        }

        var categoryId = item.ApplyCategory && suggestion.Category is not null
            ? await GetOrCreateCategoryIdAsync(collectionId.Value, suggestion.Category)
            : null;

        changedCount += await collectionRepository.AddResourceAsync(suggestion.ResourceId, collectionId.Value);
        await resourcePlacementRepository.BatchUpsertCategoryAsync(
            [suggestion.ResourceId],
            collectionId.Value,
            categoryId);
        changedCount++;

        if (item.ApplyTags)
        {
            foreach (var tag in suggestion.Tags)
            {
                var tagId = await GetOrCreateCollectionTagIdAsync(collectionId.Value, tag);
                if (tagId is not null)
                {
                    changedCount += await resourcePlacementTagRepository.AddTagToResourcesInCollectionAsync(
                        [suggestion.ResourceId],
                        collectionId.Value,
                        tagId.Value);
                }
            }
        }

        return changedCount;
    }

    private async Task<long?> GetOrCreateCollectionIdAsync(AiSuggestedEntity entity)
    {
        if (entity.Id is > 0)
        {
            return entity.Id.Value;
        }

        if (collectionsByName.TryGetValue(entity.Name, out var collection))
        {
            return collection.Id;
        }

        if (!ConfirmCreateMissingItems)
        {
            return null;
        }

        var id = await collectionRepository.CreateAsync(entity.Name);
        await LoadExistingMetadataAsync();
        return id;
    }

    private async Task<long?> GetOrCreateCategoryIdAsync(long collectionId, AiSuggestedEntity entity)
    {
        if (entity.Id is > 0)
        {
            return entity.Id.Value;
        }

        var categories = categoriesByCollectionId.GetValueOrDefault(collectionId) ?? [];
        var category = categories.FirstOrDefault(item =>
            string.Equals(item.Name, entity.Name, StringComparison.OrdinalIgnoreCase));
        if (category is not null)
        {
            return category.Id;
        }

        if (!ConfirmCreateMissingItems)
        {
            return null;
        }

        var id = await collectionCategoryRepository.CreateAsync(collectionId, entity.Name);
        await LoadExistingMetadataAsync();
        return id;
    }

    private async Task<long?> GetOrCreateCollectionTagIdAsync(long collectionId, AiSuggestedEntity entity)
    {
        if (entity.Id is > 0)
        {
            return entity.Id.Value;
        }

        var tags = tagsByCollectionId.GetValueOrDefault(collectionId) ?? [];
        var tag = tags.FirstOrDefault(item =>
            string.Equals(item.Name, entity.Name, StringComparison.OrdinalIgnoreCase));
        if (tag is not null)
        {
            return tag.Id;
        }

        if (!ConfirmCreateMissingItems)
        {
            return null;
        }

        var id = await collectionTagRepository.CreateAsync(collectionId, entity.Name, "#EEF6FF");
        await LoadExistingMetadataAsync();
        return id;
    }

    private async Task LoadExistingMetadataAsync()
    {
        var collections = await collectionRepository.GetAllAsync();
        collectionsByName = collections.ToDictionary(collection => collection.Name, StringComparer.OrdinalIgnoreCase);

        var categoryMap = new Dictionary<long, IReadOnlyList<CollectionCategory>>();
        var tagMap = new Dictionary<long, IReadOnlyList<CollectionTag>>();
        foreach (var collection in collections)
        {
            categoryMap[collection.Id] = await collectionCategoryRepository.GetByCollectionAsync(collection.Id);
            tagMap[collection.Id] = await collectionTagRepository.GetByCollectionAsync(collection.Id);
        }

        categoriesByCollectionId = categoryMap;
        tagsByCollectionId = tagMap;

        foreach (var suggestion in Suggestions)
        {
            suggestion.UpdateMissingItems(collectionsByName, categoriesByCollectionId, tagsByCollectionId);
        }

        var missingItems = Suggestions
            .SelectMany(item => item.MissingItemDescriptions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        HasMissingItems = missingItems.Length > 0;
        MissingItemsText = missingItems.Length == 0
            ? "所有建议的集合、二级分类和集合内标签都已存在。"
            : string.Join("；", missingItems);
    }
}

public sealed partial class AiSuggestionItemViewModel : ObservableObject
{
    public AiSuggestionItemViewModel(AiSuggestion suggestion)
    {
        Suggestion = NormalizeSuggestion(suggestion);
        IsSelected = true;
        ApplyTitle = !string.IsNullOrWhiteSpace(Suggestion.SuggestedTitle);
        ApplyPlacement = Suggestion.Collection is not null;
        ApplyCategory = Suggestion.Category is not null;
        ApplyTags = Suggestion.Tags.Count > 0;
        ApplyNote = !string.IsNullOrWhiteSpace(Suggestion.SuggestedNote);
    }

    public AiSuggestion Suggestion { get; }

    public ObservableCollection<string> MissingItemDescriptions { get; } = [];

    public string CollectionDisplay => Suggestion.Collection is null
        ? "—"
        : $"{Suggestion.Collection.Name}{(Suggestion.Collection.IsNew ? "（新建）" : string.Empty)}";

    public string CategoryDisplay => Suggestion.Category is null
        ? "未分类"
        : $"{Suggestion.Category.Name}{(Suggestion.Category.IsNew ? "（新建）" : string.Empty)}";

    public IReadOnlyList<string> CollectionNames => Suggestion.Collection is null
        ? []
        : [CollectionDisplay];

    public IReadOnlyList<string> TagNames => Suggestion.Tags
        .Select(tag => $"{tag.Name}{(tag.IsNew ? "（新建）" : string.Empty)}")
        .ToArray();

    public bool HasMissingItems => MissingItemDescriptions.Count > 0;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool applyTitle;

    [ObservableProperty]
    private bool applyPlacement;

    [ObservableProperty]
    private bool applyCategory;

    [ObservableProperty]
    private bool applyTags;

    [ObservableProperty]
    private bool applyNote;

    public void UpdateMissingItems(
        IReadOnlyDictionary<string, CollectionModel> collectionsByName,
        IReadOnlyDictionary<long, IReadOnlyList<CollectionCategory>> categoriesByCollectionId,
        IReadOnlyDictionary<long, IReadOnlyList<CollectionTag>> tagsByCollectionId)
    {
        MissingItemDescriptions.Clear();

        var collection = Suggestion.Collection;
        if (collection is null)
        {
            OnPropertyChanged(nameof(HasMissingItems));
            return;
        }

        var collectionId = collection.Id;
        if (collectionId is null or <= 0 && collectionsByName.TryGetValue(collection.Name, out var existingCollection))
        {
            collectionId = existingCollection.Id;
        }

        if (collectionId is null or <= 0)
        {
            MissingItemDescriptions.Add($"新集合：{collection.Name}");
        }

        if (Suggestion.Category is not null)
        {
            var categories = collectionId is > 0
                ? categoriesByCollectionId.GetValueOrDefault(collectionId.Value) ?? []
                : [];
            var hasCategory = Suggestion.Category.Id is > 0 ||
                categories.Any(category => string.Equals(category.Name, Suggestion.Category.Name, StringComparison.OrdinalIgnoreCase));
            if (!hasCategory)
            {
                MissingItemDescriptions.Add($"新二级分类：{collection.Name} / {Suggestion.Category.Name}");
            }
        }

        foreach (var tag in Suggestion.Tags)
        {
            var existingTags = collectionId is > 0
                ? tagsByCollectionId.GetValueOrDefault(collectionId.Value) ?? []
                : [];
            var hasTag = tag.Id is > 0 ||
                existingTags.Any(existing => string.Equals(existing.Name, tag.Name, StringComparison.OrdinalIgnoreCase));
            if (!hasTag)
            {
                MissingItemDescriptions.Add($"新集合标签：{collection.Name} / {tag.Name}");
            }
        }

        OnPropertyChanged(nameof(HasMissingItems));
    }

    private static AiSuggestion NormalizeSuggestion(AiSuggestion suggestion)
    {
        if (suggestion.Collection is null && suggestion.SuggestedCollections.Count > 0)
        {
            suggestion.Collection = new AiSuggestedEntity
            {
                Name = suggestion.SuggestedCollections.First(),
                IsNew = true
            };
        }

        if (suggestion.Tags.Count == 0 && suggestion.SuggestedTags.Count > 0)
        {
            suggestion.Tags = suggestion.SuggestedTags
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => new AiSuggestedEntity
                {
                    Name = name.Trim(),
                    IsNew = true
                })
                .ToArray();
        }

        return suggestion;
    }
}
