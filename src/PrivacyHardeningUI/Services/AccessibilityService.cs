using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace PrivacyHardeningUI.Services;

/// <summary>
/// Service for managing WCAG 2.1 Level AA accessibility features.
/// </summary>
public interface IAccessibilityService
{
    /// <summary>
    /// Gets whether high contrast mode is enabled.
    /// </summary>
    bool IsHighContrastMode { get; }

    /// <summary>
    /// Gets whether reduced motion is preferred.
    /// </summary>
    bool PreferReducedMotion { get; }

    /// <summary>
    /// Gets the current text scaling factor (1.0 = 100%).
    /// </summary>
    double TextScaleFactor { get; }

    /// <summary>
    /// Set the text scaling factor for better readability.
    /// </summary>
    /// <param name="scale">Scale factor (1.0 to 2.0 recommended for WCAG AA)</param>
    void SetTextScaleFactor(double scale);

    /// <summary>
    /// Enable or disable reduced motion animations.
    /// </summary>
    void SetReducedMotion(bool enabled);

    /// <summary>
    /// Check if an element has appropriate keyboard focus visuals.
    /// </summary>
    bool HasKeyboardFocusVisual(Control control);

    /// <summary>
    /// Validate color contrast ratio meets WCAG AA standards.
    /// </summary>
    /// <param name="foreground">Foreground color</param>
    /// <param name="background">Background color</param>
    /// <param name="isLargeText">Whether text is large (18pt+ or 14pt+ bold)</param>
    /// <returns>True if contrast ratio meets WCAG AA requirements</returns>
    bool ValidateContrastRatio(Color foreground, Color background, bool isLargeText = false);
}

/// <summary>
/// Implementation of accessibility service following WCAG 2.1 Level AA guidelines.
/// </summary>
public class AccessibilityService : IAccessibilityService
{
    private bool _highContrastMode;
    private bool _preferReducedMotion;
    private double _textScaleFactor = 1.0;

    // WCAG AA contrast ratio requirements
    private const double MinContrastRatioNormalText = 4.5;
    private const double MinContrastRatioLargeText = 3.0;

    public AccessibilityService()
    {
        DetectSystemAccessibilitySettings();
    }

    public bool IsHighContrastMode => _highContrastMode;
    public bool PreferReducedMotion => _preferReducedMotion;
    public double TextScaleFactor => _textScaleFactor;

    public void SetTextScaleFactor(double scale)
    {
        // WCAG AA: Text should be resizable up to 200% without loss of functionality
        _textScaleFactor = Math.Clamp(scale, 1.0, 2.0);
    }

    public void SetReducedMotion(bool enabled)
    {
        _preferReducedMotion = enabled;
    }

    public bool HasKeyboardFocusVisual(Control control)
    {
        // WCAG 2.4.7: Focus Visible (Level AA)
        // Verify control has focus visual defined
        return control.GetValue(Control.FocusAdornerProperty) != null
            || control.Classes.Contains("keyboard-focus");
    }

    public bool ValidateContrastRatio(Color foreground, Color background, bool isLargeText = false)
    {
        var ratio = CalculateContrastRatio(foreground, background);
        var requiredRatio = isLargeText ? MinContrastRatioLargeText : MinContrastRatioNormalText;
        return ratio >= requiredRatio;
    }

    /// <summary>
    /// Detect system accessibility settings (Windows high contrast, reduced motion, etc.)
    /// </summary>
    private void DetectSystemAccessibilitySettings()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Detect Windows high contrast mode
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Control Panel\Accessibility\HighContrast");

                var flags = key?.GetValue("Flags");
                if (flags is int flagsValue)
                {
                    // HCF_HIGHCONTRASTON = 0x00000001
                    _highContrastMode = (flagsValue & 1) == 1;
                }

                // Windows doesn't have a system-wide reduced motion setting by default
                // This can be extended to check for third-party accessibility tools
                _preferReducedMotion = false;
            }
        }
        catch
        {
            // Fall back to defaults if detection fails
            _highContrastMode = false;
            _preferReducedMotion = false;
        }
    }

    /// <summary>
    /// Calculate contrast ratio between two colors using WCAG formula.
    /// </summary>
    /// <remarks>
    /// WCAG contrast ratio formula: (L1 + 0.05) / (L2 + 0.05)
    /// where L1 is the relative luminance of the lighter color
    /// and L2 is the relative luminance of the darker color.
    /// </remarks>
    private static double CalculateContrastRatio(Color color1, Color color2)
    {
        var luminance1 = GetRelativeLuminance(color1);
        var luminance2 = GetRelativeLuminance(color2);

        var lighter = Math.Max(luminance1, luminance2);
        var darker = Math.Min(luminance1, luminance2);

        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// Calculate relative luminance using WCAG formula.
    /// </summary>
    private static double GetRelativeLuminance(Color color)
    {
        var r = GetLuminanceComponent(color.R / 255.0);
        var g = GetLuminanceComponent(color.G / 255.0);
        var b = GetLuminanceComponent(color.B / 255.0);

        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    /// <summary>
    /// Get luminance component with gamma correction.
    /// </summary>
    private static double GetLuminanceComponent(double component)
    {
        return component <= 0.03928
            ? component / 12.92
            : Math.Pow((component + 0.055) / 1.055, 2.4);
    }
}
