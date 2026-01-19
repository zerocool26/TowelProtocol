using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrivacyHardeningContracts.Responses;
using PrivacyHardeningUI.Services;

namespace PrivacyHardeningUI.ViewModels;

public partial class AdvisorViewModel : ObservableObject
{
    private readonly ServiceClient _client;
    private readonly NavigationService _navigation;
    private readonly PolicySelectionViewModel _selectionViewModel;
    private readonly AuditViewModel _auditViewModel;
    private readonly PreviewViewModel _previewViewModel;

    [ObservableProperty] private int _privacyScore;
    [ObservableProperty] private string _grade = "-";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    public ObservableCollection<RecommendationItem> Recommendations { get; } = new();
    public bool HasRecommendations => Recommendations.Count > 0;
    public bool CanReviewAll => !IsLoading
        && !_client.IsStandaloneMode
        && Recommendations.Any(r => r.RelatedPolicyIds != null && r.RelatedPolicyIds.Length > 0);
    public bool CanAuditAll => !IsLoading
        && !_client.IsStandaloneMode
        && Recommendations.Any(r => r.RelatedPolicyIds != null && r.RelatedPolicyIds.Length > 0);
    public bool CanApplyPack => !IsLoading
        && !_client.IsStandaloneMode
        && Recommendations.Any(r => r.RelatedPolicyIds != null && r.RelatedPolicyIds.Length > 0);
    public bool ShowEmptyState => !IsLoading && !HasRecommendations;

    public AdvisorViewModel(ServiceClient client, NavigationService navigation, PolicySelectionViewModel selectionViewModel, AuditViewModel auditViewModel, PreviewViewModel previewViewModel)
    {
        _client = client;
        _navigation = navigation;
        _selectionViewModel = selectionViewModel;
        _auditViewModel = auditViewModel;
        _previewViewModel = previewViewModel;

        Recommendations.CollectionChanged += OnRecommendationsChanged;
        _ = LoadAsync();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        Recommendations.Clear();

        try
        {
            var result = await _client.GetRecommendationsAsync();
            if (result.Success)
            {
                PrivacyScore = result.PrivacyScore;
                Grade = result.Grade;
                
                foreach(var rec in result.Recommendations)
                {
                    Recommendations.Add(rec);
                }
                UpdateDerivedState();
            }
            else
            {
                ErrorMessage = "Failed to load recommendations: " + (result.Errors.FirstOrDefault()?.Message ?? "Unknown error");
            }
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
    public async Task FixIssue(RecommendationItem item)
    {
        if (item == null)
        {
            return;
        }

        if (item.RelatedPolicyIds == null || item.RelatedPolicyIds.Length == 0)
        {
            ErrorMessage = "No related policies were provided for this recommendation.";
            return;
        }

        if (_client.IsStandaloneMode)
        {
            ErrorMessage = "Standalone (read-only): start the service to preview or apply fixes.";
            return;
        }

        ErrorMessage = null;
        await _selectionViewModel.SelectOnlyAsync(item.RelatedPolicyIds);
        _navigation.Navigate(AppPage.Preview);
    }

    [RelayCommand]
    public async Task ReviewAllAsync()
    {
        if (_client.IsStandaloneMode)
        {
            ErrorMessage = "Standalone (read-only): start the service to preview or apply fixes.";
            return;
        }

        var policyIds = Recommendations
            .SelectMany(r => r.RelatedPolicyIds ?? Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (policyIds.Length == 0)
        {
            ErrorMessage = "No related policies were found for the current recommendations.";
            return;
        }

        ErrorMessage = null;
        await _selectionViewModel.SelectOnlyAsync(policyIds);
        _navigation.Navigate(AppPage.Preview);
    }

    [RelayCommand]
    public async Task ApplyRecommendedPackAsync()
    {
        if (_client.IsStandaloneMode)
        {
            ErrorMessage = "Standalone (read-only): start the service to preview or apply fixes.";
            return;
        }

        var policyIds = Recommendations
            .SelectMany(r => r.RelatedPolicyIds ?? Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (policyIds.Length == 0)
        {
            ErrorMessage = "No related policies were found for the current recommendations.";
            return;
        }

        // Safe workflow: select → preview (auto-generate) → user explicitly proceeds to apply.
        ErrorMessage = null;
        await _selectionViewModel.SelectOnlyAsync(policyIds);
        _navigation.Navigate(AppPage.Preview);
        await _previewViewModel.GeneratePreviewAsync();
    }

    [RelayCommand]
    public async Task AuditIssueAsync(RecommendationItem item)
    {
        if (item == null)
        {
            return;
        }

        if (item.RelatedPolicyIds == null || item.RelatedPolicyIds.Length == 0)
        {
            ErrorMessage = "No related policies were provided for this recommendation.";
            return;
        }

        if (_client.IsStandaloneMode)
        {
            ErrorMessage = "Standalone (read-only): start the service to run audits.";
            return;
        }

        ErrorMessage = null;
        await _auditViewModel.RunAuditForPoliciesAsync(item.RelatedPolicyIds);
        _navigation.Navigate(AppPage.Audit);
    }

    [RelayCommand]
    public async Task AuditAllAsync()
    {
        if (_client.IsStandaloneMode)
        {
            ErrorMessage = "Standalone (read-only): start the service to run audits.";
            return;
        }

        var policyIds = Recommendations
            .SelectMany(r => r.RelatedPolicyIds ?? Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (policyIds.Length == 0)
        {
            ErrorMessage = "No related policies were found for the current recommendations.";
            return;
        }

        ErrorMessage = null;
        await _auditViewModel.RunAuditForPoliciesAsync(policyIds);
        _navigation.Navigate(AppPage.Audit);
    }

    private void OnRecommendationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateDerivedState();
    }

    private void UpdateDerivedState()
    {
        OnPropertyChanged(nameof(HasRecommendations));
        OnPropertyChanged(nameof(CanReviewAll));
        OnPropertyChanged(nameof(CanAuditAll));
        OnPropertyChanged(nameof(CanApplyPack));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    partial void OnIsLoadingChanged(bool value)
    {
        UpdateDerivedState();
    }
}
