using System.Windows;
using LocalResourceExplorer.Models;
using LocalResourceExplorer.Repositories;
using LocalResourceExplorer.ViewModels;

namespace LocalResourceExplorer.Views;

public partial class AiSuggestionsView : Window
{
    public AiSuggestionsView(
        IEnumerable<AiSuggestion> suggestions,
        ResourceRepository resourceRepository,
        CollectionRepository collectionRepository,
        CollectionCategoryRepository collectionCategoryRepository,
        CollectionTagRepository collectionTagRepository,
        ResourcePlacementRepository resourcePlacementRepository,
        ResourcePlacementTagRepository resourcePlacementTagRepository)
    {
        InitializeComponent();
        DataContext = new AiSuggestionsViewModel(
            suggestions,
            resourceRepository,
            collectionRepository,
            collectionCategoryRepository,
            collectionTagRepository,
            resourcePlacementRepository,
            resourcePlacementTagRepository);
    }

    public bool HasApplied => DataContext is AiSuggestionsViewModel viewModel && viewModel.HasApplied;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
