using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningContracts.Responses;

/// <summary>
/// Result containing available policy definitions
/// </summary>
public sealed class GetPoliciesResult : ResponseBase
{
    /// <summary>
    /// All available policies
    /// </summary>
    public required PolicyDefinition[] Policies { get; init; }

    /// <summary>
    /// Policy manifest version
    /// </summary>
    public required string ManifestVersion { get; init; }

    /// <summary>
    /// When policies were last updated
    /// </summary>
    public required DateTime LastUpdated { get; init; }
}
