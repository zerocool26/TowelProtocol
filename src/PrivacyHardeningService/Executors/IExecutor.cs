using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningService.Executors;

/// <summary>
/// Interface for policy execution mechanisms
/// </summary>
public interface IExecutor
{
    /// <summary>
    /// Mechanism type this executor handles
    /// </summary>
    MechanismType MechanismType { get; }

    /// <summary>
    /// Check if a policy is currently applied
    /// </summary>
    Task<bool> IsAppliedAsync(PolicyDefinition policy, CancellationToken cancellationToken);

    /// <summary>
    /// Get current value for comparison
    /// </summary>
    Task<string?> GetCurrentValueAsync(PolicyDefinition policy, CancellationToken cancellationToken);

    /// <summary>
    /// Apply a policy
    /// </summary>
    Task<ChangeRecord> ApplyAsync(PolicyDefinition policy, CancellationToken cancellationToken);

    /// <summary>
    /// Revert a policy
    /// </summary>
    Task<ChangeRecord> RevertAsync(PolicyDefinition policy, ChangeRecord originalChange, CancellationToken cancellationToken);
}
