using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PrivacyHardeningUI.Converters;

/// <summary>
/// Converts boolean to checkmark or X (true = ✓, false = ✗)
/// </summary>
public sealed class BoolToCheckmarkConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? "✓" : "✗";
        }

        return "?";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException("BoolToCheckmarkConverter does not support ConvertBack");
    }
}
