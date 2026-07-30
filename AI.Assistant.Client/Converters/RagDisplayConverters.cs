using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Client.Converters;

public class SourceTypeDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SourceType st)
        {
            return st switch
            {
                SourceType.Code => "Code",
                SourceType.Document => "文档",
                SourceType.Markdown => "Markdown",
                SourceType.Text => "文本",
                SourceType.Pdf => "PDF",
                _ => "未知"
            };
        }
        return "未知";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class SourceTypeBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SourceType st)
        {
            return st switch
            {
                SourceType.Code => new SolidColorBrush(Color.FromRgb(0xDB, 0xEE, 0xFF)),
                SourceType.Document or SourceType.Markdown => new SolidColorBrush(Color.FromRgb(0xD1, 0xFA, 0xE5)),
                _ => new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6))
            };
        }
        return new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class SourceTypeForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SourceType st)
        {
            return st switch
            {
                SourceType.Code => new SolidColorBrush(Color.FromRgb(0x1A, 0x73, 0xE8)),
                SourceType.Document or SourceType.Markdown => new SolidColorBrush(Color.FromRgb(0x05, 0x79, 0x3B)),
                _ => new SolidColorBrush(Color.FromRgb(0x5F, 0x6B, 0x7A))
            };
        }
        return new SolidColorBrush(Color.FromRgb(0x5F, 0x6B, 0x7A));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class TextTruncateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrEmpty(s))
        {
            int maxLen = parameter is string p && int.TryParse(p, out var l) ? l : 80;
            return s.Length > maxLen ? s[..maxLen] + "..." : s;
        }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
