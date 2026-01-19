using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using System.IO;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrivacyHardeningUI.Services;
using PrivacyHardeningUI.Views;
using System.Text.Json;
using System.Collections.Generic;
using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningUI.ViewModels;

/// <summary>
/// Main window ViewModel
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ServiceClient _serviceClient;
    private readonly IThemeService _themeService;
    private readonly PrivacyHardeningUI.Services.NavigationService _navigation;

    public ObservableCollection<NavItemViewModel> NavItems { get; } = new();

    [ObservableProperty]
    private NavItemViewModel? _selectedNavItem;

    public object? CurrentPage => SelectedNavItem?.ViewModel;

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

    public StatusRailViewModel StatusRail { get; }

    public DashboardViewModel Dashboard { get; }
    public PolicySelectionViewModel PolicySelection { get; }
    public AuditViewModel Audit { get; }
    public DiffViewModel Diff { get; }

    public PreviewViewModel Preview { get; }
    public ApplyViewModel Apply { get; }
    public HistoryViewModel History { get; }
    public DriftViewModel Drift { get; }
    public ReportsViewModel Reports { get; }
    public AdvisorViewModel Advisor { get; }

    public MainViewModel(ServiceClient serviceClient,
        IThemeService themeService,
        PrivacyHardeningUI.Services.NavigationService navigation,
        DashboardViewModel dashboard,
        StatusRailViewModel statusRail,
        PolicySelectionViewModel policySelection,
        ApplyViewModel apply,
        AuditViewModel audit,
        PreviewViewModel preview,
        HistoryViewModel history,
        DriftViewModel drift,
        ReportsViewModel reports,
        DiffViewModel diff,
        AdvisorViewModel advisor)
    {
        _serviceClient = serviceClient;
        _themeService = themeService;
        _navigation = navigation;

        Dashboard = dashboard;
        StatusRail = statusRail;
        PolicySelection = policySelection;
        Apply = apply;
        Audit = audit;
        Preview = preview;
        History = history;
        Drift = drift;
        Reports = reports;
        Diff = diff;
        Advisor = advisor;

        // Initialize theme state from application
        IsDarkTheme = _themeService.IsDarkMode;

        _themeService.ThemeChanged += (_, dark) =>
        {
            IsDarkTheme = dark;
        };

        // Build task-based navigation
        NavItems.Add(new NavItemViewModel(AppPage.Dashboard, "Dashboard", "home", Dashboard));
        NavItems.Add(new NavItemViewModel(AppPage.Advisor, "Advisor", "lightbulb", Advisor));
        NavItems.Add(new NavItemViewModel(AppPage.Audit, "Audit", "search", Audit));
        NavItems.Add(new NavItemViewModel(AppPage.Preview, "Preview", "diff", Preview));
        NavItems.Add(new NavItemViewModel(AppPage.Apply, "Apply", "shield", Apply));
        NavItems.Add(new NavItemViewModel(AppPage.History, "History", "history", History));
        NavItems.Add(new NavItemViewModel(AppPage.Drift, "Drift", "warning", Drift));
        NavItems.Add(new NavItemViewModel(AppPage.Reports, "Reports", "report", Reports));

        SelectedNavItem = NavItems.FirstOrDefault(i => i.Page == AppPage.Dashboard) ?? NavItems.FirstOrDefault();

        // Allow other viewmodels to request navigation (workflow jumps).
        _navigation.NavigateRequested += page => Navigate(page);

        // Subscribe to service progress updates
        _serviceClient.ProgressReceived += (percent, message) =>
        {
            ProgressValue = percent;
            if (!string.IsNullOrEmpty(message)) StatusMessage = message;
        };

        StatusMessage = "Initializing system state...";

        // Kick status rail refresh (non-blocking)
        _ = StatusRail.RefreshAsync();
    }

    partial void OnSelectedNavItemChanged(NavItemViewModel? value)
    {
        OnPropertyChanged(nameof(CurrentPage));
    }

    [RelayCommand]
    private void Navigate(AppPage page)
    {
        var item = NavItems.FirstOrDefault(i => i.Page == page);
        if (item != null)
        {
            SelectedNavItem = item;
        }
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

            // Build configuration overrides from user selections
            var overrides = new Dictionary<string, string>();
            foreach (var p in selectedPolicies)
            {
                if (p.SelectedBehavior != "Default")
                {
                    object? config = null;
                    if (p.Mechanism == MechanismType.ScheduledTask)
                    {
                        if (Enum.TryParse<TaskAction>(p.SelectedBehavior, true, out var action))
                        {
                            config = new { Action = action };
                        }
                    }
                    
                    if (config != null)
                    {
                        overrides[p.PolicyId] = JsonSerializer.Serialize(config);
                    }
                }
            }

            var applyTask = Task.Run(() => _serviceClient.ApplyAsync(policyIds, overrides, createRestorePoint: true, dryRun: false));
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
        Navigate(AppPage.Audit);
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        _themeService.ToggleTheme();
        IsDarkTheme = _themeService.IsDarkMode;
    }

    [RelayCommand]
    private void OpenSettings()
    {
        try
        {
            var window = App.GetService<SettingsWindow>();
            var desktop = Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var owner = desktop?.MainWindow;
            if (owner != null)
            {
                // Show as dialog with owner
                _ = window.ShowDialog(owner);
            }
            else
            {
                window.Show();
            }
        }
        catch
        {
            // ignore failures to open settings
        }
    }
}
