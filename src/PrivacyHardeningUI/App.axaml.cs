using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PrivacyHardeningUI.Services;
using PrivacyHardeningUI.ViewModels;
using PrivacyHardeningUI.Views;

namespace PrivacyHardeningUI;

public partial class App : Application
{
    private IHost? _host;

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

                // Register ViewModels
                services.AddTransient<MainViewModel>();
                services.AddTransient<PolicySelectionViewModel>();
                services.AddTransient<AuditViewModel>();
                services.AddTransient<DiffViewModel>();

                // Register Views
                services.AddTransient<MainWindow>();
            })
            .Build();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _host.Services.GetRequiredService<MainWindow>();
        }

        // Ensure default theme resources are loaded (Light by default)
        SetTheme(false);

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Switch between light and dark theme resource dictionaries.
    /// </summary>
    /// <param name="dark">True to use dark theme, false for light.</param>
    public void SetTheme(bool dark)
    {
        if (Resources?.MergedDictionaries == null) return;

        // Remove existing theme resources if present
        var existing = Resources.MergedDictionaries
            .OfType<ResourceInclude>()
            .FirstOrDefault(r => r.Source != null && r.Source.OriginalString.Contains("ThemeResources"));

        if (existing != null)
        {
            Resources.MergedDictionaries.Remove(existing);
        }

        var fileName = dark ? "ThemeResources.Dark.axaml" : "ThemeResources.Light.axaml";
        var uri = new Uri($"avares://PrivacyHardeningUI/Styles/{fileName}");

        Resources.MergedDictionaries.Add(new ResourceInclude(uri));
    }

    public static T GetService<T>() where T : class
    {
        if (Current is App app && app._host != null)
        {
            return app._host.Services.GetRequiredService<T>();
        }
        throw new InvalidOperationException("Application host not initialized");
    }
}
