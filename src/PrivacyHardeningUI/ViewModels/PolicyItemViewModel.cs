using CommunityToolkit.Mvvm.ComponentModel;
using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningUI.ViewModels;

/// <summary>
/// ViewModel for individual policy item in the selection UI
/// </summary>
public partial class PolicyItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isApplied;

    [ObservableProperty]
    private bool _isApplicable;

    public required string PolicyId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required PolicyCategory Category { get; init; }
    public required RiskLevel RiskLevel { get; init; }
    public required SupportStatus SupportStatus { get; init; }
    public required MechanismType Mechanism { get; init; }
    public required BreakageScenario[] KnownBreakage { get; init; }
    public required string[] Dependencies { get; init; }
    public required string[] References { get; init; }
    public required string? Notes { get; init; }
    public required bool Reversible { get; init; }

    public string RiskBadge => RiskLevel switch
    {
        RiskLevel.Low => "✓ Low Risk",
        RiskLevel.Medium => "⚠ Medium Risk",
        RiskLevel.High => "⚠ High Risk",
        RiskLevel.Critical => "⛔ Critical Risk",
        _ => "?"
    };

    public string SupportBadge => SupportStatus switch
    {
        SupportStatus.Supported => "Supported",
        SupportStatus.Undocumented => "Undocumented",
        SupportStatus.Unsupported => "Unsupported",
        SupportStatus.Deprecated => "⛔ Deprecated",
        _ => "?"
    };
}
