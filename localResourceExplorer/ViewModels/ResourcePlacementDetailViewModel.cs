using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalResourceExplorer.Models;

namespace LocalResourceExplorer.ViewModels;

public sealed partial class ResourcePlacementDetailViewModel : ObservableObject
{
    public ResourcePlacementDetailViewModel(ResourcePlacement placement)
    {
        Placement = placement;
        SelectedCategoryOptionId = placement.CategoryId ?? 0;
    }

    public ResourcePlacement Placement { get; }

    public long PlacementId => Placement.Id;

    public long ResourceId => Placement.ResourceId;

    public long CollectionId => Placement.CollectionId;

    public string CollectionName => Placement.CollectionName;

    public string CategoryName => string.IsNullOrWhiteSpace(Placement.CategoryName)
        ? "未分类"
        : Placement.CategoryName;

    public ObservableCollection<PlacementCategoryOption> CategoryOptions { get; } = [];

    public ObservableCollection<CollectionTag> AvailableTags { get; } = [];

    public ObservableCollection<ResourcePlacementTagDetailViewModel> Tags { get; } = [];

    [ObservableProperty]
    private long selectedCategoryOptionId;

    [ObservableProperty]
    private CollectionTag? selectedAvailableTag;
}

public sealed class PlacementCategoryOption
{
    public long Id { get; init; }

    public string Name { get; init; } = string.Empty;
}

public sealed class ResourcePlacementTagDetailViewModel
{
    public ResourcePlacementTagDetailViewModel(
        ResourcePlacementDetailViewModel placement,
        ResourcePlacementTag tag)
    {
        Placement = placement;
        Tag = tag;
    }

    public ResourcePlacementDetailViewModel Placement { get; }

    public ResourcePlacementTag Tag { get; }

    public long PlacementId => Tag.PlacementId;

    public long TagId => Tag.TagId;

    public string TagName => Tag.TagName;

    public string Color => string.IsNullOrWhiteSpace(Tag.Color) ? "#EEF6FF" : Tag.Color;
}
