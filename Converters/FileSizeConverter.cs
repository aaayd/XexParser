using System.Globalization;
using System.Windows.Data;

namespace XexTool.Converters;

public class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is uint bytes)
        {
            return FormatBytes(bytes);
        }
        if (value is long longBytes)
        {
            return FormatBytes((uint)longBytes);
        }
        if (value is int intBytes)
        {
            return FormatBytes((uint)intBytes);
        }
        return "0 B";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static string FormatBytes(uint bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < suffixes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {suffixes[order]}";
    }
}
