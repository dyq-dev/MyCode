using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AI.Assistant.Client.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            bool invert = parameter is string s && s == "Invert";
            return (invert ? !boolValue : boolValue) ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            bool invert = parameter is string s && s == "Invert";
            bool result = visibility == Visibility.Visible;
            return invert ? !result : result;
        }
        return false;
    }
}
