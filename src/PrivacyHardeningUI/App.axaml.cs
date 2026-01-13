using System;
using System.IO;
using System.Text.Json;
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

                // Register settings related services
                services.AddSingleton<SettingsService>();

                // UI-only navigation coordinator
                services.AddSingleton<NavigationService>();

                // Register theme service
                services.AddSingleton<IThemeService>(sp => new ThemeService(this, sp.GetRequiredService<SettingsService>()));

                // Register accessibility service (WCAG 2.1 Level AA)
                services.AddSingleton<IAccessibilityService, AccessibilityService>();

                // Register telemetry monitoring service
                services.AddSingleton<ITelemetryMonitorService, TelemetryMonitorService>();

                // Register ViewModels
                // Keep page viewmodels as singletons so selection/audit/preview state is consistent across navigation.
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<StatusRailViewModel>();

                services.AddSingleton<PolicySelectionViewModel>();
                services.AddSingleton<ApplyViewModel>();

                services.AddSingleton<AuditViewModel>();
                services.AddSingleton<PreviewViewModel>();
                services.AddSingleton<HistoryViewModel>();
                services.AddSingleton<DriftViewModel>();
                services.AddSingleton<ReportsViewModel>();

                services.AddSingleton<DiffViewModel>();

                services.AddSingleton<MainViewModel>();
                services.AddSingleton<TelemetryMonitorViewModel>();

                // Register settings VM
                services.AddTransient<SettingsViewModel>();

                // Register Views
                services.AddTransient<SettingsWindow>();
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
        // Prefer saved user preference if present, otherwise detect system theme
        var saved = LoadSavedThemePreference();
        if (saved.HasValue)
        {
            SetTheme(saved.Value);
        }
        else
        {
            var systemDarkMode = DetectSystemDarkMode();
            SetTheme(systemDarkMode);
        }
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

        // NOTE: Swapping theme resource dictionaries at runtime can cause Avalonia to
        // evaluate control styles before the theme resources are available, leading
        // to exceptions. To avoid startup/resource-ordering issues we only update
        // the RequestedThemeVariant and persist the user's preference here. A
        // safer runtime theme-swap implementation will be added that replaces the
        // specific resource dictionary inside the consolidated Theme.axaml.

        try
        {
            SaveThemePreference(dark);
        }
        catch
        {
            // ignore persistence failures
        }
    }

    private string GetSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "PrivacyHardeningUI");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    private void SaveThemePreference(bool dark)
    {
        var path = GetSettingsPath();
        var doc = new { isDarkMode = dark };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(doc, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllBytes(path, bytes);
    }

    private bool? LoadSavedThemePreference()
    {
        try
        {
            var path = GetSettingsPath();
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            if (doc.TryGetProperty("isDarkMode", out var prop) && prop.ValueKind == JsonValueKind.True)
                return true;
            if (doc.TryGetProperty("isDarkMode", out prop) && prop.ValueKind == JsonValueKind.False)
                return false;
        }
        catch
        {
            // ignore and treat as no preference
        }
        return null;
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
    private readonly SettingsService _settings;
    public event EventHandler<bool>? ThemeChanged;

    public ThemeService(App app, SettingsService settings)
    {
        _app = app;
        _settings = settings;
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
