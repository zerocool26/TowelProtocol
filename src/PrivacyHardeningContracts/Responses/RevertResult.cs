using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningContracts.Responses;

/// <summary>
/// Result of a revert operation
/// </summary>
public sealed class RevertResult : ResponseBase
{
    /// <summary>
    /// Policies that were successfully reverted
    /// </summary>
    public required string[] RevertedPolicies { get; init; }

    /// <summary>
    /// Policies that failed to revert
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
    /// When operation completed
    /// </summary>
    public required DateTime CompletedAt { get; init; }

    /// <summary>
    /// Whether a system restart is recommended
    /// </summary>
    public bool RestartRecommended { get; init; }
}
