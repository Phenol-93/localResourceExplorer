using System.Globalization;
using System.Windows.Data;

namespace LocalResourceExplorer.Helpers;

public sealed class DurationDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return "-";
        }

        if (!long.TryParse(value.ToString(), out var durationMs) || durationMs <= 0)
        {
            return "-";
        }

        var duration = TimeSpan.FromMilliseconds(durationMs);
        return duration.TotalHours >= 1
            ? duration.ToString(@"hh\:mm\:ss", culture)
            : duration.ToString(@"mm\:ss", culture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
