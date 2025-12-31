namespace PrivacyHardeningContracts.Models;

/// <summary>
/// Represents a single network endpoint that can be blocked via firewall.
/// Provides granular per-endpoint control rather than bulk blocking.
/// </summary>
public sealed record FirewallEndpoint
{
    /// <summary>
    /// Hostname or IP address to block
    /// </summary>
    public required string Hostname { get; init; }

    /// <summary>
    /// User-friendly description of what this endpoint is used for
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Criticality level (e.g., "Non-essential", "Breaks settings sync", "Critical for updates")
    /// </summary>
    public required string Criticality { get; init; }

    /// <summary>
    /// Known breakage when blocking this endpoint
    /// </summary>
    public required string KnownBreakage { get; init; }

    /// <summary>
    /// Whether user can select/deselect this endpoint individually
    /// </summary>
    public bool UserSelectable { get; init; } = true;

    /// <summary>
    /// Whether this endpoint is blocked by default in profiles
    /// Per granular control requirements: ALWAYS false - user chooses
    /// </summary>
    public bool EnabledByDefault { get; init; } = false;

    /// <summary>
    /// Port number to block (null = all ports)
    /// </summary>
    public int? Port { get; init; }

    /// <summary>
    /// Protocol (TCP, UDP, or Both)
    /// </summary>
    public string Protocol { get; init; } = "Both";

    /// <summary>
    /// Direction (Inbound, Outbound, or Both)
    /// </summary>
    public string Direction { get; init; } = "Outbound";

    /// <summary>
    /// Category of endpoint (e.g., "Telemetry", "Diagnostics", "Cloud Services")
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Microsoft documentation or reference for this endpoint
    /// </summary>
    public string? Reference { get; init; }
}
