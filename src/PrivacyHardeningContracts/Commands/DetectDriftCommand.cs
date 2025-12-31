namespace PrivacyHardeningContracts.Commands;

/// <summary>
/// Command to detect drift from last applied configuration
/// (e.g., after Windows update)
/// </summary>
public sealed class DetectDriftCommand : CommandBase
{
    public override string CommandType => "DetectDrift";

    /// <summary>
    /// Compare against specific snapshot
    /// </summary>
    public string? SnapshotId { get; init; }
}
