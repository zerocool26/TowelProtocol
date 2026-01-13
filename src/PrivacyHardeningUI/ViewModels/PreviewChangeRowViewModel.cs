using System;
using Avalonia.Media;
using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningUI.ViewModels;

public sealed class PreviewChangeRowViewModel
{
    private static readonly IBrush _statusOk = new SolidColorBrush(Color.Parse("#10B981"));
    private static readonly IBrush _statusBad = new SolidColorBrush(Color.Parse("#EF4444"));

    private static readonly IBrush _riskLow = new SolidColorBrush(Color.Parse("#10B981"));
    private static readonly IBrush _riskMedium = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush _riskHigh = new SolidColorBrush(Color.Parse("#F97316"));
    private static readonly IBrush _riskCritical = new SolidColorBrush(Color.Parse("#EF4444"));

    public required string PolicyId { get; init; }
    public required string PolicyName { get; init; }

    public required ChangeOperation Operation { get; init; }
    public required MechanismType Mechanism { get; init; }

    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public required string Description { get; init; }

    public string? BeforeState { get; init; }
    public required string AfterState { get; init; }

    public RiskLevel RiskLevel { get; init; } = RiskLevel.Medium;
    public SupportStatus SupportStatus { get; init; } = SupportStatus.Supported;
    public bool Reversible { get; init; }
    public string? Notes { get; init; }

    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    public BreakageScenario[] KnownBreakage { get; init; } = Array.Empty<BreakageScenario>();

    public string OperationText => Operation.ToString();

    public string MechanismText => Mechanism switch
    {
        MechanismType.GroupPolicy => "Group Policy",
        MechanismType.Registry => "Registry",
        MechanismType.MDM => "MDM/CSP",
        MechanismType.Service => "Service",
        MechanismType.ScheduledTask => "Scheduled Task",
        MechanismType.Firewall => "Firewall",
        MechanismType.PowerShell => "PowerShell",
        MechanismType.HostsFile => "Hosts File",
        MechanismType.WFPDriver => "WFP Driver",
        _ => Mechanism.ToString()
    };

    public string MechanismIcon => Mechanism switch
    {
        MechanismType.GroupPolicy => "gpo",
        MechanismType.Registry => "registry",
        MechanismType.MDM => "mdm",
        MechanismType.Service => "service",
        MechanismType.ScheduledTask => "task",
        MechanismType.Firewall => "firewall",
        MechanismType.PowerShell => "powershell",
        MechanismType.HostsFile => "hosts",
        MechanismType.WFPDriver => "wfp",
        _ => "info"
    };

    public string RiskBadge => RiskLevel switch
    {
        RiskLevel.Low => "Low",
        RiskLevel.Medium => "Medium",
        RiskLevel.High => "High",
        RiskLevel.Critical => "Critical",
        _ => "?"
    };

    public IBrush StatusBrush => Success ? _statusOk : _statusBad;

    public IBrush RiskBrush => RiskLevel switch
    {
        RiskLevel.Low => _riskLow,
        RiskLevel.Medium => _riskMedium,
        RiskLevel.High => _riskHigh,
        RiskLevel.Critical => _riskCritical,
        _ => _riskMedium
    };

    public string StatusBadge => Success ? "Ready" : "Blocked";

    public string? SummaryLine
    {
        get
        {
            if (!Success && !string.IsNullOrWhiteSpace(ErrorMessage))
                return ErrorMessage;
            return Description;
        }
    }
}
