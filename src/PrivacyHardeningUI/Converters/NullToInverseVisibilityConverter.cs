using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PrivacyHardeningUI.Converters;

/// <summary>
/// Inverse of <see cref="NullToVisibilityConverter"/>.
/// - null/empty => true
/// - non-null/non-empty => false
/// </summary>
public sealed class NullToInverseVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            return string.IsNullOrWhiteSpace(str);
        }

        return value == null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException("NullToInverseVisibilityConverter does not support ConvertBack");
}
