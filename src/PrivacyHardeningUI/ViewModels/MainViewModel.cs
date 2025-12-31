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
    private int _progressValue;

    [ObservableProperty]
    private bool _progressVisible;

    private System.Threading.CancellationTokenSource? _applyCts;
    [ObservableProperty]
    private bool _showConfirmDialog;

    [ObservableProperty]
    private string? _confirmDialogSummary;

    [ObservableProperty]
    private string? _snackbarMessage;

    [ObservableProperty]
    private bool _snackbarVisible;

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

        // Subscribe to service progress updates
        _serviceClient.ProgressReceived += (percent, message) =>
        {
            ProgressValue = percent;
            if (!string.IsNullOrEmpty(message)) StatusMessage = message;
        };
    }

    [RelayCommand]
    private async Task ApplySelectedPoliciesAsync()
    {
        // Show confirmation first
        var selectedPolicies = PolicySelection.GetSelectedPolicies().ToArray();
        if (selectedPolicies.Length == 0)
        {
            StatusMessage = "No policies selected";
            return;
        }

        ConfirmDialogSummary = GeneratePolicySummary(selectedPolicies);
        ShowConfirmDialog = true;
        // ApplyConfirmedAsync will handle proceeding
    }


    private string GeneratePolicySummary(PolicyItemViewModel[] selected)
    {
        // Build a concise summary with counts and top-level info
        var lines = new System.Collections.Generic.List<string>();
        lines.Add($"Selected policies: {selected.Length}");
        var byCategory = selected.GroupBy(p => p.Category).Select(g => $"{g.Key}: {g.Count()}");
        lines.AddRange(byCategory);
        lines.Add("\nPolicies:\n" + string.Join('\n', selected.Take(20).Select(p => $"- {p.PolicyId}: {p.Name}")));
        if (selected.Length > 20) lines.Add($"...and {selected.Length - 20} more policies");
        return string.Join('\n', lines);
    }

    [RelayCommand]
    private async Task ApplyConfirmedAsync()
    {
        ShowConfirmDialog = false;
        // proceed with apply
        IsProcessing = true;
        ProgressVisible = true;
        ProgressValue = 0;
        StatusMessage = "Applying selected policies...";
        _applyCts = new System.Threading.CancellationTokenSource();

        try
        {
            var selectedPolicies = PolicySelection.GetSelectedPolicies().ToArray();
            var policyIds = selectedPolicies.Select(p => p.PolicyId).ToArray();

            var applyTask = Task.Run(() => _serviceClient.ApplyAsync(policyIds, createRestorePoint: true, dryRun: false));
            while (!applyTask.IsCompleted)
            {
                if (_applyCts?.IsCancellationRequested == true)
                {
                    StatusMessage = "Apply cancelled by user.";
                    ShowSnackbar("Apply cancelled");
                    return;
                }
                ProgressValue = (ProgressValue + 7) % 95;
                await Task.Delay(300);
            }
            var result = await applyTask;
            ProgressValue = 100;
            if (result.Success)
            {
                StatusMessage = $"Applied {result.AppliedPolicies.Length} policies successfully";
                ShowSnackbar("Applied policies successfully");
            }
            else
            {
                StatusMessage = $"Applied {result.AppliedPolicies.Length}, failed {result.FailedPolicies.Length}";
                ShowSnackbar("Some policies failed to apply");
            }
        }
        catch (UnauthorizedAccessException)
        {
            // keep existing elevated helper flow
            StatusMessage = "Requesting elevation to apply policies...";
            ShowSnackbar("Requesting elevation...");
            try
            {
                var helperExe = Path.Combine(AppContext.BaseDirectory, "PrivacyHardeningElevated.exe");
                if (!File.Exists(helperExe))
                {
                    StatusMessage = "Elevated helper not found. Please run the UI as Administrator or install the helper.";
                    ShowSnackbar("Elevated helper not found");
                    return;
                }

                var policyIds = PolicySelection.GetSelectedPolicies().Select(p => p.PolicyId).ToArray();
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
                    ShowSnackbar("Elevation failed");
                    return;
                }
                await Task.Run(() => proc.WaitForExit());
                StatusMessage = proc.ExitCode == 0 ? "Elevated apply completed successfully" : $"Elevated helper failed (exit {proc.ExitCode})";
                ShowSnackbar(StatusMessage);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                StatusMessage = "Elevation cancelled by user.";
                ShowSnackbar("Elevation cancelled");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            ShowSnackbar("Error during apply");
        }
        finally
        {
            IsProcessing = false;
            ProgressVisible = false;
            ProgressValue = 0;
            _applyCts?.Dispose();
            _applyCts = null;
        }
    }

    [RelayCommand]
    private void CancelApplyConfirmation()
    {
        ShowConfirmDialog = false;
        ShowSnackbar("Apply cancelled");
    }

    private async void ShowSnackbar(string message, int ms = 3500)
    {
        SnackbarMessage = message;
        SnackbarVisible = true;
        try { await Task.Delay(ms); } catch { }
        SnackbarVisible = false;
    }

    [RelayCommand]
    private void DismissSnackbar()
    {
        SnackbarVisible = false;
    }


    [RelayCommand(CanExecute = nameof(CanCancelApply))]
    private void CancelApply()
    {
        if (_applyCts != null && !_applyCts.IsCancellationRequested)
        {
            _applyCts.Cancel();
        }
    }

    private bool CanCancelApply()
    {
        return IsProcessing;
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
