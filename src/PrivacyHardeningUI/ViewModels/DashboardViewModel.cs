using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrivacyHardeningUI.Services;

namespace PrivacyHardeningUI.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly ServiceClient _serviceClient;
    private readonly StatusRailViewModel _statusRail;

    [ObservableProperty]
    private string _greeting = "Good Morning";

    [ObservableProperty]
    private int _policyCount;

    [ObservableProperty]
    private bool _isStandalone;

    [ObservableProperty]
    private string? _serviceMessage;

    public DashboardViewModel(ServiceClient serviceClient, StatusRailViewModel statusRail)
    {
        _serviceClient = serviceClient;
        _statusRail = statusRail;

        _serviceClient.StandaloneModeChanged += (_, standalone) => IsStandalone = standalone;
        IsStandalone = _serviceClient.IsStandaloneMode;

        UpdateGreeting();
        _ = RefreshSummaryAsync();
    }

    private void UpdateGreeting()
    {
        var hour = DateTime.Now.Hour;
        Greeting = hour switch
        {
            < 12 => "Good Morning",
            < 17 => "Good Afternoon",
            _ => "Good Evening"
        };
    }

    [RelayCommand]
    public async Task RefreshSummaryAsync()
    {
        try
        {
            var result = await _serviceClient.GetPoliciesAsync(false);
            PolicyCount = result.Policies?.Length ?? 0;

            if (IsStandalone)
            {
                ServiceMessage = "Running in Standalone Mode. Policy application and real-time auditing require the background service.";
            }
            else
            {
                ServiceMessage = "Service is active and ready.";
            }

            // Sync with status rail if needed
        }
        catch
        {
            ServiceMessage = "Unable to determine service status.";
        }
    }
}
