namespace PrivacyHardeningContracts.Models;

/// <summary>
/// Defines which Windows builds and SKUs a policy applies to
/// </summary>
public sealed class PolicyApplicability
{
    /// <summary>
    /// Minimum Windows build number (e.g., 22000 for Windows 11 21H2)
    /// </summary>
    public int? MinBuild { get; init; }

    /// <summary>
    /// Maximum Windows build number (null = no upper limit)
    /// </summary>
    public int? MaxBuild { get; init; }

    /// <summary>
    /// Supported Windows SKUs
    /// </summary>
    public required string[] SupportedSkus { get; init; }

    /// <summary>
    /// Explicitly excluded SKUs
    /// </summary>
    public string[] ExcludedSkus { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Special device requirements (e.g., "Copilot+ PC")
    /// </summary>
    public string? DeviceRequirement { get; init; }

    /// <summary>
    /// Deprecated as of this build number
    /// </summary>
    public int? DeprecatedAsOfBuild { get; init; }
}
