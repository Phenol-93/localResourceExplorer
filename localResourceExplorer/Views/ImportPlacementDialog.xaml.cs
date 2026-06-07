using System.Windows;
using System.Windows.Controls;
using LocalResourceExplorer.Models;
using LocalResourceExplorer.Repositories;
using CollectionModel = LocalResourceExplorer.Models.Collection;

namespace LocalResourceExplorer.Views;

public partial class ImportPlacementDialog : Window
{
    private readonly CollectionCategoryRepository categoryRepository;
    private readonly CollectionOption noCollectionOption = new(null, "不选择集合");
    private readonly CategoryOption noCategoryOption = new(null, "无");

    public ImportPlacementDialog(
        IEnumerable<CollectionModel> collections,
        CollectionCategoryRepository categoryRepository)
    {
        InitializeComponent();
        this.categoryRepository = categoryRepository;

        var options = new[] { noCollectionOption }
            .Concat(collections.Select(collection => new CollectionOption(collection, collection.Name)))
            .ToArray();

        CollectionComboBox.ItemsSource = options;
        CollectionComboBox.SelectedIndex = 0;
        CategoryComboBox.ItemsSource = new[] { noCategoryOption };
        CategoryComboBox.SelectedIndex = 0;
        CategoryComboBox.IsEnabled = false;
    }

    public CollectionModel? SelectedCollection { get; private set; }

    public CollectionCategory? SelectedCategory { get; private set; }

    private async void CollectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CollectionComboBox.SelectedItem is not CollectionOption option || option.Collection is null)
        {
            SelectedCollection = null;
            SelectedCategory = null;
            CategoryComboBox.ItemsSource = new[] { noCategoryOption };
            CategoryComboBox.SelectedIndex = 0;
            CategoryComboBox.IsEnabled = false;
            return;
        }

        SelectedCollection = option.Collection;
        var categories = await categoryRepository.GetByCollectionAsync(option.Collection.Id);
        var categoryOptions = new[] { noCategoryOption }
            .Concat(categories.Select(category => new CategoryOption(category, category.Name)))
            .ToArray();

        CategoryComboBox.ItemsSource = categoryOptions;
        CategoryComboBox.SelectedIndex = 0;
        CategoryComboBox.IsEnabled = categoryOptions.Length > 1;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedCollection = (CollectionComboBox.SelectedItem as CollectionOption)?.Collection;
        SelectedCategory = (CategoryComboBox.SelectedItem as CategoryOption)?.Category;
        DialogResult = true;
    }

    private sealed record CollectionOption(CollectionModel? Collection, string Name);

    private sealed record CategoryOption(CollectionCategory? Category, string Name);
}
