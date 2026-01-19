using System.Text.Json.Serialization;

namespace PrivacyHardeningContracts.Models;

/// <summary>
/// Service-side configuration settings
/// </summary>
public sealed class ServiceConfiguration
{
    /// <summary>
    /// How frequently to run background drift detection (in minutes). 0 = Disabled.
    /// </summary>
    public int DriftCheckIntervalMinutes { get; set; } = 0;

    /// <summary>
    /// Whether to automatically remediate drift when detected (Aggressive)
    /// </summary>
    public bool AutoRemediateDrift { get; set; } = false;

    /// <summary>
    /// Whether to log detailed evidence for audits
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = true;
}
