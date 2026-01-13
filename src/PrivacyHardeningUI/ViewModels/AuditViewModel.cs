using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrivacyHardeningContracts.Models;
using PrivacyHardeningContracts.Responses;
using PrivacyHardeningUI.Services;

namespace PrivacyHardeningUI.ViewModels;

public partial class AuditViewModel : ObservableObject
{
    private readonly ServiceClient _serviceClient;
    private readonly PolicySelectionViewModel _selection;
    private readonly NavigationService _navigation;
    private readonly StatusRailViewModel _statusRail;
    private readonly SettingsService _settingsService;

    private readonly Dictionary<string, PolicyDefinition> _policyIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _policyIndexLock = new();
    private bool _policyIndexLoaded;

    private System.Threading.CancellationTokenSource? _evidenceCts;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _infoMessage;

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private bool _showCompliant = true;

    [ObservableProperty]
    private bool _showNonCompliant = true;

    [ObservableProperty]
    private bool _showNotApplicable;

    [ObservableProperty]
    private bool _showUnsupported = true;

    [ObservableProperty]
    private bool _showOnlyReversible;

    [ObservableProperty]
    private bool _showOnlyApplied;

    [ObservableProperty]
    private string? _groupBy = "Category";

    [ObservableProperty]
    private AuditPolicyRowViewModel? _selectedItem;

    [ObservableProperty]
    private bool _isEvidenceLoading;

    [ObservableProperty]
    private string? _evidenceError;

    [ObservableProperty]
    private AuditResult? _lastAuditResult;

    public ObservableCollection<AuditPolicyRowViewModel> Items { get; } = new();
    public ObservableCollection<AuditPolicyRowViewModel> FilteredItems { get; } = new();

    public ObservableCollection<ChangeRecord> RelatedChanges { get; } = new();

    public AuditViewModel(ServiceClient serviceClient, PolicySelectionViewModel selection, NavigationService navigation, StatusRailViewModel statusRail, SettingsService settingsService)
    {
        _serviceClient = serviceClient;
        _selection = selection;
        _navigation = navigation;
        _statusRail = statusRail;
        _settingsService = settingsService;

        // Load policies into list immediately (as pending/unknown state)
        _ = Task.Run(async () =>
        {
            await EnsurePolicyIndexAsync();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => PopulateInitialState());
        });

        // Keep Audit UI responsive to service availability changes.
        _serviceClient.StandaloneModeChanged += (_, isStandalone) =>
        {
            if (isStandalone)
            {
                // Avoid showing a scary red error for the expected standalone path.
                ErrorMessage = null;
                InfoMessage ??= "Standalone (read-only): start the service to run audits, previews, applies, and history.";
            }

            OnPropertyChanged(nameof(IsStandalone));
            OnPropertyChanged(nameof(CanRunAudit));
            OnPropertyChanged(nameof(CanUsePrivilegedActions));
        };

