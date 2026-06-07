using System.Windows;

namespace LocalResourceExplorer.Services;

public sealed class ThemeService
{
    private const string LightThemePath = "Themes/LightTheme.xaml";
    private const string DarkThemePath = "Themes/DarkTheme.xaml";

    private static bool isDarkTheme;

    public bool IsDarkTheme => isDarkTheme;

    public void ApplyTheme(bool useDarkTheme)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var nextThemePath = useDarkTheme ? DarkThemePath : LightThemePath;
        var nextThemeUri = new Uri(nextThemePath, UriKind.Relative);

        for (var index = dictionaries.Count - 1; index >= 0; index--)
        {
            var source = dictionaries[index].Source?.OriginalString;
            if (IsThemeDictionary(source))
            {
                dictionaries.RemoveAt(index);
            }
        }

        dictionaries.Insert(0, new ResourceDictionary { Source = nextThemeUri });
        isDarkTheme = useDarkTheme;
    }

    private static bool IsThemeDictionary(string? source)
    {
        return source?.EndsWith(LightThemePath, StringComparison.OrdinalIgnoreCase) == true ||
            source?.EndsWith(DarkThemePath, StringComparison.OrdinalIgnoreCase) == true;
    }

    public bool ToggleTheme()
    {
        ApplyTheme(!IsDarkTheme);
        return IsDarkTheme;
    }
}
