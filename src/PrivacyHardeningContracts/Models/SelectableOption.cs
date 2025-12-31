namespace PrivacyHardeningContracts.Models;

/// <summary>
/// Generic selectable option structure for service/task configuration.
/// Provides user with full control over each configurable aspect.
/// </summary>
/// <typeparam name="T">Type of the option value (string, int, enum, etc.)</typeparam>
public sealed record SelectableOption<T>
{
    /// <summary>
    /// Whether the user can select from multiple options
    /// </summary>
    public required bool UserSelectable { get; init; }

    /// <summary>
    /// Available options for user to choose from
    /// </summary>
    public required OptionChoice<T>[] Options { get; init; }

    /// <summary>
    /// Current value in the system (for comparison)
    /// </summary>
    public T? CurrentValue { get; init; }

    /// <summary>
    /// Recommended value (informational only - user decides)
    /// </summary>
    public T? RecommendedValue { get; init; }

    /// <summary>
    /// Whether user MUST make a choice (cannot skip)
    /// </summary>
    public bool UserMustChoose { get; init; }

    /// <summary>
    /// Help text explaining what this option controls
    /// </summary>
    public string? HelpText { get; init; }
}

/// <summary>
/// A single choice within a selectable option
/// </summary>
public sealed record OptionChoice<T>
{
    /// <summary>
    /// The value to apply
    /// </summary>
    public required T Value { get; init; }

    /// <summary>
    /// User-friendly label
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// Detailed description of what this choice does
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Whether this choice is reversible
    /// </summary>
    public bool Reversible { get; init; } = true;

    /// <summary>
    /// Requires additional user confirmation (for dangerous options)
    /// </summary>
    public bool RequiresConfirmation { get; init; }

    /// <summary>
    /// Warning message if this option requires confirmation
    /// </summary>
    public string? ConfirmationWarning { get; init; }
}
