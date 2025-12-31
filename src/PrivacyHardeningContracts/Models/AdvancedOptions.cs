namespace PrivacyHardeningContracts.Models;

/// <summary>
/// Advanced options that users can toggle for fine-grained control over policy application.
/// All options default to safe values - user can override with full knowledge of consequences.
/// </summary>
public sealed record AdvancedOptions
{
    /// <summary>
    /// Whether to skip dependency checking.
    /// WARNING: Skipping dependencies may result in incomplete privacy protection or system issues.
    /// </summary>
    public bool SkipDependencyCheck { get; init; }

    /// <summary>
    /// Whether to skip compatibility checking (Windows build, SKU, etc.).
    /// WARNING: May attempt to apply policies that don't work on this system.
    /// </summary>
    public bool SkipCompatibilityCheck { get; init; }

    /// <summary>
    /// Force apply even if policy appears to be already applied.
    /// Useful for re-applying after Windows Update reverted changes.
    /// </summary>
    public bool ForceApply { get; init; }

    /// <summary>
    /// Create a Windows System Restore point before applying changes.
    /// Recommended: true (provides rollback capability)
    /// </summary>
    public bool CreateRestorePoint { get; init; } = true;

    /// <summary>
    /// Logging verbosity level for this operation
    /// </summary>
    public LogVerbosity LogVerbosity { get; init; } = LogVerbosity.Detailed;

    /// <summary>
    /// Dry run mode - show what would be changed without actually changing it
    /// </summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// Create backup of registry keys before modifying
    /// </summary>
    public bool BackupRegistryKeys { get; init; } = true;

    /// <summary>
    /// Export detailed audit log to file
    /// </summary>
    public bool ExportAuditLog { get; init; }

    /// <summary>
    /// Path for audit log export (if ExportAuditLog is true)
    /// </summary>
    public string? AuditLogPath { get; init; }
}

/// <summary>
/// Logging verbosity levels
/// </summary>
public enum LogVerbosity
{
    /// <summary>
    /// Only log errors and critical warnings
    /// </summary>
    Minimal,

    /// <summary>
    /// Log normal operations (apply, revert, errors)
    /// </summary>
    Normal,

    /// <summary>
    /// Log detailed information including before/after values
    /// </summary>
    Detailed,

    /// <summary>
    /// Log everything including debug information
    /// </summary>
    Verbose
}
