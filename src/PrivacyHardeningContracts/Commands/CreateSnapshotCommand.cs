namespace PrivacyHardeningContracts.Commands;

/// <summary>
/// Command to create a snapshot of current system state
/// </summary>
public sealed class CreateSnapshotCommand : CommandBase
{
    public override string CommandType => "CreateSnapshot";

    /// <summary>
    /// Snapshot description
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Also create Windows restore point
    /// </summary>
    public bool CreateRestorePoint { get; init; } = false;
}
