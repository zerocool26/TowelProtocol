namespace PrivacyHardeningContracts.Commands;

/// <summary>
/// Command to revert previously applied policies
/// </summary>
public sealed class RevertCommand : CommandBase
{
    public override string CommandType => "Revert";

    /// <summary>
    /// Policy IDs to revert (null = revert all)
    /// </summary>
    public string[]? PolicyIds { get; init; }

    /// <summary>
    /// Restore from specific snapshot ID
    /// </summary>
    public string? SnapshotId { get; init; }

    /// <summary>
    /// Use Windows System Restore point
    /// </summary>
    public string? RestorePointId { get; init; }

    /// <summary>
    /// Create restore point before reverting
    /// </summary>
    public bool CreateRestorePoint { get; init; } = true;
}
