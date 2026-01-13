using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrivacyHardeningContracts.Responses;
using PrivacyHardeningUI.Services;

namespace PrivacyHardeningUI.ViewModels;

public sealed partial class DriftViewModel : ObservableObject
{
    private readonly ServiceClient _serviceClient;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private DriftDetectionResult? _lastResult;

    public ObservableCollection<DriftItem> DriftedPolicies { get; } = new();

    public DriftViewModel(ServiceClient serviceClient)
    {
        _serviceClient = serviceClient;
    }

    [RelayCommand]
    public async Task DetectDriftAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        DriftedPolicies.Clear();
        LastResult = null;

        try
        {
            var result = await _serviceClient.DetectDriftAsync(snapshotId: null);
            LastResult = result;

            if (!result.Success)
            {
                ErrorMessage = result.Errors?.FirstOrDefault()?.Message ?? "Drift detection is unavailable without the service.";
                return;
            }

            foreach (var item in result.DriftedPolicies.OrderBy(i => i.PolicyId))
            {
                DriftedPolicies.Add(item);
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
}
