using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Headers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalResourceExplorer.Repositories;
using LocalResourceExplorer.Services;

namespace LocalResourceExplorer.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private const string ThemeSettingKey = "theme";
    private const string AiBaseUrlSettingKey = "ai_base_url";
    private const string AiApiKeySettingKey = "ai_api_key";
    private const string AiModelNameSettingKey = "ai_model_name";
    private const string LightThemeValue = "light";
    private const string DarkThemeValue = "dark";

    private readonly DatabaseService databaseService = new();
    private readonly AppSettingsRepository appSettingsRepository;
    private readonly ThemeService themeService = new();

    public SettingsViewModel()
    {
        appSettingsRepository = new AppSettingsRepository(databaseService);
        DatabasePath = databaseService.DatabasePath;
        _ = LoadAsync();
    }

    public ObservableCollection<string> ThemeOptions { get; } = ["浅色", "深色"];

    [ObservableProperty]
    private string selectedTheme = "浅色";

    [ObservableProperty]
    private string databasePath = string.Empty;

    [ObservableProperty]
    private string aiBaseUrl = string.Empty;

    [ObservableProperty]
    private string aiApiKey = string.Empty;

    [ObservableProperty]
    private string aiModelName = string.Empty;

    [ObservableProperty]
    private string statusText = "设置会保存到 SQLite 的 app_settings 表。";

    partial void OnSelectedThemeChanged(string value)
    {
        themeService.ApplyTheme(IsDarkTheme(value));
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            await databaseService.InitializeAsync();
            var settings = await appSettingsRepository.GetAllAsync();

            if (settings.TryGetValue(ThemeSettingKey, out var theme))
            {
                SelectedTheme = string.Equals(theme, DarkThemeValue, StringComparison.OrdinalIgnoreCase)
                    ? "深色"
                    : "浅色";
            }

            AiBaseUrl = settings.GetValueOrDefault(AiBaseUrlSettingKey) ?? string.Empty;
            AiApiKey = settings.GetValueOrDefault(AiApiKeySettingKey) ?? string.Empty;
            AiModelName = settings.GetValueOrDefault(AiModelNameSettingKey) ?? string.Empty;
            StatusText = "设置已加载。";
        }
        catch (Exception ex)
        {
            StatusText = $"设置加载失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            await databaseService.InitializeAsync();
            await appSettingsRepository.SetAsync(ThemeSettingKey, IsDarkTheme(SelectedTheme) ? DarkThemeValue : LightThemeValue);
            await appSettingsRepository.SetAsync(AiBaseUrlSettingKey, AiBaseUrl.Trim());
            await appSettingsRepository.SetAsync(AiApiKeySettingKey, AiApiKey);
            await appSettingsRepository.SetAsync(AiModelNameSettingKey, AiModelName.Trim());

            themeService.ApplyTheme(IsDarkTheme(SelectedTheme));
            StatusText = "设置已保存。";
        }
        catch (Exception ex)
        {
            StatusText = $"设置保存失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task TestAiConnectionAsync()
    {
        var baseUrl = AiBaseUrl.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            StatusText = "请先填写 AI Base URL。";
            return;
        }

        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            if (!string.IsNullOrWhiteSpace(AiApiKey))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AiApiKey);
            }

            var endpoint = BuildModelsEndpoint(baseUrl);
            using var response = await client.GetAsync(endpoint);
            if (!response.IsSuccessStatusCode)
            {
                AppLog.AiError(
                    new InvalidOperationException($"HTTP {(int)response.StatusCode}"),
                    "AI connection test returned an unsuccessful response");
            }

            StatusText = response.IsSuccessStatusCode
                ? "AI 连接测试成功。"
                : $"AI 连接已返回响应：HTTP {(int)response.StatusCode}";
        }
        catch (Exception ex)
        {
            StatusText = $"AI 连接测试失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ClearApiKeyAsync()
    {
        AiApiKey = string.Empty;
        try
        {
            await appSettingsRepository.RemoveAsync(AiApiKeySettingKey);
            StatusText = "API Key 已清除。";
        }
        catch (Exception ex)
        {
            StatusText = $"API Key 清除失败：{ex.Message}";
        }
    }

    private static bool IsDarkTheme(string theme)
    {
        return string.Equals(theme, "深色", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(theme, DarkThemeValue, StringComparison.OrdinalIgnoreCase);
    }

    private static Uri BuildModelsEndpoint(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        if (!trimmed.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
        {
            trimmed += "/models";
        }

        return new Uri(trimmed, UriKind.Absolute);
    }
}
