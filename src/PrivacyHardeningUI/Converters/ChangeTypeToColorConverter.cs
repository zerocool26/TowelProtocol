using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PrivacyHardeningUI.Converters;

/// <summary>
/// Converts change type string to color for diff view
/// </summary>
public sealed class ChangeTypeToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string changeType)
        {
            return changeType.ToUpperInvariant() switch
            {
                "ADDED" => Colors.Green,
                "MODIFIED" => Colors.Orange,
                "REMOVED" => Colors.Red,
                "UNCHANGED" => Colors.Gray,
                _ => Colors.DarkGray
            };
        }

        return Colors.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException("ChangeTypeToColorConverter does not support ConvertBack");
    }
}
