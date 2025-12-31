namespace PrivacyHardeningContracts.Models;

/// <summary>
/// Granular configuration options for Windows services.
/// Allows user to control each aspect independently rather than simple enable/disable.
/// </summary>
public sealed record ServiceConfigOptions
{
    /// <summary>
    /// Service startup type configuration
    /// </summary>
    public SelectableOption<string>? StartupType { get; init; }

    /// <summary>
    /// Action to take on the running service
    /// </summary>
    public SelectableOption<string>? ServiceAction { get; init; }

    /// <summary>
    /// Service recovery options (what happens on failure)
    /// </summary>
    public SelectableOption<string>? RecoveryOptions { get; init; }

    /// <summary>
    /// Service dependencies handling
    /// </summary>
    public SelectableOption<string>? DependencyHandling { get; init; }

    /// <summary>
    /// Whether to modify service security settings
    /// </summary>
    public bool ModifyServiceSecurity { get; init; }

    /// <summary>
    /// Service security configuration (if ModifyServiceSecurity is true)
    /// </summary>
    public ServiceSecurityConfig? SecurityConfig { get; init; }
}

/// <summary>
/// Service security configuration
/// </summary>
public sealed record ServiceSecurityConfig
{
    /// <summary>
    /// Whether to prevent service from being started manually
    /// </summary>
    public bool PreventManualStart { get; init; }

    /// <summary>
    /// Whether to modify service permissions
    /// </summary>
    public bool ModifyPermissions { get; init; }

    /// <summary>
    /// User-friendly description of security changes
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Requires explicit user confirmation
    /// </summary>
    public bool RequiresConfirmation { get; init; } = true;
}
