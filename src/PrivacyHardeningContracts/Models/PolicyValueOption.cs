namespace PrivacyHardeningContracts.Models;

/// <summary>
/// Represents a single selectable value option for parameterized policies.
/// Enables granular user control by exposing all possible values with detailed descriptions.
/// </summary>
public sealed record PolicyValueOption
{
    /// <summary>
    /// The actual value to be applied (can be int, string, bool, etc.)
    /// </summary>
    public required object Value { get; init; }

    /// <summary>
    /// User-friendly label for this option (e.g., "Basic", "Enhanced", "Full")
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// Detailed description explaining what this option does
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Optional SKU or build requirements for this option
    /// (e.g., "Security" level only available on Enterprise/Education)
    /// </summary>
    public string[]? Requirements { get; init; }

    /// <summary>
    /// Whether this option is recommended for privacy-focused users
    /// (Note: User still must choose, this is informational only)
    /// </summary>
    public bool RecommendedForPrivacy { get; init; }

    /// <summary>
    /// Known breakage or limitations when selecting this option
    /// </summary>
    public string? KnownLimitations { get; init; }
}
