namespace PrivacyHardeningContracts.Models;

/// <summary>
/// Risk level for applying a policy (potential for breakage)
/// </summary>
public enum RiskLevel
{
    /// <summary>
    /// No known breakage, fully supported by Microsoft
    /// </summary>
    Low,

    /// <summary>
    /// May break specific features, supported mechanism
    /// </summary>
    Medium,

    /// <summary>
    /// Likely to break features or unsupported mechanism
    /// </summary>
    High,

    /// <summary>
    /// Experimental or undocumented, may cause system instability
    /// </summary>
    Critical
}
