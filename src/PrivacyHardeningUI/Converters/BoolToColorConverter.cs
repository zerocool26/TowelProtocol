using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PrivacyHardeningUI.Converters;

/// <summary>
/// Converts boolean to color (true = Green, false = Red)
/// </summary>
public sealed class BoolToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Colors.Green : Colors.Red;
        }

        return Colors.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException("BoolToColorConverter does not support ConvertBack");
    }
}
