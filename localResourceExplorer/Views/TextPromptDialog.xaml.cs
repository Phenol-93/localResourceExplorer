using System.Windows;

namespace LocalResourceExplorer.Views;

public partial class TextPromptDialog : Window
{
    public TextPromptDialog(string title, string prompt, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;
        PromptTextBlock.Text = prompt;
        ValueTextBox.Text = defaultValue;
        ValueTextBox.SelectAll();
        ValueTextBox.Focus();
    }

    public string ResponseText => ValueTextBox.Text.Trim();

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
