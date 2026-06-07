using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalResourceExplorer.Models;
using CollectionModel = LocalResourceExplorer.Models.Collection;

namespace LocalResourceExplorer.ViewModels;

public sealed partial class CollectionNavigationItemViewModel : ObservableObject
{
    public CollectionNavigationItemViewModel(CollectionModel collection)
    {
        Collection = collection;
    }

    public CollectionModel Collection { get; }

    public long Id => Collection.Id;

    public string Name => Collection.Name;

    public ObservableCollection<CollectionCategoryNavigationItemViewModel> Categories { get; } = [];

    [ObservableProperty]
    private bool isExpanded;

    [ObservableProperty]
    private bool isSelected;
}

public sealed partial class CollectionCategoryNavigationItemViewModel : ObservableObject
{
    public CollectionCategoryNavigationItemViewModel(
        CollectionNavigationItemViewModel parent,
        CollectionCategory category)
    {
        Parent = parent;
        Category = category;
    }

    public CollectionNavigationItemViewModel Parent { get; }

    public CollectionCategory Category { get; }

    public long Id => Category.Id;

    public string Name => Category.Name;

    [ObservableProperty]
    private bool isSelected;
}

public sealed partial class CollectionTagNavigationItemViewModel : ObservableObject
{
    public CollectionTagNavigationItemViewModel(CollectionTag tag)
    {
        Tag = tag;
    }

    public CollectionTag Tag { get; }

    public long Id => Tag.Id;

    public string Name => Tag.Name;

    public string Color => string.IsNullOrWhiteSpace(Tag.Color) ? "#EEF6FF" : Tag.Color;

    [ObservableProperty]
    private bool isSelected;
}
