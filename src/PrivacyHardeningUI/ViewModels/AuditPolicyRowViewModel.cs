using System;
using CommunityToolkit.Mvvm.ComponentModel;
using PrivacyHardeningContracts.Models;
using PrivacyHardeningContracts.Responses;

namespace PrivacyHardeningUI.ViewModels;

public sealed partial class AuditPolicyRowViewModel : ObservableObject
{
    public PolicyAuditItem Item { get; }
    public PolicyDefinition? Definition { get; }

    [ObservableProperty]
    private bool _isSelected;

    public AuditPolicyRowViewModel(PolicyAuditItem item, PolicyDefinition? definition = null)
    {
        Item = item;
        Definition = definition;
    }

    public string PolicyId => Item.PolicyId;
    public string PolicyName => Item.PolicyName;
    public PolicyCategory Category => Item.Category;
    public RiskLevel RiskLevel => Item.RiskLevel;
    public SupportStatus SupportStatus => Item.SupportStatus;

    public MechanismType? Mechanism => Definition?.Mechanism;
    public bool? Reversible => Definition?.Reversible;
    public string[] Tags => Definition?.Tags ?? Array.Empty<string>();
    public string? Notes => Definition?.Notes;
    public string? Description => Definition?.Description;
    public string? TechnicalEvidence => Definition?.EvidencePath;
    public ImpactRating? Impact => Definition?.Impact;

    public string PrivacyImpactText => Impact != null ? Impact.Privacy switch { 3 => "High", 2 => "Medium", 1 => "Low", _ => "None" } : "Unknown";
    public string PerformanceImpactText => Impact != null ? Impact.Performance switch { 3 => "High", 2 => "Noticeable", 1 => "Negligible", _ => "None" } : "Unknown";
    public string CompatibilityRiskText => Impact != null ? Impact.Compatibility switch { 3 => "High", 2 => "Medium", 1 => "Low", _ => "None" } : "Unknown";

    public bool IsApplied => Item.IsApplied;
    public bool IsApplicable => Item.IsApplicable;
    public bool Matches => Item.Matches;

    public string? CurrentValue => Item.CurrentValue;
    public string? ExpectedValue => Item.ExpectedValue;
    public string? EvidenceDetails => Item.EvidenceDetails; // Technical evidence for deep fine-tuning
    public string? DriftDescription => Item.DriftDescription;
    public string? NotApplicableReason => Item.NotApplicableReason;

    public AuditComplianceStatus ComplianceStatus
    {
        get
        {
            if (string.Equals(Item.CurrentValue, "Unknown", StringComparison.OrdinalIgnoreCase) && 
                string.Equals(Item.ExpectedValue, "Audit Required", StringComparison.OrdinalIgnoreCase))
            {
                return AuditComplianceStatus.Unknown;
            }

            if (!IsApplicable) return AuditComplianceStatus.NotApplicable;
            if (Matches) return AuditComplianceStatus.Compliant;
            // When applicable but doesn't match, treat as non-compliant.
            return AuditComplianceStatus.NonCompliant;
        }
    }

    public string StatusPillText => ComplianceStatus switch
    {
        AuditComplianceStatus.Compliant => "Compliant",
        AuditComplianceStatus.NonCompliant => "Non-compliant",
        AuditComplianceStatus.NotApplicable => "Not applicable",
        _ => "Pending"
    };

    public string RiskText => RiskLevel.ToString();

    public string SupportText => SupportStatus.ToString();

    public string MechanismText => Mechanism?.ToString() ?? "Unknown";

    public string MechanismIcon => (Mechanism ?? MechanismType.Registry) switch
    {
        MechanismType.Registry => "registry",
        MechanismType.Service => "service",
        MechanismType.ScheduledTask => "task",
        MechanismType.Firewall => "firewall",
        MechanismType.PowerShell => "powershell",
        MechanismType.GroupPolicy => "gpo",
        MechanismType.MDM => "mdm",
        MechanismType.HostsFile => "hosts",
        MechanismType.WFPDriver => "wfp",
        _ => "settings"
    };

    public bool IsReversible => Reversible == true;

    public string? SummaryLine
    {
        get
        {
            if (!IsApplicable)
            {
                return string.IsNullOrWhiteSpace(NotApplicableReason) ? "Not applicable" : NotApplicableReason;
            }

            if (Matches)
            {
                return "Matches expected state";
            }

            if (!string.IsNullOrWhiteSpace(DriftDescription))
            {
                return DriftDescription;
            }

            return "Differs from expected state";
        }
    }

    public string EvidenceBlock
    {
        get
        {
            // Evidence-first: always show current vs expected, technical path, and optional drift description.
            var before = CurrentValue ?? "(null)";
            var after = ExpectedValue ?? "(null)";
            var tech = string.IsNullOrWhiteSpace(TechnicalEvidence) ? string.Empty : $"\n\nTECHNICAL EVIDENCE PATH:\n{TechnicalEvidence}";
            var detail = string.IsNullOrWhiteSpace(EvidenceDetails) ? string.Empty : $"\n\nTECHNICAL DETAILS:\n{EvidenceDetails}";
            var drift = string.IsNullOrWhiteSpace(DriftDescription) ? string.Empty : $"\n\nDRIFT STATUS:\n{DriftDescription}";
            
            return $"CURRENT STATE:\n{before}\n\nEXPECTED STATE:\n{after}{tech}{detail}{drift}";
        }
    }
}
