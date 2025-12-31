namespace PrivacyHardeningContracts.Models;

/// <summary>
/// Mechanism used to enforce a policy
/// </summary>
public enum MechanismType
{
    /// <summary>
    /// Group Policy Object (lgpo.exe or native API)
    /// </summary>
    GroupPolicy,

    /// <summary>
    /// Direct registry modification (fallback or unsupported)
    /// </summary>
    Registry,

    /// <summary>
    /// MDM/CSP (Mobile Device Management Configuration Service Provider)
    /// </summary>
    MDM,

    /// <summary>
    /// Windows Service configuration (startup type, disabled)
    /// </summary>
    Service,

    /// <summary>
    /// Scheduled Task modification
    /// </summary>
    ScheduledTask,

    /// <summary>
    /// Windows Firewall rule
    /// </summary>
    Firewall,

    /// <summary>
    /// Signed PowerShell script execution
    /// </summary>
    PowerShell,

    /// <summary>
    /// Hosts file modification (last resort)
    /// </summary>
    HostsFile,

    /// <summary>
    /// WFP (Windows Filtering Platform) callout driver (future)
    /// </summary>
    WFPDriver
}
