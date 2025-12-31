using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrivacyHardeningContracts.Responses;
using PrivacyHardeningUI.Services;

namespace PrivacyHardeningUI.ViewModels;

public partial class AuditViewModel : ObservableObject
{
    private readonly ServiceClient _serviceClient;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<PolicyAuditItem> AuditItems { get; } = new();

    public AuditViewModel(ServiceClient serviceClient)
    {
        _serviceClient = serviceClient;
    }

    [RelayCommand]
    public async Task RunAuditAsync()
    {
        IsRunning = true;
        ErrorMessage = null;

        try
        {
            var result = await _serviceClient.AuditAsync();

            AuditItems.Clear();
            foreach (var item in result.Items)
            {
                AuditItems.Add(item);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Audit failed: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }
}
