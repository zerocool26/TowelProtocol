using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningUI.Converters;

/// <summary>
/// Converts RiskLevel enum to color brush for UI display
/// </summary>
public sealed class RiskLevelToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is RiskLevel riskLevel)
        {
            return riskLevel switch
            {
                RiskLevel.Low => new SolidColorBrush(Colors.Green),
                RiskLevel.Medium => new SolidColorBrush(Colors.Orange),
                RiskLevel.High => new SolidColorBrush(Colors.Red),
                RiskLevel.Critical => new SolidColorBrush(Colors.DarkRed),
                _ => new SolidColorBrush(Colors.Gray)
            };
        }

        return new SolidColorBrush(Colors.Gray);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException("RiskLevelToBrushConverter does not support ConvertBack");
    }
}
