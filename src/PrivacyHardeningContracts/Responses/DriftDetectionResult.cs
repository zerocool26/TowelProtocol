namespace PrivacyHardeningContracts.Responses;

/// <summary>
/// Result of drift detection
/// </summary>
public sealed class DriftDetectionResult : ResponseBase
{
    /// <summary>
    /// Whether drift was detected
    /// </summary>
    public required bool DriftDetected { get; init; }

    /// <summary>
    /// Policies that have drifted from expected state
    /// </summary>
    public required DriftItem[] DriftedPolicies { get; init; }

    /// <summary>
    /// When last applied
    /// </summary>
    public DateTime? LastAppliedAt { get; init; }

    /// <summary>
    /// Snapshot used for comparison
    /// </summary>
    public string? BaselineSnapshotId { get; init; }
}

public sealed class DriftItem
{
    public required string PolicyId { get; init; }
    public required string PolicyName { get; init; }
    public required string ExpectedValue { get; init; }
    public required string CurrentValue { get; init; }
    public required string DriftReason { get; init; }
}
