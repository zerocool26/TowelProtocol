using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrivacyHardeningContracts.Models;
using PrivacyHardeningContracts.Responses;
using PrivacyHardeningUI.Services;

namespace PrivacyHardeningUI.ViewModels;

public sealed record MechanismFilterOption(string Label, MechanismType? Mechanism);

public enum PreviewIssueSeverity
{
    Info,
    Warning,
    Error
}

public enum PreviewSelectionIssueKind
{
    MissingDependencies,
    Conflicts
}

public sealed record PreviewSelectionIssueViewModel(
    PreviewIssueSeverity Severity,
    PreviewSelectionIssueKind Kind,
    string Title,
    string Details,
    string[] RelatedPolicies,
    string ActionLabel);

public sealed partial class PreviewViewModel : ObservableObject
{
    private readonly ServiceClient _serviceClient;
    private readonly PolicySelectionViewModel _selection;
    private readonly NavigationService _navigation;

    private IReadOnlyDictionary<string, PolicyDefinition>? _policyIndex;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private ApplyResult? _lastPreviewResult;

    public ObservableCollection<PreviewChangeRowViewModel> AllChanges { get; } = new();
    public ObservableCollection<PreviewChangeRowViewModel> Changes { get; } = new();

    [ObservableProperty]
    private PreviewChangeRowViewModel? _selectedChange;

    public ObservableCollection<PreviewSelectionIssueViewModel> SelectionIssues { get; } = new();

    public ObservableCollection<MechanismFilterOption> MechanismFilters { get; } = new();

    [ObservableProperty]
    private MechanismFilterOption? _selectedMechanismFilter;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _showOnlyFailures;

    [ObservableProperty]
    private DateTimeOffset? _lastGeneratedAt;

    [ObservableProperty]
    private bool _highRiskAcknowledged;

    public PreviewViewModel(ServiceClient serviceClient, PolicySelectionViewModel selection, NavigationService navigation)
    {
        _serviceClient = serviceClient;
        _selection = selection;
        _navigation = navigation;

        MechanismFilters.Add(new MechanismFilterOption("All mechanisms", null));
        foreach (var m in Enum.GetValues<MechanismType>())
        {
            MechanismFilters.Add(new MechanismFilterOption(m.ToString(), m));
        }
        SelectedMechanismFilter = MechanismFilters.FirstOrDefault();
    }

    public int SelectedCount => _selection.GetSelectedPolicies().Count();

    public bool HasHighRiskSelected => _selection.GetSelectedPolicies().Any(p => p.RiskLevel >= RiskLevel.High);

    public bool CanApplyAfterPreview => !HasHighRiskSelected || HighRiskAcknowledged;

    public bool HasSelectionIssues => SelectionIssues.Count > 0;

    public bool HasBlockingSelectionIssues => SelectionIssues.Any(i => i.Kind == PreviewSelectionIssueKind.Conflicts);

    public int ChangeCount => Changes.Count;

    public int FailureCount => Changes.Count(c => !c.Success);

    public bool HasPreview => LastPreviewResult != null;

    public bool HasPreviewFailures => FailureCount > 0;

    public bool CanProceedToApply => HasPreview && CanApplyAfterPreview && !HasBlockingSelectionIssues;

    public bool HasSelectedChange => SelectedChange != null;

    public bool HasNoSelectedChange => !HasSelectedChange;

