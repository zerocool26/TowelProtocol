using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningContracts.Responses;

/// <summary>
/// Result of an apply operation
/// </summary>
public sealed class ApplyResult : ResponseBase
{
    /// <summary>
    /// Policies that were successfully applied
    /// </summary>
    public required string[] AppliedPolicies { get; init; }

    /// <summary>
    /// Policies that failed to apply
    /// </summary>
    public required string[] FailedPolicies { get; init; }

    /// <summary>
    /// Detailed change records
    /// </summary>
    public required ChangeRecord[] Changes { get; init; }

    /// <summary>
    /// Restore point ID (if created)
    /// </summary>
    public string? RestorePointId { get; init; }

    /// <summary>
    /// Snapshot ID created for this operation
    /// </summary>
    public required string SnapshotId { get; init; }

    /// <summary>
    /// When operation completed
    /// </summary>
    public required DateTime CompletedAt { get; init; }

    /// <summary>
    /// Whether a system restart is recommended
    /// </summary>
    public bool RestartRecommended { get; init; }

    /// <summary>
    /// Policies that require restart to take effect
    /// </summary>
    public string[] PoliciesRequiringRestart { get; init; } = Array.Empty<string>();
}
