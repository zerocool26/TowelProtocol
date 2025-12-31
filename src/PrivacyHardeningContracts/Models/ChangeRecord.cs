namespace PrivacyHardeningContracts.Models;

/// <summary>
/// Records a single system change made by the service
/// </summary>
public sealed class ChangeRecord
{
    /// <summary>
    /// Unique change ID
    /// </summary>
    public required string ChangeId { get; init; }

    /// <summary>
    /// Policy that caused this change
    /// </summary>
    public required string PolicyId { get; init; }

    /// <summary>
    /// Timestamp when change was applied
    /// </summary>
    public required DateTime AppliedAt { get; init; }

    /// <summary>
    /// Mechanism used
    /// </summary>
    public required MechanismType Mechanism { get; init; }

    /// <summary>
    /// Description of the change
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Previous state (for rollback)
    /// </summary>
    public string? PreviousState { get; init; }

    /// <summary>
    /// New state
    /// </summary>
    public required string NewState { get; init; }

    /// <summary>
    /// Whether this change was successful
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error message if unsuccessful
    /// </summary>
    public string? ErrorMessage { get; init; }
}
