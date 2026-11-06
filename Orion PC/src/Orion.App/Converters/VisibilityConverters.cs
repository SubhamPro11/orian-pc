using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Orion.App.Converters;

public class StringVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is string s && !string.IsNullOrWhiteSpace(s)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool b = value switch
        {
            bool flag => flag,
            int count => count > 0,
            long lCount => lCount > 0,
            double dCount => dCount > 0,
            string s => !string.IsNullOrWhiteSpace(s),
            System.Collections.ICollection col => col.Count > 0,
            _ => value != null
        };

        if (parameter is string p && p.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
        {
            b = !b;
        }

        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class StringFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value == null) return string.Empty;
        if (parameter is string format)
        {
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, format, value);
        }
        return value.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
