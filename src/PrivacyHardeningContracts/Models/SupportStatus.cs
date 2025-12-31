namespace PrivacyHardeningContracts.Models;

/// <summary>
/// Whether the policy mechanism is officially supported by Microsoft
/// </summary>
public enum SupportStatus
{
    /// <summary>
    /// Documented and supported by Microsoft
    /// </summary>
    Supported,

    /// <summary>
    /// Works but not officially documented
    /// </summary>
    Undocumented,

    /// <summary>
    /// Explicitly unsupported, may break in updates
    /// </summary>
    Unsupported,

    /// <summary>
    /// Deprecated by Microsoft, scheduled for removal
    /// </summary>
    Deprecated
}
