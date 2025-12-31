using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using System.IO;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrivacyHardeningUI.Services;

namespace PrivacyHardeningUI.ViewModels;

/// <summary>
/// Main window ViewModel
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ServiceClient _serviceClient;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isDarkTheme;

    public PolicySelectionViewModel PolicySelection { get; }
    public AuditViewModel Audit { get; }
    public DiffViewModel Diff { get; }

    public MainViewModel(ServiceClient serviceClient,
        PolicySelectionViewModel policySelection,
        AuditViewModel audit,
        DiffViewModel diff)
    {
        _serviceClient = serviceClient;
        PolicySelection = policySelection;
        Audit = audit;
        Diff = diff;

        // Initialize theme state from application
        var app = Application.Current;
        IsDarkTheme = app?.RequestedThemeVariant == ThemeVariant.Dark;
    }

    [RelayCommand]
    private async Task ApplySelectedPoliciesAsync()
    {
        IsProcessing = true;
        StatusMessage = "Applying selected policies...";

        try
        {
            var selectedPolicies = PolicySelection.GetSelectedPolicies().ToArray();
            if (selectedPolicies.Length == 0)
            {
                StatusMessage = "No policies selected";
                return;
            }

            var policyIds = selectedPolicies.Select(p => p.PolicyId).ToArray();

            try
            {
                var result = await _serviceClient.ApplyAsync(policyIds, createRestorePoint: true, dryRun: false);

                if (result.Success)
                {
                    StatusMessage = $"Applied {result.AppliedPolicies.Length} policies successfully";
                }
                else
                {
                    StatusMessage = $"Applied {result.AppliedPolicies.Length}, failed {result.FailedPolicies.Length}";
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Service rejected due to lack of privileges — launch elevated helper
                StatusMessage = "Requesting elevation to apply policies...";

                try
                {
                    var helperExe = Path.Combine(AppContext.BaseDirectory, "PrivacyHardeningElevated.exe");
                    if (!File.Exists(helperExe))
                    {
                        StatusMessage = "Elevated helper not found. Please run the UI as Administrator or install the helper.";
                        return;
                    }

                    // Build arguments: apply <id1> <id2> ...
                    var args = "apply " + string.Join(' ', policyIds.Select(p => p));

                    var psi = new ProcessStartInfo
                    {
                        FileName = helperExe,
                        Arguments = args,
                        UseShellExecute = true,
                        Verb = "runas",
                        WorkingDirectory = AppContext.BaseDirectory
                    };

                    using var proc = Process.Start(psi);
                    if (proc == null)
                    {
                        StatusMessage = "Failed to start elevated helper.";
                        return;
                    }

                    await Task.Run(() => proc.WaitForExit());

                    StatusMessage = proc.ExitCode == 0 ? "Elevated apply completed successfully" : $"Elevated helper failed (exit {proc.ExitCode})";
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    StatusMessage = "Elevation cancelled by user.";
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task RunAuditAsync()
    {
        await Audit.RunAuditAsync();
        SelectedTabIndex = 1; // Switch to Audit tab
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        var app = Application.Current;
        if (app == null) return;

        var current = app.RequestedThemeVariant;
        app.RequestedThemeVariant = current == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
        IsDarkTheme = app.RequestedThemeVariant == ThemeVariant.Dark;

        // Also swap resource dictionaries for custom brushes
        if (Application.Current is App a)
        {
            a.SetTheme(IsDarkTheme);
        }
    }
}
