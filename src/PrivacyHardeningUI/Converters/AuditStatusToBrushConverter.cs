using System;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using PrivacyHardeningUI.ViewModels;

namespace PrivacyHardeningUI.Converters;

public sealed class AuditStatusToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (Application.Current?.Resources is null)
            return Brushes.Gray;

        var status = value as AuditComplianceStatus?;
        if (value is AuditComplianceStatus s) status = s;

        var key = status switch
        {
            AuditComplianceStatus.Compliant => "SuccessBrush",
            AuditComplianceStatus.NonCompliant => "ErrorBrush",
            AuditComplianceStatus.NotApplicable => "TextTertiaryBrush",
            _ => "WarningBrush"
        };

        if (Application.Current.Resources.TryGetValue(key, out var brush) && brush is IBrush b)
            return b;

        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