        // If configured, start an audit as soon as we're initialized/connected
        if (_settingsService.Load().AutoAuditOnStart && !IsStandalone)
        {
            _ = Task.Run(async () =>
            {
                // Give it a tiny moment to stabilize
                await Task.Delay(500);
                if (CanRunAudit)
                {
                    await RunAuditAsync();
                }
            });
        }
    }

    public int TotalCount => Items.Count;
    public int CompliantCount => Items.Count(i => i.ComplianceStatus == AuditComplianceStatus.Compliant);
    public int NonCompliantCount => Items.Count(i => i.ComplianceStatus == AuditComplianceStatus.NonCompliant);
    public int NotApplicableCount => Items.Count(i => i.ComplianceStatus == AuditComplianceStatus.NotApplicable);

    public bool IsStandalone => _serviceClient.IsStandaloneMode;
    public bool CanRunAudit => !IsRunning && !IsStandalone;
    public bool CanUsePrivilegedActions => !IsRunning && !IsStandalone;

    /// <summary>
    /// True when the red error panel should be visible. In standalone mode we show an
    /// informational read-only banner instead of a scary error.
    /// </summary>
    public bool ShowAuditError => !IsStandalone && !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(ShowAuditError));
    }

    partial void OnInfoMessageChanged(string? value)
    {
        // Keeps view state consistent if we later decide to key visibility off InfoMessage.
        OnPropertyChanged(nameof(ShowAuditError));
    }

    partial void OnSearchTextChanged(string? value) => RefreshFilter();
    partial void OnShowCompliantChanged(bool value) => RefreshFilter();
    partial void OnShowNonCompliantChanged(bool value) => RefreshFilter();
    partial void OnShowNotApplicableChanged(bool value) => RefreshFilter();
    partial void OnShowUnsupportedChanged(bool value) => RefreshFilter();
    partial void OnShowOnlyReversibleChanged(bool value) => RefreshFilter();
    partial void OnShowOnlyAppliedChanged(bool value) => RefreshFilter();
    partial void OnGroupByChanged(string? value) => RefreshFilter();

    partial void OnSelectedItemChanged(AuditPolicyRowViewModel? value)
    {
        _ = LoadEvidenceForSelectedAsync(value);
    }

    public string[] GroupByOptions { get; } = new[] { "Category", "Risk", "Mechanism", "Name" };

    private void PopulateInitialState()
    {
        if (Items.Count > 0) return;

        lock (_policyIndexLock)
        {
            foreach (var policy in _policyIndex.Values)
            {
                var item = new PolicyAuditItem
                {
                    PolicyId = policy.PolicyId,
                    PolicyName = policy.Name,
                    Category = policy.Category,
                    RiskLevel = policy.RiskLevel,
                    SupportStatus = policy.SupportStatus,
                    IsApplied = false,
                    IsApplicable = true,
                    Matches = false,
                    CurrentValue = "Unknown",
                    ExpectedValue = "Audit Required",
                    EvidenceDetails = "Audit has not been run for this session."
                };
                Items.Add(new AuditPolicyRowViewModel(item, policy));
            }
        }
        RefreshFilter();
    }

    private async Task EnsurePolicyIndexAsync()
    {
        if (_policyIndexLoaded)
            return;

        try
        {
            var policies = await _serviceClient.GetPoliciesAsync(onlyApplicable: false);
            lock (_policyIndexLock)
            {
                _policyIndex.Clear();
                foreach (var p in policies.Policies)
                {
                    _policyIndex[p.PolicyId] = p;
                }
                _policyIndexLoaded = true;
            }
        }
        catch
        {
            // Non-fatal: Audit can still run; rows just won't show mechanism/tags.
        }
    }

    [RelayCommand]
    public async Task RunAuditAsync()
    {
        if (_serviceClient.IsStandaloneMode)
        {
            ErrorMessage = null;
            InfoMessage = "Standalone (read-only): start the service to run audits.";
            OnPropertyChanged(nameof(IsStandalone));
            OnPropertyChanged(nameof(CanRunAudit));
            OnPropertyChanged(nameof(CanUsePrivilegedActions));
            return;
        }

        IsRunning = true;
        ErrorMessage = null;
        InfoMessage = null;

        OnPropertyChanged(nameof(IsStandalone));
        OnPropertyChanged(nameof(CanRunAudit));

        SelectedItem = null;

        try
        {
            await EnsurePolicyIndexAsync();
            var result = await _serviceClient.AuditAsync();

            if (!result.Success)
            {
                // Standalone/read-only: treat as informational, not an error.
                if (_serviceClient.IsStandaloneMode)
                {
                    ErrorMessage = null;
                    InfoMessage = "Audit requires the service. Standalone mode is read-only (you can browse policies, but audit/apply need the service).";
                }
                else
                {
                    ErrorMessage = result.Errors?.FirstOrDefault()?.Message ?? "Audit is unavailable without the service.";
                }

                Items.Clear();
                FilteredItems.Clear();
                return;
            }

            LastAuditResult = result;

            Items.Clear();
            foreach (var item in result.Items)
            {
                PolicyDefinition? def = null;
                lock (_policyIndexLock)
                {
                    _policyIndex.TryGetValue(item.PolicyId, out def);
                }
                Items.Add(new AuditPolicyRowViewModel(item, def));
            }

            RefreshFilter();

            // Calculate health score and update status rail (Unified Status & Health)
            int total = Items.Count(i => i.IsApplicable);
            int compliant = Items.Count(i => i.IsApplicable && i.Matches);
            int score = total > 0 ? (int)((double)compliant / total * 100) : 100;

            _statusRail.UpdateMetrics(score, total, compliant);

            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(CompliantCount));
            OnPropertyChanged(nameof(NonCompliantCount));
            OnPropertyChanged(nameof(NotApplicableCount));
        }
        catch (UnauthorizedAccessException)
        {
            ErrorMessage = "Audit requires the service/elevated helper. Please ensure the service is running.";
        }
        catch (ServiceUnavailableException)
        {
            // Prefer a calm read-only message when the named pipe/service isn't reachable.
            ErrorMessage = null;
            InfoMessage = "Service is not running. Standalone mode is read-only; start the service to run audits.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Audit failed: {ex.Message}";
        }
        finally
        {
            IsRunning = false;

            OnPropertyChanged(nameof(IsStandalone));
            OnPropertyChanged(nameof(CanRunAudit));
            OnPropertyChanged(nameof(ShowAuditError));
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = null;
        ShowCompliant = true;
        ShowNonCompliant = true;
        ShowNotApplicable = false;
        ShowUnsupported = true;
        ShowOnlyReversible = false;
        ShowOnlyApplied = false;
        GroupBy = "Category";
        RefreshFilter();
    }

    private void RefreshFilter()
    {
        FilteredItems.Clear();

        IEnumerable<AuditPolicyRowViewModel> rows = Items;

        // Sort/group (sorting only in sprint 1; headers can come later)
        rows = (GroupBy ?? "Category") switch
        {
            "Risk" => rows.OrderByDescending(r => r.RiskLevel).ThenBy(r => r.Category).ThenBy(r => r.PolicyName),
            "Mechanism" => rows.OrderBy(r => r.MechanismText).ThenByDescending(r => r.RiskLevel).ThenBy(r => r.PolicyName),
            "Name" => rows.OrderBy(r => r.PolicyName),
            _ => rows.OrderBy(r => r.Category).ThenByDescending(r => r.RiskLevel).ThenBy(r => r.PolicyName)
        };

        foreach (var row in rows)
        {
            if (!PassesStatusFilter(row))
                continue;

            if (ShowOnlyReversible && !row.IsReversible)
                continue;

            if (ShowOnlyApplied && !row.IsApplied)
                continue;

            if (!PassesSearch(row))
                continue;

            FilteredItems.Add(row);
        }

        // Preserve selection when possible
        if (SelectedItem != null && !FilteredItems.Contains(SelectedItem))
            SelectedItem = null;
    }

    private bool PassesStatusFilter(AuditPolicyRowViewModel row)
    {
        if (!ShowUnsupported && row.SupportStatus != SupportStatus.Supported)
            return false;

        return row.ComplianceStatus switch
        {
            AuditComplianceStatus.Compliant => ShowCompliant,
            AuditComplianceStatus.NonCompliant => ShowNonCompliant,
            AuditComplianceStatus.NotApplicable => ShowNotApplicable,
            _ => true
        };
    }

    private bool PassesSearch(AuditPolicyRowViewModel row)
    {
        var q = SearchText;
        if (string.IsNullOrWhiteSpace(q))
            return true;

        q = q.Trim();

        return row.PolicyId.Contains(q, StringComparison.OrdinalIgnoreCase)
               || row.PolicyName.Contains(q, StringComparison.OrdinalIgnoreCase)
               || row.Category.ToString().Contains(q, StringComparison.OrdinalIgnoreCase)
               || row.RiskText.Contains(q, StringComparison.OrdinalIgnoreCase)
               || row.MechanismText.Contains(q, StringComparison.OrdinalIgnoreCase)
               || row.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase))
               || (row.SummaryLine?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private async Task LoadEvidenceForSelectedAsync(AuditPolicyRowViewModel? row)
    {
        RelatedChanges.Clear();
        EvidenceError = null;

        _evidenceCts?.Cancel();
        _evidenceCts?.Dispose();
        _evidenceCts = new System.Threading.CancellationTokenSource();
        var ct = _evidenceCts.Token;

        if (row == null)
            return;

        // Evidence timeline relies on service state/history.
        if (_serviceClient.IsStandaloneMode)
        {
            EvidenceError = "Timeline evidence requires the service (standalone mode only has policy definitions).";
            return;
        }

        IsEvidenceLoading = true;
        try
        {
            var state = await _serviceClient.GetStateAsync(includeHistory: true);
            if (ct.IsCancellationRequested)
                return;

            var related = state.CurrentState.ChangeHistory
                .Where(c => string.Equals(c.PolicyId, row.PolicyId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(c => c.AppliedAt)
                .Take(200);

            foreach (var c in related)
                RelatedChanges.Add(c);
        }
        catch (Exception ex)
        {
            EvidenceError = ex.Message;
        }
        finally
        {
            IsEvidenceLoading = false;
        }
    }

    [RelayCommand]
    private void PreviewSelected()
    {
        if (SelectedItem == null)
            return;

        if (_serviceClient.IsStandaloneMode)
        {
            ErrorMessage = null;
            InfoMessage = "Standalone (read-only): start the service to generate a preview.";
            return;
        }

        _selection.SelectOnly(SelectedItem.PolicyId);
        _navigation.Navigate(AppPage.Preview);
    }

    [RelayCommand]
    private void ApplySelected()
    {
        if (SelectedItem == null)
            return;

        if (_serviceClient.IsStandaloneMode)
        {
            ErrorMessage = null;
            InfoMessage = "Standalone (read-only): start the service to apply policies.";
            return;
        }

        _selection.SelectOnly(SelectedItem.PolicyId);
        _navigation.Navigate(AppPage.Apply);
    }

    [RelayCommand]
    private async Task RevertSelectedAsync()
    {
        if (SelectedItem == null)
            return;

        if (_serviceClient.IsStandaloneMode)
        {
            ErrorMessage = null;
            InfoMessage = "Standalone (read-only): start the service to revert policies.";
            return;
        }

        try
        {
            await _serviceClient.RevertAsync(new[] { SelectedItem.PolicyId });
        }
        catch
        {
            // surfaced via snackbar elsewhere; keep Audit stable
        }
    }
}
