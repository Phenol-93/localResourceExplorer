using System.Globalization;
using System.Windows.Data;

namespace LocalResourceExplorer.Helpers;

public sealed class FileSizeDisplayConverter : IValueConverter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null || !double.TryParse(value.ToString(), out var sizeBytes) || sizeBytes < 0)
        {
            return "-";
        }

        var unitIndex = 0;
        while (sizeBytes >= 1024 && unitIndex < Units.Length - 1)
        {
            sizeBytes /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{sizeBytes:0} {Units[unitIndex]}"
            : $"{sizeBytes:0.##} {Units[unitIndex]}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
