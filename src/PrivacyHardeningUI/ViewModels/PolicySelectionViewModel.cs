using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrivacyHardeningContracts.Models;
using PrivacyHardeningUI.Services;

namespace PrivacyHardeningUI.ViewModels;

/// <summary>
/// ViewModel for the individual policy selection panel
/// </summary>
public partial class PolicySelectionViewModel : ObservableObject
{
    private readonly ServiceClient _serviceClient;

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

    public ObservableCollection<PolicyItemViewModel> AllPolicies { get; } = new();
    public ObservableCollection<PolicyItemViewModel> FilteredPolicies { get; } = new();

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
            foreach (var policy in result.Policies)
            {
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
                    Dependencies = policy.Dependencies.Select(d => d.PolicyId).ToArray(),
                    References = policy.References,
                    Notes = policy.Notes,
                    Reversible = policy.Reversible,
                    IsSelected = policy.EnabledByDefault,
                    IsApplicable = true // Service already filtered
                };

                AllPolicies.Add(viewModel);
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

    public IEnumerable<PolicyItemViewModel> GetSelectedPolicies()
    {
        return AllPolicies.Where(p => p.IsSelected);
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
