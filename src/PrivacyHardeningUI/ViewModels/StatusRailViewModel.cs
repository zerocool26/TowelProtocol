using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrivacyHardeningContracts.Responses;
using PrivacyHardeningUI.Services;

namespace PrivacyHardeningUI.ViewModels;

public sealed partial class StatusRailViewModel : ObservableObject
{
    private readonly ServiceClient _serviceClient;

    [ObservableProperty]
    private string _connectionState = "Unknown";

    [ObservableProperty]
    private string? _lastError;

    [ObservableProperty]
    private DateTimeOffset? _lastAuditAt;

    [ObservableProperty]
    private SystemInfo? _systemInfo;

    [ObservableProperty]
    private string? _currentSnapshotId;

    [ObservableProperty]
    private int _healthScore = -1; // -1 = pending audit

    [ObservableProperty]
    private int _totalPolicies;

    [ObservableProperty]
    private int _appliedPolicies;

    public string HealthStatus => HealthScore switch
    {
        < 0 => "Pending Audit",
        < 30 => "Critical",
        < 70 => "Fair",
        < 90 => "Good",
        _ => "Optimized"
    };

    public string HealthColor => HealthScore switch
    {
        < 0 => "#808080", // Gray
        < 30 => "#FF4444", // Red
        < 70 => "#FFBB33", // Orange/Yellow
        _ => "#00C851" // Green
    };

    public StatusRailViewModel(ServiceClient serviceClient)
    {
        _serviceClient = serviceClient;
    }

    public bool IsDomainJoined => SystemInfo?.IsDomainJoined == true;
    public bool IsMDMManaged => SystemInfo?.IsMDMManaged == true;

    partial void OnHealthScoreChanged(int value)
    {
        OnPropertyChanged(nameof(HealthStatus));
        OnPropertyChanged(nameof(HealthColor));
    }

    partial void OnSystemInfoChanged(SystemInfo? value)
    {
        OnPropertyChanged(nameof(IsDomainJoined));
        OnPropertyChanged(nameof(IsMDMManaged));
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        LastError = null;
        _serviceClient.Reconnect();

        try
        {
            var state = await _serviceClient.GetStateAsync(includeHistory: false);
            SystemInfo = state.SystemInfo;
            CurrentSnapshotId = state.CurrentState.SnapshotId;

            if (state.Success)
            {
                ConnectionState = "Connected";
                return;
            }

            if (_serviceClient.IsStandaloneMode)
            {
                // Keep it calm: standalone is a valid development/runtime mode.
                ConnectionState = "Standalone Mode";
                return;
            }

            ConnectionState = "Disconnected";
            // Only show an error banner for non-standalone failures.
            LastError = state.Errors?.FirstOrDefault()?.Message;
        }
        catch (ServiceUnavailableException)
        {
            ConnectionState = "Standalone (read-only)";
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            ConnectionState = _serviceClient.IsStandaloneMode ? "Standalone (read-only)" : "Disconnected";
        }
    }

    public void SetLastAuditNow()
    {
        LastAuditAt = DateTimeOffset.Now;
    }

    public void UpdateMetrics(int score, int total, int applied)
    {
        HealthScore = score;
        TotalPolicies = total;
        AppliedPolicies = applied;
        SetLastAuditNow();
    }
}
