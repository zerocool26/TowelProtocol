using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrivacyHardeningContracts.Models;
using PrivacyHardeningUI.Services;

using System.ComponentModel;
using System.Threading;
using Avalonia.Threading;

namespace PrivacyHardeningUI.ViewModels;

/// <summary>
/// ViewModel for the individual policy selection panel
/// </summary>
public partial class PolicySelectionViewModel : ObservableObject
{
    private readonly ServiceClient _serviceClient;
    private bool _suppressSelectionEvents;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private PolicyCategory? _selectedCategory;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _showOnlyApplicable = true;

    [ObservableProperty]
    private bool _showOnlySelected;
    
    [ObservableProperty]
    private string? _statusMessage;

    public ObservableCollection<PolicyItemViewModel> AllPolicies { get; } = new();
    public ObservableCollection<PolicyItemViewModel> FilteredPolicies { get; } = new();
    public ObservableCollection<string> AvailableProfiles { get; } = new();

    public PolicyCategory[] Categories { get; } = Enum.GetValues<PolicyCategory>();

    public PolicySelectionViewModel(ServiceClient serviceClient)
    {
        _serviceClient = serviceClient;

        // Automatically load policies when the view model is created
        _ = LoadPoliciesAsync();
    }

    [RelayCommand]
    private async Task LoadPoliciesAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var result = await _serviceClient.GetPoliciesAsync(ShowOnlyApplicable);

