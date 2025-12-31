using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningContracts.Responses;

/// <summary>
/// Result of an audit operation
/// </summary>
public sealed class AuditResult : ResponseBase
{
    /// <summary>
    /// Individual policy audit results
    /// </summary>
    public required PolicyAuditItem[] Items { get; init; }

    /// <summary>
    /// System information
    /// </summary>
    public required SystemInfo SystemInfo { get; init; }
}

/// <summary>
/// Audit result for a single policy
/// </summary>
public sealed class PolicyAuditItem
{
    public required string PolicyId { get; init; }
    public required string PolicyName { get; init; }
    public required PolicyCategory Category { get; init; }
    public required RiskLevel RiskLevel { get; init; }
    public required SupportStatus SupportStatus { get; init; }

    /// <summary>
    /// Is this policy currently applied?
    /// </summary>
    public required bool IsApplied { get; init; }

    /// <summary>
    /// Is this policy applicable to current system?
    /// </summary>
    public required bool IsApplicable { get; init; }

    /// <summary>
    /// Reason why not applicable (if applicable)
    /// </summary>
    public string? NotApplicableReason { get; init; }

    /// <summary>
    /// Current system value vs expected value
    /// </summary>
    public string? CurrentValue { get; init; }
    public string? ExpectedValue { get; init; }

    /// <summary>
    /// Whether system state matches expected
    /// </summary>
    public required bool Matches { get; init; }

    /// <summary>
    /// Description of drift (if any)
    /// </summary>
    public string? DriftDescription { get; init; }
}

/// <summary>
/// System information
/// </summary>
public sealed class SystemInfo
{
    public required int WindowsBuild { get; init; }
    public required string WindowsVersion { get; init; }
    public required string WindowsSku { get; init; }
    public required bool IsDomainJoined { get; init; }
    public required bool IsMDMManaged { get; init; }
    public required bool DefenderTamperProtectionEnabled { get; init; }
}
