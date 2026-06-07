using System.Windows;
using System.Windows.Input;
using CollectionModel = LocalResourceExplorer.Models.Collection;

namespace LocalResourceExplorer.Views;

public partial class CollectionPickerDialog : Window
{
    public CollectionPickerDialog(IEnumerable<CollectionModel> collections, string title, string prompt)
    {
        InitializeComponent();
        Title = title;
        PromptTextBlock.Text = prompt;
        CollectionListBox.ItemsSource = collections.ToArray();
        CollectionListBox.SelectedIndex = 0;
        CollectionListBox.Focus();
    }

    public CollectionModel? SelectedCollection => CollectionListBox.SelectedItem as CollectionModel;

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        AcceptSelection();
    }

    private void CollectionListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        AcceptSelection();
    }

    private void AcceptSelection()
    {
        if (SelectedCollection is null)
        {
            MessageBox.Show("请先选择一个集合。", "选择集合", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
