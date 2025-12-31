using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PrivacyHardeningUI.Converters;

/// <summary>
/// Converts enum values to human-readable strings
/// </summary>
public sealed class EnumToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
            return string.Empty;

        if (value is Enum enumValue)
        {
            // Convert camelCase or PascalCase to space-separated words
            var str = enumValue.ToString();
            var result = System.Text.RegularExpressions.Regex.Replace(
                str,
                "([a-z])([A-Z])",
                "$1 $2"
            );
            return result;
        }

        return value.ToString() ?? string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException("EnumToStringConverter does not support ConvertBack");
    }
}
