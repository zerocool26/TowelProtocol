namespace PrivacyHardeningContracts.Models;

/// <summary>
/// Describes a known breakage scenario when a policy is applied
/// </summary>
public sealed class BreakageScenario
{
    /// <summary>
    /// Description of what breaks
    /// </summary>
    public required string Scenario { get; init; }

    /// <summary>
    /// Severity of the breakage
    /// </summary>
    public required RiskLevel Severity { get; init; }

    /// <summary>
    /// Additional details or workarounds
    /// </summary>
    public string? Details { get; init; }
}
