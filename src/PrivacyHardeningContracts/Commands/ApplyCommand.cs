namespace PrivacyHardeningContracts.Commands;

/// <summary>
/// Command to apply selected policies to the system
/// </summary>
public sealed class ApplyCommand : CommandBase
{
    public override string CommandType => "Apply";

    /// <summary>
    /// Specific policy IDs to apply (user selection or profile)
    /// </summary>
    public required string[] PolicyIds { get; init; }

    /// <summary>
    /// Create Windows restore point before applying
    /// </summary>
    public bool CreateRestorePoint { get; init; } = true;

    /// <summary>
    /// Dry-run mode (simulate but don't actually apply)
    /// </summary>
    public bool DryRun { get; init; } = false;

    /// <summary>
    /// Profile name (if applying a profile)
    /// </summary>
    public string? ProfileName { get; init; }

    /// <summary>
    /// Optional configuration overrides map.
    /// Key: PolicyId
    /// Value: JSON string representing the configuration (deserialized by service)
    /// </summary>
    public Dictionary<string, string>? ConfigurationOverrides { get; init; }

    /// <summary>
    /// Continue on error or stop at first failure
    /// </summary>
    public bool ContinueOnError { get; init; } = false;
}
