using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PrivacyHardeningUI.Converters;

/// <summary>
/// Converts boolean to bool for IsVisible (true = true/visible, false = false/collapsed)
/// In Avalonia, we use bool directly for IsVisible instead of Visibility enum
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue;
        }

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue;
        }

        return false;
    }
}
