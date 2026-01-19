using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrivacyHardeningUI.Services;
using PrivacyHardeningContracts.Models;
using Avalonia;

namespace PrivacyHardeningUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly IThemeService _themeService;
    private readonly ServiceClient _serviceClient;

    [ObservableProperty]
    private bool isDarkTheme;

    [ObservableProperty]
    private double fontSize = 14.0;

    [ObservableProperty]
    private bool autoAuditOnStart;

    [ObservableProperty]
    private bool redactReports;

    [ObservableProperty]
    private bool enableEvidenceLogging;

    [ObservableProperty]
    private string customPolicyPath = string.Empty;

    // Service Settings
    [ObservableProperty] private bool enableDriftMonitor;
    [ObservableProperty] private int driftCheckInterval = 60;
    [ObservableProperty] private bool autoRemediate;
    [ObservableProperty] private bool isServiceAvailable;

    public SettingsViewModel(SettingsService settingsService, IThemeService themeService, ServiceClient serviceClient)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        _serviceClient = serviceClient;

        var s = _settingsService.Load();
        IsDarkTheme = s.IsDarkMode;
        FontSize = s.FontSize > 0 ? s.FontSize : 14.0;
        AutoAuditOnStart = s.AutoAuditOnStart;
        RedactReports = s.RedactReports;
        EnableEvidenceLogging = s.EnableEvidenceLogging;
        CustomPolicyPath = s.CustomPolicyPath;

        _ = LoadServiceConfigAsync();
    }

    private async Task LoadServiceConfigAsync()
    {
        try
        {
            var result = await _serviceClient.GetServiceConfigAsync();
            if (result.Success)
            {
                var cfg = result.Configuration;
                EnableDriftMonitor = cfg.DriftCheckIntervalMinutes > 0;
                DriftCheckInterval = cfg.DriftCheckIntervalMinutes > 0 ? cfg.DriftCheckIntervalMinutes : 60;
                AutoRemediate = cfg.AutoRemediateDrift;
                IsServiceAvailable = true;
            }
            else
            {
                IsServiceAvailable = false;
            }
        }
        catch
        {
            IsServiceAvailable = false;
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        // 1. Save Local Settings
        var s = new SettingsService.SettingsModel
        {
            IsDarkMode = IsDarkTheme,
            FontSize = FontSize,
            AutoAuditOnStart = AutoAuditOnStart,
            RedactReports = RedactReports,
            EnableEvidenceLogging = EnableEvidenceLogging,
            CustomPolicyPath = CustomPolicyPath
        };

        _settingsService.Save(s);

        // Apply theme immediately
        _themeService.SetTheme(IsDarkTheme);

        // Try to set a runtime resource for font size (some controls may respect this)
        try
        {
            var res = Application.Current!.Resources;
            res["AppFontSize"] = FontSize;
            // update derived tokens so StaticResource users get updated values
            res["FontSizeBody"] = FontSize;
            res["FontSizeBodyLarge"] = FontSize + 2;
            res["FontSizeSubtitle"] = FontSize + 6;
            res["FontSizeTitle"] = FontSize + 10;
        }
        catch
        {
            // ignore failures
        }

        // 2. Save Service Settings
        if (IsServiceAvailable)
        {
             var config = new ServiceConfiguration
             {
                 DriftCheckIntervalMinutes = EnableDriftMonitor ? DriftCheckInterval : 0,
                 AutoRemediateDrift = AutoRemediate
             };
             await _serviceClient.UpdateServiceConfigAsync(config);
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var dlg = new Avalonia.Controls.OpenFileDialog();
        var res = await dlg.ShowAsync(Avalonia.Application.Current.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null);
        if (res != null && res.Length > 0 && File.Exists(res[0]))
        {
            _settingsService.Import(res[0]);
            var s = _settingsService.Load();
            IsDarkTheme = s.IsDarkMode;
            FontSize = s.FontSize;
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var dlg = new Avalonia.Controls.SaveFileDialog();
        var path = await dlg.ShowAsync(Avalonia.Application.Current.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null);
        if (!string.IsNullOrWhiteSpace(path))
        {
            _settingsService.Export(path);
        }
    }

    [RelayCommand]
    private async Task PickPolicyPathAsync()
    {
        var dlg = new Avalonia.Controls.OpenFolderDialog();
        var path = await dlg.ShowAsync(Avalonia.Application.Current.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null);
        if (!string.IsNullOrWhiteSpace(path))
        {
            CustomPolicyPath = path;
        }
    }
}
