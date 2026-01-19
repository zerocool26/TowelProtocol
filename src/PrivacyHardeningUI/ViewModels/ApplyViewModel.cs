using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrivacyHardeningUI.Services;
using PrivacyHardeningContracts.Commands;
using Avalonia.Controls.Notifications;
using System.Text;
using System;

namespace PrivacyHardeningUI.ViewModels;

public sealed partial class ApplyViewModel : ObservableObject
{
    private readonly ServiceClient _serviceClient;
    private readonly INotificationManager? _notificationManager;

    public PolicySelectionViewModel Selection { get; }

    [ObservableProperty]
    private bool createRestorePoint = true;

    [ObservableProperty]
    private bool isApplying;

    [ObservableProperty]
    private string lastOperationResult = string.Empty;

    public ApplyViewModel(PolicySelectionViewModel selection, ServiceClient serviceClient)
    {
        Selection = selection;
        _serviceClient = serviceClient;
        
        // Try to resolve notification manager if available in App (optional)
        _notificationManager = App.Current?.Resources["NotificationManager"] as INotificationManager;
    }

    public int SelectedCount => Selection.GetSelectedPolicies().Count();

    public bool HasHighRiskSelected => Selection.GetSelectedPolicies().Any(p => p.RiskLevel >= PrivacyHardeningContracts.Models.RiskLevel.High);

    [RelayCommand]
    private Task RefreshSummaryAsync()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasHighRiskSelected));
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (IsApplying) return;

        var policies = Selection.GetSelectedPolicies().ToList();
        if (policies.Count == 0)
        {
            LastOperationResult = "No policies selected.";
            return;
        }

        IsApplying = true;
        LastOperationResult = "Applying policies...";

        try
        {
            var result = await _serviceClient.ApplyAsync(
                policies.Select(p => p.PolicyId).ToArray(), 
                overrides: null, 
                createRestorePoint: CreateRestorePoint, 
                dryRun: false
            );

            var sb = new StringBuilder();
            if (result.Success)
            {
                sb.AppendLine("Operation completed successfully.");
            }
            else
            {
                sb.AppendLine("Operation failed.");
                foreach(var err in result.Errors) sb.AppendLine($"- {err.Message}");
            }

            var applied = result.AppliedPolicies.Length;
            var failed = result.FailedPolicies.Length;
            sb.AppendLine($"Applied: {applied}, Failed: {failed}");

            if (!string.IsNullOrEmpty(result.RestorePointId))
            {
                sb.AppendLine($"Restore Point Created: {result.RestorePointId}");
            }

            LastOperationResult = sb.ToString();

            // Refresh dashboards/state if needed
            // Selection.Refresh...?
        }
        catch (Exception ex)
        {
            LastOperationResult = $"Error: {ex.Message}";
        }
        finally
        {
            IsApplying = false;
        }
    }
}
