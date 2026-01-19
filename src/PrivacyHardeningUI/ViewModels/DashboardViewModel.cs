using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrivacyHardeningUI.Services;

namespace PrivacyHardeningUI.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly ServiceClient _serviceClient;
    private readonly StatusRailViewModel _statusRail;
    private readonly ITelemetryMonitorService _telemetryMonitor;
    private readonly INetworkTrafficMonitorService _networkMonitor;

    [ObservableProperty]
    private string _greeting = "Good Morning";

    [ObservableProperty]
    private int _policyCount;

    [ObservableProperty]
    private bool _isStandalone;

    [ObservableProperty]
    private string? _serviceMessage;

    [ObservableProperty]
    private string _diagnosticLevel = "Unknown";

    [ObservableProperty]
    private int _activeTelemetryServices;

    [ObservableProperty]
    private int _activeTelemetryTasks;

    [ObservableProperty]
    private int _activeTelemetryConnections;

    [ObservableProperty]
    private string _telemetryRiskStatus = "Unknown";
    
    [ObservableProperty]
    private bool _isScanningTelemetry;

    public ObservableCollection<TelemetryConnection> DetectedConnections { get; } = new();

    public DashboardViewModel(
        ServiceClient serviceClient, 
        StatusRailViewModel statusRail,
        ITelemetryMonitorService telemetryMonitor,
        INetworkTrafficMonitorService networkMonitor)
    {
        _serviceClient = serviceClient;
        _statusRail = statusRail;
        _telemetryMonitor = telemetryMonitor;
        _networkMonitor = networkMonitor;

        _serviceClient.StandaloneModeChanged += (_, standalone) => IsStandalone = standalone;
        IsStandalone = _serviceClient.IsStandaloneMode;

        UpdateGreeting();
        // Load initial data
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
        IsScanningTelemetry = true;
        try
        {
            // Update policies
            var result = await _serviceClient.GetPoliciesAsync(false);
            PolicyCount = result.Policies?.Length ?? 0;

            if (IsStandalone)
            {
                ServiceMessage = "Running in Standalone Mode. Real-time auditing limited.";
            }
            else
            {
                ServiceMessage = "Service is active and ready.";
            }

            // Update Telemetry Monitor
            var telemetryStatus = await _telemetryMonitor.GetTelemetryStatusAsync();
            var diagLevelComp = telemetryStatus.FirstOrDefault(c => c.Id == "DiagnosticDataLevel");
            DiagnosticLevel = diagLevelComp?.Description.Replace("Current level: ", "") ?? "Unknown";

            ActiveTelemetryServices = telemetryStatus.Count(c => c.Category == "Service" && c.Status == TelemetryStatus.Active);
            ActiveTelemetryTasks = telemetryStatus.Count(c => c.Category == "Task" && c.Status == TelemetryStatus.Active);

            // Update Network Monitor
            var connections = await _networkMonitor.AnalyzeTelemetryConnectionsAsync();
            ActiveTelemetryConnections = connections.Count;
            
            DetectedConnections.Clear();
            foreach(var conn in connections.OrderByDescending(c => c.RiskLevel).Take(5))
            {
                DetectedConnections.Add(conn);
            }

            // Calculate Risk
            if (ActiveTelemetryConnections > 0 || ActiveTelemetryServices > 0)
            {
                TelemetryRiskStatus = "Activity Detected";
            }
            else if (DiagnosticLevel == "Full")
            {
                 TelemetryRiskStatus = "High (Full Telemetry)";
            }
            else
            {
                TelemetryRiskStatus = "Protected";
            }
        }
        catch (Exception ex)
        {
            ServiceMessage = $"Error refreshing dashboard: {ex.Message}";
        }
        finally
        {
             IsScanningTelemetry = false;
        }
    }
}
