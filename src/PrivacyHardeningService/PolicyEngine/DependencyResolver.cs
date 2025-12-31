using Microsoft.Extensions.Logging;
using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningService.PolicyEngine;

/// <summary>
/// Resolves policy dependencies and determines application order
/// </summary>
public sealed class DependencyResolver
{
    private readonly ILogger<DependencyResolver> _logger;

    public DependencyResolver(ILogger<DependencyResolver> logger)
    {
        _logger = logger;
    }

    public PolicyDefinition[] ResolveDependencies(PolicyDefinition[] policies, string[] requestedPolicyIds)
    {
        var policyMap = policies.ToDictionary(p => p.PolicyId);
        var resolved = new List<PolicyDefinition>();
        var visiting = new HashSet<string>();
        var visited = new HashSet<string>();

        foreach (var policyId in requestedPolicyIds)
        {
            if (!policyMap.ContainsKey(policyId))
            {
                _logger.LogWarning("Policy not found: {PolicyId}", policyId);
                continue;
            }

            Visit(policyMap[policyId], policyMap, resolved, visiting, visited);
        }

        return resolved.ToArray();
    }

    private void Visit(
        PolicyDefinition policy,
        Dictionary<string, PolicyDefinition> policyMap,
        List<PolicyDefinition> resolved,
        HashSet<string> visiting,
        HashSet<string> visited)
    {
        if (visited.Contains(policy.PolicyId))
        {
            return;
        }

        if (visiting.Contains(policy.PolicyId))
        {
            _logger.LogError("Circular dependency detected for policy: {PolicyId}", policy.PolicyId);
            throw new InvalidOperationException($"Circular dependency detected: {policy.PolicyId}");
        }

        visiting.Add(policy.PolicyId);

        // Visit dependencies first
        foreach (var dependency in policy.Dependencies)
        {
            // Extract policy ID from PolicyDependency object
            var depId = dependency.PolicyId;

            if (policyMap.TryGetValue(depId, out var depPolicy))
            {
                // Check if dependency is required or can be skipped
                if (dependency.Type == DependencyType.Required ||
                    dependency.Type == DependencyType.Prerequisite)
                {
                    Visit(depPolicy, policyMap, resolved, visiting, visited);
                }
                else if (dependency.Type == DependencyType.Recommended)
                {
                    // For recommended dependencies, visit if user hasn't overridden
                    if (!dependency.UserCanOverride)
                    {
                        Visit(depPolicy, policyMap, resolved, visiting, visited);
                    }
                    else
                    {
                        _logger.LogDebug(
                            "Recommended dependency {DependencyId} can be overridden by user for {PolicyId}",
                            depId, policy.PolicyId);
                    }
                }
                else if (dependency.Type == DependencyType.Conflict)
                {
                    _logger.LogWarning(
                        "Conflict detected: {PolicyId} conflicts with {DependencyId}",
                        policy.PolicyId, depId);
                }
            }
            else
            {
                _logger.LogWarning("Dependency not found: {PolicyId} -> {DependencyId} (Type: {Type})",
                    policy.PolicyId, depId, dependency.Type);
            }
        }

        visiting.Remove(policy.PolicyId);
        visited.Add(policy.PolicyId);
        resolved.Add(policy);
    }
}
