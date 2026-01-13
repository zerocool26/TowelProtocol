using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrivacyHardeningContracts.Models;
using PrivacyHardeningContracts.Responses;
using PrivacyHardeningUI.Services;

namespace PrivacyHardeningUI.ViewModels;

public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly ServiceClient _serviceClient;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private SystemSnapshot? _snapshot;

    public ObservableCollection<HistorySessionViewModel> Sessions { get; } = new();

    public HistoryViewModel(ServiceClient serviceClient)
    {
        _serviceClient = serviceClient;
        
        // Initial load
        _ = RefreshAsync();
    }

    [RelayCommand]
    public async Task RevertSessionAsync(HistorySessionViewModel? session)
    {
        if (session == null || string.IsNullOrWhiteSpace(session.SnapshotId)) return;

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var result = await _serviceClient.RevertAsync(snapshotId: session.SnapshotId);
            if (!result.Success)
            {
                ErrorMessage = result.Errors?.FirstOrDefault()?.Message ?? "Revert failed.";
            }
            else
            {
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Revert failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        Sessions.Clear();
        Snapshot = null;

        try
        {
            var state = await _serviceClient.GetStateAsync(includeHistory: true);

            if (!state.Success)
            {
                if (_serviceClient.IsStandaloneMode)
                {
                    ErrorMessage = "History is unavailable in Standalone mode. Please start the Privacy Hardening Service to view session history and perform rollbacks.";
                }
                else
                {
                    ErrorMessage = state.Errors?.FirstOrDefault()?.Message ?? "History is unavailable without the service.";
                }
                return;
            }
            Snapshot = state.CurrentState;

            var grouped = state.CurrentState.ChangeHistory
                .GroupBy(c => c.SnapshotId ?? "default")
                .OrderByDescending(g => g.Max(c => c.AppliedAt));

            foreach (var group in grouped)
            {
                var id = group.Key == "default" ? null : group.Key;
                Sessions.Add(new HistorySessionViewModel(id, group));
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