    partial void OnSelectedChangeChanged(PreviewChangeRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedChange));
        OnPropertyChanged(nameof(HasNoSelectedChange));
    }

    partial void OnHighRiskAcknowledgedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanApplyAfterPreview));
        OnPropertyChanged(nameof(CanProceedToApply));
    }

    partial void OnSelectedMechanismFilterChanged(MechanismFilterOption? value) => ApplyFilters();

    partial void OnFilterTextChanged(string value) => ApplyFilters();

    partial void OnShowOnlyFailuresChanged(bool value) => ApplyFilters();

    [RelayCommand]
    public async Task GeneratePreviewAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        SelectionIssues.Clear();

        AllChanges.Clear();
        Changes.Clear();
        LastPreviewResult = null;
        HighRiskAcknowledged = false;
        LastGeneratedAt = null;
        SelectedChange = null;

        try
        {
            var ids = _selection.GetSelectedPolicies().Select(p => p.PolicyId).ToArray();
            if (ids.Length == 0)
            {
                ErrorMessage = "No policies selected.";
                return;
            }

            await EnsurePolicyIndexAsync();
            BuildSelectionIssues(ids);

            var result = await _serviceClient.ApplyAsync(ids, createRestorePoint: false, dryRun: true);
            LastPreviewResult = result;

            foreach (var c in result.Changes.OrderByDescending(c => c.AppliedAt))
            {
                var def = (_policyIndex != null && _policyIndex.TryGetValue(c.PolicyId, out var d)) ? d : null;

                AllChanges.Add(new PreviewChangeRowViewModel
                {
                    PolicyId = c.PolicyId,
                    PolicyName = def?.Name ?? c.PolicyId,
                    Operation = c.Operation,
                    Mechanism = c.Mechanism,
                    Success = c.Success,
                    ErrorMessage = c.ErrorMessage,
                    Description = c.Description,
                    BeforeState = c.PreviousState,
                    AfterState = c.NewState,
                    RiskLevel = def?.RiskLevel ?? RiskLevel.Medium,
                    SupportStatus = def?.SupportStatus ?? SupportStatus.Supported,
                    Reversible = def?.Reversible ?? false,
                    Notes = def?.Notes,
                    KnownBreakage = def?.KnownBreakage ?? Array.Empty<BreakageScenario>()
                });
            }

            ApplyFilters();
            SelectedChange = Changes.FirstOrDefault();
            LastGeneratedAt = DateTimeOffset.Now;

            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasHighRiskSelected));
            OnPropertyChanged(nameof(CanApplyAfterPreview));
            OnPropertyChanged(nameof(HasSelectionIssues));
            OnPropertyChanged(nameof(HasBlockingSelectionIssues));
            OnPropertyChanged(nameof(ChangeCount));
            OnPropertyChanged(nameof(FailureCount));
            OnPropertyChanged(nameof(HasPreview));
            OnPropertyChanged(nameof(HasPreviewFailures));
            OnPropertyChanged(nameof(CanProceedToApply));
        }
        catch (UnauthorizedAccessException)
        {
            ErrorMessage = "Preview requires the service/elevated helper to authorize changes. Please ensure the service is running.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private Task ProceedToApplyAsync()
    {
        if (!HasPreview)
        {
            ErrorMessage = "Generate a preview first.";
            return Task.CompletedTask;
        }

        if (!CanApplyAfterPreview)
        {
            ErrorMessage = "High-risk changes require acknowledgement before proceeding.";
            return Task.CompletedTask;
        }

        _navigation.Navigate(AppPage.Apply);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RefreshSelectionInfoAsync()
    {
        ErrorMessage = null;
        try
        {
            await EnsurePolicyIndexAsync();

            var ids = _selection.GetSelectedPolicies().Select(p => p.PolicyId).ToArray();
            SelectionIssues.Clear();
            if (ids.Length > 0)
            {
                BuildSelectionIssues(ids);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasHighRiskSelected));
            OnPropertyChanged(nameof(CanApplyAfterPreview));
            OnPropertyChanged(nameof(HasSelectionIssues));
            OnPropertyChanged(nameof(HasBlockingSelectionIssues));
        }
    }

    [RelayCommand]
    private async Task ResolveSelectionIssueAsync(PreviewSelectionIssueViewModel issue)
    {
        if (issue == null)
            return;

        ErrorMessage = null;
        try
        {
            if (issue.Kind == PreviewSelectionIssueKind.MissingDependencies)
            {
                await _selection.AddPoliciesAsync(issue.RelatedPolicies);
            }
            else if (issue.Kind == PreviewSelectionIssueKind.Conflicts)
            {
                await _selection.RemovePoliciesAsync(issue.RelatedPolicies);
            }

            await RefreshSelectionInfoAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task EnsurePolicyIndexAsync()
    {
        if (_policyIndex != null)
            return;

        var policies = await _serviceClient.GetPoliciesAsync(onlyApplicable: false);
        _policyIndex = policies.Policies
            .GroupBy(p => p.PolicyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    private void ApplyFilters()
    {
        Changes.Clear();

        IEnumerable<PreviewChangeRowViewModel> filtered = AllChanges;

        if (ShowOnlyFailures)
        {
            filtered = filtered.Where(c => !c.Success);
        }

        if (SelectedMechanismFilter?.Mechanism is MechanismType mech)
        {
            filtered = filtered.Where(c => c.Mechanism == mech);
        }

        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            filtered = filtered.Where(c =>
                c.PolicyId.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                c.PolicyName.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                (c.Description?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        foreach (var c in filtered)
            Changes.Add(c);

        OnPropertyChanged(nameof(ChangeCount));
        OnPropertyChanged(nameof(FailureCount));
        OnPropertyChanged(nameof(HasPreviewFailures));
    }

    private void BuildSelectionIssues(string[] selectedIds)
    {
        if (_policyIndex == null)
            return;

        var selected = new HashSet<string>(selectedIds, StringComparer.OrdinalIgnoreCase);

        var missingRequired = new List<(string from, PolicyDependency dep)>();
        var conflicts = new List<(string from, PolicyDependency dep)>();

        foreach (var id in selectedIds)
        {
            if (!_policyIndex.TryGetValue(id, out var def))
                continue;

            foreach (var dep in def.Dependencies)
            {
                if (dep.Type == DependencyType.Conflict)
                {
                    if (selected.Contains(dep.PolicyId))
                        conflicts.Add((id, dep));
                    continue;
                }

                var isHard = dep.Type is DependencyType.Required or DependencyType.Prerequisite;
                if (isHard && !selected.Contains(dep.PolicyId))
                {
                    missingRequired.Add((id, dep));
                }
            }
        }

        if (missingRequired.Count > 0)
        {
            var details = string.Join("\n", missingRequired
                .Take(10)
                .Select(m => $"- {m.from} requires {m.dep.PolicyId}: {m.dep.Reason}"));
            if (missingRequired.Count > 10)
                details += $"\n...and {missingRequired.Count - 10} more";

            SelectionIssues.Add(new PreviewSelectionIssueViewModel(
                PreviewIssueSeverity.Warning,
                PreviewSelectionIssueKind.MissingDependencies,
                "Missing required dependencies",
                details,
                missingRequired.Select(m => m.dep.PolicyId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                "Add dependencies"));
        }

        if (conflicts.Count > 0)
        {
            var details = string.Join("\n", conflicts
                .Take(10)
                .Select(c => $"- {c.from} conflicts with {c.dep.PolicyId}: {c.dep.Reason}"));
            if (conflicts.Count > 10)
                details += $"\n...and {conflicts.Count - 10} more";

            SelectionIssues.Add(new PreviewSelectionIssueViewModel(
                PreviewIssueSeverity.Error,
                PreviewSelectionIssueKind.Conflicts,
                "Conflicting policies selected",
                details,
                conflicts.Select(c => c.dep.PolicyId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                "Remove conflicts"));
        }

        OnPropertyChanged(nameof(HasSelectionIssues));
        OnPropertyChanged(nameof(HasBlockingSelectionIssues));
        OnPropertyChanged(nameof(CanProceedToApply));
    }
}
