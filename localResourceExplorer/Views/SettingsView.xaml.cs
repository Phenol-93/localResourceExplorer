using System.ComponentModel;
using System.Windows;
using LocalResourceExplorer.ViewModels;

namespace LocalResourceExplorer.Views;

public partial class SettingsView : Window
{
    private bool isSyncingPassword;

    public SettingsView()
    {
        InitializeComponent();

        var viewModel = new SettingsViewModel();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        DataContext = viewModel;
    }

    private void ApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (isSyncingPassword || DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        viewModel.AiApiKey = ApiKeyPasswordBox.Password;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsViewModel.AiApiKey) ||
            sender is not SettingsViewModel viewModel ||
            ApiKeyPasswordBox.Password == viewModel.AiApiKey)
        {
            return;
        }

        try
        {
            isSyncingPassword = true;
            ApiKeyPasswordBox.Password = viewModel.AiApiKey;
        }
        finally
        {
            isSyncingPassword = false;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
