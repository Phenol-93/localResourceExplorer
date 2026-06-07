using System.Windows;
using System.Windows.Controls;
using LocalResourceExplorer.Models;
using LocalResourceExplorer.Repositories;
using CollectionModel = LocalResourceExplorer.Models.Collection;

namespace LocalResourceExplorer.Views;

public partial class CollectionCategoryPickerDialog : Window
{
    private readonly CollectionCategoryRepository categoryRepository;
    private readonly CategoryOption noCategoryOption = new(null, "未分类");

    public CollectionCategoryPickerDialog(
        IEnumerable<CollectionModel> collections,
        CollectionCategoryRepository categoryRepository,
        string title,
        string prompt,
        long? fixedCollectionId = null)
    {
        InitializeComponent();
        this.categoryRepository = categoryRepository;
        Title = title;
        PromptTextBlock.Text = prompt;

        var collectionOptions = collections
            .Select(collection => new CollectionOption(collection, collection.Name))
            .ToArray();

        CollectionComboBox.ItemsSource = collectionOptions;
        CollectionComboBox.SelectedItem = fixedCollectionId is null
            ? collectionOptions.FirstOrDefault()
            : collectionOptions.FirstOrDefault(option => option.Collection.Id == fixedCollectionId.Value);
        CollectionComboBox.IsEnabled = fixedCollectionId is null;

        CategoryComboBox.ItemsSource = new[] { noCategoryOption };
        CategoryComboBox.SelectedIndex = 0;
    }

    public CollectionModel? SelectedCollection { get; private set; }

    public CollectionCategory? SelectedCategory { get; private set; }

    private async void CollectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CollectionComboBox.SelectedItem is not CollectionOption option)
        {
            CategoryComboBox.ItemsSource = new[] { noCategoryOption };
            CategoryComboBox.SelectedIndex = 0;
            return;
        }

        SelectedCollection = option.Collection;
        var categories = await categoryRepository.GetByCollectionAsync(option.Collection.Id);
        var categoryOptions = new[] { noCategoryOption }
            .Concat(categories.Select(category => new CategoryOption(category, category.Name)))
            .ToArray();

        CategoryComboBox.ItemsSource = categoryOptions;
        CategoryComboBox.SelectedIndex = 0;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedCollection = (CollectionComboBox.SelectedItem as CollectionOption)?.Collection;
        SelectedCategory = (CategoryComboBox.SelectedItem as CategoryOption)?.Category;
        if (SelectedCollection is null)
        {
            MessageBox.Show("请先选择一个集合。", "选择集合", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private sealed record CollectionOption(CollectionModel Collection, string Name);

    private sealed record CategoryOption(CollectionCategory? Category, string Name);
}
