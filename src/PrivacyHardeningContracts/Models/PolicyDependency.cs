namespace PrivacyHardeningContracts.Models;

/// <summary>
/// Represents a dependency between policies with user override capabilities.
/// Ensures user has full visibility and control over policy relationships.
/// </summary>
public sealed record PolicyDependency
{
    /// <summary>
    /// ID of the policy this one depends on
    /// </summary>
    public required string PolicyId { get; init; }

    /// <summary>
    /// Human-readable reason for the dependency
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Type of dependency
    /// </summary>
    public required DependencyType Type { get; init; }

    /// <summary>
    /// Whether user can override this dependency (skip it if they understand the risk)
    /// </summary>
    public bool UserCanOverride { get; init; }

    /// <summary>
    /// Warning message shown if user attempts to override
    /// </summary>
    public string? OverrideWarning { get; init; }

    /// <summary>
    /// Whether this is optional (recommended but not required)
    /// </summary>
    public bool Optional { get; init; }

    /// <summary>
    /// Auto-select this dependency by default (user can still deselect)
    /// Note: Even with auto-select, user must review and confirm
    /// </summary>
    public bool AutoSelect { get; init; }
}

/// <summary>
/// Type of dependency relationship
/// </summary>
public enum DependencyType
{
    /// <summary>
    /// Required for functionality - policy won't work correctly without it
    /// </summary>
    Required,

    /// <summary>
    /// Recommended for best results but not strictly required
    /// </summary>
    Recommended,

    /// <summary>
    /// Conflict - these policies should not be applied together
    /// </summary>
    Conflict,

    /// <summary>
    /// Prerequisite - must be applied before this policy
    /// </summary>
    Prerequisite,

    /// <summary>
    /// Related - policies in the same area (informational or loosely linked)
    /// </summary>
    Related,

    /// <summary>
    /// Complementary - recommended pairing for completeness (but not required)
    /// </summary>
    Complementary
}
