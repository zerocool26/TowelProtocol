using Microsoft.Extensions.Logging;
using PrivacyHardeningContracts.Responses;
using PrivacyHardeningService.Executors;
using PrivacyHardeningService.PolicyEngine;
using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningService.StateManager;

/// <summary>
/// Detects configuration drift after Windows updates
/// </summary>
public sealed class DriftDetector
{
    private readonly ILogger<DriftDetector> _logger;
    private readonly IExecutorFactory _executorFactory;

    public DriftDetector(
        ILogger<DriftDetector> logger,
        IExecutorFactory executorFactory)
    {
        _logger = logger;
        _executorFactory = executorFactory;
    }

    /// <summary>
    /// Checks if the policies that claimed to be applied in the snapshot are still applied according to current definitions.
    /// </summary>
    /// <param name="expectedStates">Snapshot states from DB</param>
    /// <param name="resolvedPolicies">Full list of policies with effective configuration overrides applied</param>
    public async Task<List<DriftItem>> DetectDriftAsync(
        IEnumerable<SnapshotPolicyState> expectedStates, 
        IEnumerable<PolicyDefinition> resolvedPolicies,
        CancellationToken cancellationToken)
    {
        var driftItems = new List<DriftItem>();
        var policyMap = resolvedPolicies.ToDictionary(p => p.PolicyId, StringComparer.OrdinalIgnoreCase);

        foreach (var state in expectedStates)
        {
            if (!state.IsApplied) continue; // We only care if applied policies are NO LONGER applied

            if (!policyMap.TryGetValue(state.PolicyId, out var policy))
            {
                // If policy no longer exists in current definitions, we can't check drift properly.
                // We could flag it as "Unknown Policy", but for now just skip.
                continue;
            }

            var executor = _executorFactory.GetExecutor(policy.Mechanism);
            if (executor == null)
            {
                _logger.LogWarning("Drift detection: No executor for mechanism {Mechanism}", policy.Mechanism);
                continue;
            }

            try
            {
                var isCurrentlyApplied = await executor.IsAppliedAsync(policy, cancellationToken);
                if (!isCurrentlyApplied)
                {
                    var currentValue = await executor.GetCurrentValueAsync(policy, cancellationToken);
                    
                    driftItems.Add(new DriftItem
                    {
                        PolicyId = policy.PolicyId,
                        PolicyName = policy.Name,
                        ExpectedValue = state.CurrentValue ?? "Applied",
                        CurrentValue = currentValue ?? "Not Applied",
                        DriftReason = "Value Mismatch"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking drift for policy {PolicyId}", policy.PolicyId);
            }
        }

        return driftItems;
    }
}
