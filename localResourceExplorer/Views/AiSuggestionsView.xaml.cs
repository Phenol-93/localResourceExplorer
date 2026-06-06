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
        TagRepository tagRepository)
    {
        InitializeComponent();
        DataContext = new AiSuggestionsViewModel(
            suggestions,
            resourceRepository,
            collectionRepository,
            tagRepository);
    }

    public bool HasApplied => DataContext is AiSuggestionsViewModel viewModel && viewModel.HasApplied;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
