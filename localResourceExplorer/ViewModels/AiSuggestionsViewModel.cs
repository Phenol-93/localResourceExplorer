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
    private readonly CollectionRepository collectionRepository;
    private readonly ResourceRepository resourceRepository;
    private readonly TagRepository tagRepository;
    private Dictionary<string, CollectionModel> collectionsByName = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, Tag> tagsByName = new(StringComparer.OrdinalIgnoreCase);

    public AiSuggestionsViewModel(
        IEnumerable<AiSuggestion> suggestions,
        ResourceRepository resourceRepository,
        CollectionRepository collectionRepository,
        TagRepository tagRepository)
    {
        this.resourceRepository = resourceRepository;
        this.collectionRepository = collectionRepository;
        this.tagRepository = tagRepository;

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
    private string statusText = "请选择要应用的建议。不存在的集合或标签需要额外确认后才会创建。";

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

        var missingCollections = selectedSuggestions
            .Where(item => item.ApplyCollections)
            .SelectMany(item => item.MissingCollectionNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missingTags = selectedSuggestions
            .Where(item => item.ApplyTags)
            .SelectMany(item => item.MissingTagNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if ((missingCollections.Length > 0 || missingTags.Length > 0) && !ConfirmCreateMissingItems)
        {
            StatusText = "存在尚未创建的集合或标签。请勾选确认创建，或取消对应建议后再应用。";
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

        if (item.ApplyCollections)
        {
            foreach (var collectionName in item.CollectionNames)
            {
                var collectionId = await GetOrCreateCollectionIdAsync(collectionName);
                if (collectionId is not null)
                {
                    changedCount += await collectionRepository.AddResourceAsync(suggestion.ResourceId, collectionId.Value);
                }
            }
        }

        if (item.ApplyTags)
        {
            foreach (var tagName in item.TagNames)
            {
                var tagId = await GetOrCreateTagIdAsync(tagName);
                if (tagId is not null)
                {
                    changedCount += await tagRepository.AddResourceAsync(suggestion.ResourceId, tagId.Value);
                }
            }
        }

        return changedCount;
    }

    private async Task<long?> GetOrCreateCollectionIdAsync(string name)
    {
        if (collectionsByName.TryGetValue(name, out var collection))
        {
            return collection.Id;
        }

        if (!ConfirmCreateMissingItems)
        {
            return null;
        }

        var id = await collectionRepository.CreateAsync(name);
        await LoadExistingMetadataAsync();
        return id;
    }

    private async Task<long?> GetOrCreateTagIdAsync(string name)
    {
        if (tagsByName.TryGetValue(name, out var tag))
        {
            return tag.Id;
        }

        if (!ConfirmCreateMissingItems)
        {
            return null;
        }

        var id = await tagRepository.CreateAsync(name, "#EEF6FF");
        await LoadExistingMetadataAsync();
        return id;
    }

    private async Task LoadExistingMetadataAsync()
    {
        collectionsByName = (await collectionRepository.GetAllAsync())
            .ToDictionary(collection => collection.Name, StringComparer.OrdinalIgnoreCase);
        tagsByName = (await tagRepository.GetAllAsync())
            .ToDictionary(tag => tag.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var suggestion in Suggestions)
        {
            suggestion.UpdateMissingItems(collectionsByName.Keys, tagsByName.Keys);
        }

        var missingCollections = Suggestions
            .SelectMany(item => item.MissingCollectionNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missingTags = Suggestions
            .SelectMany(item => item.MissingTagNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        HasMissingItems = missingCollections.Length > 0 || missingTags.Length > 0;
        MissingItemsText = BuildMissingItemsText(missingCollections, missingTags);
    }

    private static string BuildMissingItemsText(IReadOnlyCollection<string> collections, IReadOnlyCollection<string> tags)
    {
        if (collections.Count == 0 && tags.Count == 0)
        {
            return "所有建议集合和标签都已存在。";
        }

        var parts = new List<string>();
        if (collections.Count > 0)
        {
            parts.Add($"新集合：{string.Join("、", collections)}");
        }

        if (tags.Count > 0)
        {
            parts.Add($"新标签：{string.Join("、", tags)}");
        }

        return string.Join("；", parts);
    }
}

public sealed partial class AiSuggestionItemViewModel : ObservableObject
{
    public AiSuggestionItemViewModel(AiSuggestion suggestion)
    {
        Suggestion = suggestion;
        IsSelected = true;
        ApplyTitle = !string.IsNullOrWhiteSpace(suggestion.SuggestedTitle);
        ApplyCollections = suggestion.SuggestedCollections.Count > 0;
        ApplyTags = suggestion.SuggestedTags.Count > 0;
        ApplyNote = !string.IsNullOrWhiteSpace(suggestion.SuggestedNote);
        CollectionNames = CleanNames(suggestion.SuggestedCollections);
        TagNames = CleanNames(suggestion.SuggestedTags);
    }

    public AiSuggestion Suggestion { get; }

    public IReadOnlyList<string> CollectionNames { get; }

    public IReadOnlyList<string> TagNames { get; }

    public ObservableCollection<string> MissingCollectionNames { get; } = [];

    public ObservableCollection<string> MissingTagNames { get; } = [];

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool applyTitle;

    [ObservableProperty]
    private bool applyCollections;

    [ObservableProperty]
    private bool applyTags;

    [ObservableProperty]
    private bool applyNote;

    [ObservableProperty]
    private bool hasMissingCollections;

    [ObservableProperty]
    private bool hasMissingTags;

    public void UpdateMissingItems(IEnumerable<string> existingCollections, IEnumerable<string> existingTags)
    {
        var collectionSet = new HashSet<string>(existingCollections, StringComparer.OrdinalIgnoreCase);
        var tagSet = new HashSet<string>(existingTags, StringComparer.OrdinalIgnoreCase);

        MissingCollectionNames.Clear();
        foreach (var name in CollectionNames.Where(name => !collectionSet.Contains(name)))
        {
            MissingCollectionNames.Add(name);
        }

        MissingTagNames.Clear();
        foreach (var name in TagNames.Where(name => !tagSet.Contains(name)))
        {
            MissingTagNames.Add(name);
        }

        HasMissingCollections = MissingCollectionNames.Count > 0;
        HasMissingTags = MissingTagNames.Count > 0;
    }

    private static IReadOnlyList<string> CleanNames(IEnumerable<string> names)
    {
        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