            AllPolicies.Clear();
            AvailableProfiles.Clear();
            var profiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var policy in result.Policies)
            {
                if (policy.IncludedInProfiles != null)
                {
                    foreach (var p in policy.IncludedInProfiles) profiles.Add(p);
                }

                var viewModel = new PolicyItemViewModel
                {
                    PolicyId = policy.PolicyId,
                    Name = policy.Name,
                    Description = policy.Description,
                    Category = policy.Category,
                    RiskLevel = policy.RiskLevel,
                    SupportStatus = policy.SupportStatus,
                    Mechanism = policy.Mechanism,
                    KnownBreakage = policy.KnownBreakage,
                    Dependencies = policy.Dependencies,
                    References = policy.References,
                    IncludedInProfiles = policy.IncludedInProfiles,
                    Notes = policy.Notes,
                    Reversible = policy.Reversible,
                    IsSelected = policy.EnabledByDefault,
                    IsApplicable = true // Service already filtered
                };

                viewModel.PropertyChanged += OnPolicyItemChanged;
                AllPolicies.Add(viewModel);
            }

            foreach (var p in profiles.OrderBy(p => p))
            {
                AvailableProfiles.Add(p);
            }

            ApplyFilters();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load policies: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnPolicyItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressSelectionEvents) return;
        if (e.PropertyName != nameof(PolicyItemViewModel.IsSelected)) return;
        if (sender is not PolicyItemViewModel changedPolicy) return;

        // Auto-select dependencies
        if (changedPolicy.IsSelected)
        {
            var autoDeps = changedPolicy.Dependencies
                .Where(d => d.Type is DependencyType.Required or DependencyType.Prerequisite || d.AutoSelect)
                .Select(d => d.PolicyId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (autoDeps.Length > 0)
            {
                _suppressSelectionEvents = true;
                try
                {
                    var count = 0;
                    foreach (var depId in autoDeps)
                    {
                        var dep = AllPolicies.FirstOrDefault(p => string.Equals(p.PolicyId, depId, StringComparison.OrdinalIgnoreCase));
                        if (dep != null && !dep.IsSelected)
                        {
                            dep.IsSelected = true;
                            count++;
                        }
                    }
                    if (count > 0)
                    {
                        StatusMessage = $"Auto-selected {count} dependenc{(count == 1 ? "y" : "ies")} for {changedPolicy.Name}";
                        // Clear status after delay
                        Dispatcher.UIThread.InvokeAsync(async () => {
                             await Task.Delay(3000);
                             if (StatusMessage?.Contains("Auto-selected") == true) StatusMessage = null;
                        });
                    }
                }
                finally
                {
                   _suppressSelectionEvents = false;
                }
            }
        }
        else
        {
            _suppressSelectionEvents = true;
            try
            {
                var dependents = AllPolicies.Where(p =>
                    p.Dependencies.Any(d =>
                        d.PolicyId.Equals(changedPolicy.PolicyId, StringComparison.OrdinalIgnoreCase)
                        && d.Type is DependencyType.Required or DependencyType.Prerequisite))
                    .ToList();

                var count = 0;
                foreach(var dep in dependents)
                {
                    if (dep.IsSelected)
                    {
                        dep.IsSelected = false;
                        count++;
                    }
                }
                if (count > 0)
                {
                    StatusMessage = $"Auto-unselected {count} dependent{(count == 1 ? "s" : "")} of {changedPolicy.Name}";
                    Dispatcher.UIThread.InvokeAsync(async () => {
                         await Task.Delay(3000);
                         if (StatusMessage?.Contains("Auto-unselected") == true) StatusMessage = null;
                    });
                }
            }
            finally
            {
                _suppressSelectionEvents = false;
            }
        }
    }

    [RelayCommand]
    private void ApplyFilters()
    {
        FilteredPolicies.Clear();

        var filtered = AllPolicies.AsEnumerable();

        // Filter by category
        if (SelectedCategory.HasValue)
        {
            filtered = filtered.Where(p => p.Category == SelectedCategory.Value);
        }

        // Filter by search text
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(p =>
                p.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                p.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                p.PolicyId.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        // Filter by selected
        if (ShowOnlySelected)
        {
            filtered = filtered.Where(p => p.IsSelected);
        }

        foreach (var policy in filtered)
        {
            FilteredPolicies.Add(policy);
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var policy in FilteredPolicies)
        {
            policy.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var policy in FilteredPolicies)
        {
            policy.IsSelected = false;
        }
    }

    [RelayCommand]
    private void SelectByRisk(RiskLevel maxRisk)
    {
        foreach (var policy in FilteredPolicies)
        {
            policy.IsSelected = policy.RiskLevel <= maxRisk;
        }
    }

    [RelayCommand]
    private void SelectProfile(string? profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName)) return;

        // Clear current selection to enforce the profile exactly?
        // Or keep it additive? 
        // Best UX: "Apply Profile" usually means "Set state to Profile".
        // But users might have custom selections. 
        // Let's toggle ON the profile items, leaving others as is (Additive).
        // Users can "Select None" first if they want pure profile.

        foreach (var p in AllPolicies)
        {
            if (p.IncludedInProfiles != null && p.IncludedInProfiles.Contains(profileName, StringComparer.OrdinalIgnoreCase))
            {
                p.IsSelected = true;
            }
        }
    }

    public IEnumerable<PolicyItemViewModel> GetSelectedPolicies()
    {
        return AllPolicies.Where(p => p.IsSelected);
    }

    public async Task EnsurePoliciesLoadedAsync()
    {
        if (AllPolicies.Count > 0)
        {
            return;
        }

        if (IsLoading)
        {
            while (IsLoading)
            {
                await Task.Delay(50);
            }

            return;
        }

        await LoadPoliciesAsync();
    }

    public async Task SelectOnlyAsync(IEnumerable<string> policyIds)
    {
        await EnsurePoliciesLoadedAsync();
        SelectOnly(policyIds);
    }

    public async Task AddPoliciesAsync(IEnumerable<string> policyIds)
    {
        await EnsurePoliciesLoadedAsync();
        AddPolicies(policyIds);
    }

    public async Task RemovePoliciesAsync(IEnumerable<string> policyIds)
    {
        await EnsurePoliciesLoadedAsync();
        RemovePolicies(policyIds);
    }

    public void AddPolicies(IEnumerable<string> policyIds)
    {
        if (policyIds == null) return;

        var toSelect = GetRequiredDependencyClosure(policyIds);
        _suppressSelectionEvents = true;
        try
        {
            foreach (var p in AllPolicies)
            {
                if (toSelect.Contains(p.PolicyId))
                {
                    p.IsSelected = true;
                }
            }
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        ApplyFilters();
    }

    public void RemovePolicies(IEnumerable<string> policyIds)
    {
        if (policyIds == null) return;

        var toRemove = GetDependentRemovalSet(policyIds);
        _suppressSelectionEvents = true;
        try
        {
            foreach (var p in AllPolicies)
            {
                if (toRemove.Contains(p.PolicyId))
                {
                    p.IsSelected = false;
                }
            }
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        ApplyFilters();
    }

    private HashSet<string> GetRequiredDependencyClosure(IEnumerable<string> policyIds)
    {
        var set = new HashSet<string>(policyIds, StringComparer.OrdinalIgnoreCase);
        var changed = true;

        while (changed)
        {
            changed = false;
            foreach (var id in set.ToArray())
            {
                var policy = AllPolicies.FirstOrDefault(p => string.Equals(p.PolicyId, id, StringComparison.OrdinalIgnoreCase));
                if (policy == null) continue;

                foreach (var dep in policy.Dependencies)
                {
                    if (dep.Type is DependencyType.Required or DependencyType.Prerequisite)
                    {
                        if (set.Add(dep.PolicyId))
                        {
                            changed = true;
                        }
                    }
                }
            }
        }

        return set;
    }

    private HashSet<string> GetDependentRemovalSet(IEnumerable<string> policyIds)
    {
        var toRemove = new HashSet<string>(policyIds, StringComparer.OrdinalIgnoreCase);
        var changed = true;

        while (changed)
        {
            changed = false;
            foreach (var policy in AllPolicies)
            {
                if (!policy.IsSelected) continue;

                if (policy.Dependencies.Any(d =>
                        toRemove.Contains(d.PolicyId)
                        && d.Type is DependencyType.Required or DependencyType.Prerequisite))
                {
                    if (toRemove.Add(policy.PolicyId))
                    {
                        changed = true;
                    }
                }
            }
        }

        return toRemove;
    }

    /// <summary>
    /// Replace the current selection with the provided policy IDs.
    /// Intended for workflow jumps (e.g., Audit → Preview on a specific policy).
    /// </summary>
    public void SelectOnly(IEnumerable<string> policyIds)
    {
        var set = new HashSet<string>(policyIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (var p in AllPolicies)
        {
            p.IsSelected = set.Contains(p.PolicyId);
        }

        ApplyFilters();
    }

    public void SelectOnly(string policyId)
    {
        if (string.IsNullOrWhiteSpace(policyId))
            return;

        SelectOnly(new[] { policyId });
    }

    partial void OnSelectedCategoryChanged(PolicyCategory? value)
    {
        ApplyFilters();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilters();
    }

    partial void OnShowOnlySelectedChanged(bool value)
    {
        ApplyFilters();
    }
}
