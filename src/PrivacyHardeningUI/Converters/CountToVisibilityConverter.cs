using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PrivacyHardeningUI.Converters;

/// <summary>
/// Converts count to bool for IsVisible (count > 0 = true/visible, count = 0 = false/collapsed)
/// </summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count > 0;
        }

        if (value is long longCount)
        {
            return longCount > 0;
        }

        if (value is double doubleCount)
        {
            return doubleCount > 0;
        }

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException("CountToVisibilityConverter does not support ConvertBack");
    }
}
