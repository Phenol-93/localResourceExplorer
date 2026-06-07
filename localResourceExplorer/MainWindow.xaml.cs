using System.Windows;
using System.Windows.Controls;
using LocalResourceExplorer.Models;
using LocalResourceExplorer.ViewModels;

namespace LocalResourceExplorer;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private void ResourceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is ListView listView)
        {
            viewModel.SetSelectedResources(listView.SelectedItems.OfType<ResourceItem>());
        }
    }
}
