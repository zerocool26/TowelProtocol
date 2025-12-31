namespace PrivacyHardeningContracts.Commands;

/// <summary>
/// Command to audit current system state against policy definitions
/// (no changes made)
/// </summary>
public sealed class AuditCommand : CommandBase
{
    public override string CommandType => "Audit";

    /// <summary>
    /// Optional: specific policy IDs to audit (null = all)
    /// </summary>
    public string[]? PolicyIds { get; init; }

    /// <summary>
    /// Include detailed comparison data
    /// </summary>
    public bool IncludeDetails { get; init; } = true;
}
