namespace PrivacyHardeningContracts.Models;

/// <summary>
/// Point-in-time snapshot of system state
/// </summary>
public sealed class SystemSnapshot
{
    /// <summary>
    /// Snapshot ID
    /// </summary>
    public required string SnapshotId { get; init; }

    /// <summary>
    /// When this snapshot was created
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// Windows build number
    /// </summary>
    public required int WindowsBuild { get; init; }

    /// <summary>
    /// Windows SKU (Enterprise, Pro, etc.)
    /// </summary>
    public required string WindowsSku { get; init; }

    /// <summary>
    /// Applied policies at snapshot time
    /// </summary>
    public required string[] AppliedPolicies { get; init; }

    /// <summary>
    /// Full change history
    /// </summary>
    public required ChangeRecord[] ChangeHistory { get; init; }

    /// <summary>
    /// System restore point ID (if created)
    /// </summary>
    public string? RestorePointId { get; init; }

    /// <summary>
    /// Snapshot description
    /// </summary>
    public string? Description { get; init; }
}
