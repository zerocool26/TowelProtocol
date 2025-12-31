using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PrivacyHardeningUI.Converters;

/// <summary>
/// Converts null to bool for IsVisible (null = false/collapsed, non-null = true/visible)
/// </summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // For strings, check if null or empty
        if (value is string str)
        {
            return !string.IsNullOrWhiteSpace(str);
        }

        return value != null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException("NullToVisibilityConverter does not support ConvertBack");
    }
}
