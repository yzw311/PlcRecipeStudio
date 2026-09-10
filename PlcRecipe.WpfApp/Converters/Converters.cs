using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PlcRecipe.WpfApp.Converters;

/// <summary>动态资源键 → 画刷（设备状态点着色）。</summary>
public sealed class ResourceKeyToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key && !string.IsNullOrEmpty(key))
        {
            var brush = Application.Current.TryFindResource(key) as Brush;
            if (brush != null) return brush;
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
