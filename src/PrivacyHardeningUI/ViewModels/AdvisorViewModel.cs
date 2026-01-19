using System;
using System.Collections.ObjectModel;
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

    [ObservableProperty] private int _privacyScore;
    [ObservableProperty] private string _grade = "-";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    public ObservableCollection<RecommendationItem> Recommendations { get; } = new();

    public AdvisorViewModel(ServiceClient client, NavigationService navigation, PolicySelectionViewModel selectionViewModel)
    {
        _client = client;
        _navigation = navigation;
        _selectionViewModel = selectionViewModel;

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
}
