using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrivacyHardeningUI.Services;
using Avalonia;

namespace PrivacyHardeningUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly IThemeService _themeService;

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

    public SettingsViewModel(SettingsService settingsService, IThemeService themeService)
    {
        _settingsService = settingsService;
        _themeService = themeService;

        var s = _settingsService.Load();
        IsDarkTheme = s.IsDarkMode;
        FontSize = s.FontSize > 0 ? s.FontSize : 14.0;
        AutoAuditOnStart = s.AutoAuditOnStart;
        RedactReports = s.RedactReports;
        EnableEvidenceLogging = s.EnableEvidenceLogging;
        CustomPolicyPath = s.CustomPolicyPath;
    }

    [RelayCommand]
    private void Save()
    {
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
