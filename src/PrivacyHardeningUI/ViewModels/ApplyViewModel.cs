using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PrivacyHardeningUI.ViewModels;

public sealed partial class ApplyViewModel : ObservableObject
{
    public PolicySelectionViewModel Selection { get; }

    public ApplyViewModel(PolicySelectionViewModel selection)
    {
        Selection = selection;
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
}
