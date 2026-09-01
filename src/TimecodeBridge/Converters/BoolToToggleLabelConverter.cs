using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using System.Globalization;
using System.Windows.Data;

namespace TimecodeBridge.Converters;

public sealed class BoolToToggleLabelConverter : IValueConverter
{
    public static readonly BoolToToggleLabelConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "ON" : "OFF";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
