using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrivacyHardeningUI.Services;

namespace PrivacyHardeningUI.ViewModels;

/// <summary>
/// ViewModel for real-time telemetry monitoring dashboard.
/// </summary>
public partial class TelemetryMonitorViewModel : ObservableObject
{
    private readonly ITelemetryMonitorService _telemetryMonitor;
    private CancellationTokenSource? _refreshCancellation;

    [ObservableProperty]
    private ObservableCollection<TelemetryComponentViewModel> _components = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isAutoRefreshEnabled = true;

    [ObservableProperty]
    private int _refreshIntervalSeconds = 5;

    [ObservableProperty]
    private DateTime _lastRefresh;

    [ObservableProperty]
    private int _activeCount;

    [ObservableProperty]
    private int _inactiveCount;

    [ObservableProperty]
    private int _disabledCount;

    [ObservableProperty]
    private string _statusSummary = string.Empty;

    public TelemetryMonitorViewModel(ITelemetryMonitorService telemetryMonitor)
    {
        _telemetryMonitor = telemetryMonitor;
        _lastRefresh = DateTime.Now;
    }

    /// <summary>
    /// Initialize the view model and start monitoring.
    /// </summary>
    public async Task InitializeAsync()
    {
        await RefreshAsync();
        StartAutoRefresh();
    }

    /// <summary>
    /// Refresh telemetry status.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;

        try
        {
            var components = await _telemetryMonitor.GetTelemetryStatusAsync();

            Components.Clear();
            foreach (var component in components.OrderBy(c => c.Category).ThenBy(c => c.Name))
            {
                Components.Add(new TelemetryComponentViewModel(component));
            }

            UpdateStatistics();
            LastRefresh = DateTime.Now;
        }
        catch (Exception ex)
        {
            // Log error (in production, use proper logging)
            StatusSummary = $"Error refreshing: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Toggle auto-refresh on/off.
    /// </summary>
    [RelayCommand]
    private void ToggleAutoRefresh()
    {
        IsAutoRefreshEnabled = !IsAutoRefreshEnabled;

        if (IsAutoRefreshEnabled)
        {
            StartAutoRefresh();
        }
        else
        {
            StopAutoRefresh();
        }
    }

    /// <summary>
    /// Start automatic refresh timer.
    /// </summary>
    private void StartAutoRefresh()
    {
        StopAutoRefresh();
        _refreshCancellation = new CancellationTokenSource();

        _ = Task.Run(async () =>
        {
            while (!_refreshCancellation.Token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(RefreshIntervalSeconds), _refreshCancellation.Token);

                if (!_refreshCancellation.Token.IsCancellationRequested && IsAutoRefreshEnabled)
                {
                    await RefreshAsync();
                }
            }
        }, _refreshCancellation.Token);
    }

    /// <summary>
    /// Stop automatic refresh.
    /// </summary>
    private void StopAutoRefresh()
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = null;
    }

    /// <summary>
    /// Update statistics counters.
    /// </summary>
    private void UpdateStatistics()
    {
        ActiveCount = Components.Count(c => c.Status == TelemetryStatus.Active);
        InactiveCount = Components.Count(c => c.Status == TelemetryStatus.Inactive);
        DisabledCount = Components.Count(c => c.Status == TelemetryStatus.Disabled);

        var totalComponents = Components.Count;
        var privacyScore = totalComponents > 0
            ? (int)((DisabledCount + InactiveCount) / (double)totalComponents * 100)
            : 0;

        StatusSummary = $"{ActiveCount} active, {DisabledCount} disabled - Privacy Score: {privacyScore}%";
    }

    /// <summary>
    /// Cleanup resources.
    /// </summary>
    public void Dispose()
    {
        StopAutoRefresh();
    }
}

/// <summary>
/// ViewModel for individual telemetry component.
/// </summary>
public partial class TelemetryComponentViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _category = string.Empty;

    [ObservableProperty]
    private TelemetryStatus _status;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private DateTime _lastChecked;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _statusColor = string.Empty;

    public TelemetryComponentViewModel(TelemetryComponent component)
    {
        Id = component.Id;
        Name = component.Name;
        Category = component.Category;
        Status = component.Status;
        Description = component.Description;
        LastChecked = component.LastChecked;

        UpdateStatusDisplay();
    }

    private void UpdateStatusDisplay()
    {
        StatusText = Status switch
        {
            TelemetryStatus.Active => "ACTIVE",
            TelemetryStatus.Inactive => "Inactive",
            TelemetryStatus.Disabled => "Disabled",
            TelemetryStatus.Unknown => "Unknown",
            _ => "Unknown"
        };

        StatusColor = Status switch
        {
            TelemetryStatus.Active => "#F87171", // Red (privacy concern)
            TelemetryStatus.Inactive => "#FBBF24", // Yellow (warning)
            TelemetryStatus.Disabled => "#34D399", // Green (good for privacy)
            TelemetryStatus.Unknown => "#9CA3AF", // Gray
            _ => "#9CA3AF"
        };
    }
}
