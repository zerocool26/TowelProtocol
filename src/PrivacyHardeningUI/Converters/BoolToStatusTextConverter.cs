using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PrivacyHardeningUI.Converters;

/// <summary>
/// Converts boolean to status text (true = APPLIED, false = NOT APPLIED)
/// </summary>
public sealed class BoolToStatusTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? "APPLIED" : "NOT APPLIED";
        }

        return "UNKNOWN";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException("BoolToStatusTextConverter does not support ConvertBack");
    }
}
