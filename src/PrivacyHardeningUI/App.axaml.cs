using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PrivacyHardeningUI.Services;
using PrivacyHardeningUI.ViewModels;
using PrivacyHardeningUI.Views;

namespace PrivacyHardeningUI;

public partial class App : Application
{
    private IHost? _host;
    private bool _isDarkMode;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Build dependency injection container
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Register service client
                services.AddSingleton<ServiceClient>();

                // Register theme service
                services.AddSingleton<IThemeService>(sp => new ThemeService(this));

                // Register accessibility service (WCAG 2.1 Level AA)
                services.AddSingleton<IAccessibilityService, AccessibilityService>();

                // Register telemetry monitoring service
                services.AddSingleton<ITelemetryMonitorService, TelemetryMonitorService>();

                // Register ViewModels
                services.AddTransient<MainViewModel>();
                services.AddTransient<PolicySelectionViewModel>();
                services.AddTransient<AuditViewModel>();
                services.AddTransient<DiffViewModel>();
                services.AddTransient<TelemetryMonitorViewModel>();

                // Register Views
                services.AddTransient<MainWindow>();
            })
            .Build();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _host.Services.GetRequiredService<MainWindow>();
        }

        // Initialize with system theme or default to light
        InitializeTheme();

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Initialize theme based on system preferences or default to light mode.
    /// </summary>
    private void InitializeTheme()
    {
        // Detect system theme preference (Windows 10/11)
        var systemDarkMode = DetectSystemDarkMode();
        SetTheme(systemDarkMode);
    }

    /// <summary>
    /// Detect if Windows is using dark mode.
    /// </summary>
    private bool DetectSystemDarkMode()
    {
        try
        {
            // Check Windows registry for dark mode setting
            if (OperatingSystem.IsWindows())
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

                var value = key?.GetValue("AppsUseLightTheme");
                if (value is int useLightTheme)
                {
                    return useLightTheme == 0; // 0 = dark mode, 1 = light mode
                }
            }
        }
        catch
        {
            // Fall back to light mode if detection fails
        }

        return false;
    }

    /// <summary>
    /// Switch between light and dark theme with smooth resource transition.
    /// </summary>
    /// <param name="dark">True to use dark theme, false for light.</param>
    public void SetTheme(bool dark)
    {
        _isDarkMode = dark;

        // Update RequestedThemeVariant for Fluent Theme
        RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        if (Resources?.MergedDictionaries == null) return;

        // Remove existing theme resources if present
        var existing = Resources.MergedDictionaries
            .OfType<ResourceInclude>()
            .FirstOrDefault(r => r.Source != null && r.Source.OriginalString.Contains("ThemeResources"));

        if (existing != null)
        {
            Resources.MergedDictionaries.Remove(existing);
        }

        // Load appropriate theme resources
        var fileName = dark ? "ThemeResources.Dark.axaml" : "ThemeResources.Light.axaml";
        var uri = new Uri($"avares://PrivacyHardeningUI/Styles/{fileName}");

        Resources.MergedDictionaries.Add(new ResourceInclude(uri));
    }

    /// <summary>
    /// Toggle between dark and light themes.
    /// </summary>
    public void ToggleTheme()
    {
        SetTheme(!_isDarkMode);
    }

    /// <summary>
    /// Get current theme mode.
    /// </summary>
    public bool IsDarkMode => _isDarkMode;

    public static T GetService<T>() where T : class
    {
        if (Current is App app && app._host != null)
        {
            return app._host.Services.GetRequiredService<T>();
        }
        throw new InvalidOperationException("Application host not initialized");
    }
}

/// <summary>
/// Service interface for theme management.
/// </summary>
public interface IThemeService
{
    bool IsDarkMode { get; }
    void SetTheme(bool dark);
    void ToggleTheme();
    event EventHandler<bool>? ThemeChanged;
}

/// <summary>
/// Theme service implementation for centralized theme management.
/// </summary>
public class ThemeService : IThemeService
{
    private readonly App _app;
    public event EventHandler<bool>? ThemeChanged;

    public ThemeService(App app)
    {
        _app = app;
    }

    public bool IsDarkMode => _app.IsDarkMode;

    public void SetTheme(bool dark)
    {
        if (_app.IsDarkMode != dark)
        {
            _app.SetTheme(dark);
            ThemeChanged?.Invoke(this, dark);
        }
    }

    public void ToggleTheme()
    {
        var newTheme = !_app.IsDarkMode;
        _app.SetTheme(newTheme);
        ThemeChanged?.Invoke(this, newTheme);
    }
}
