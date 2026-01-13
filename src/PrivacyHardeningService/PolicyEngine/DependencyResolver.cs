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

    /// <summary>
    /// Validates the entire policy graph for circular dependencies
    /// </summary>
    /// <param name="policies">All known policy definitions</param>
    /// <exception cref="InvalidOperationException">If a circular dependency is detected</exception>
    public void ValidateGraph(PolicyDefinition[] policies)
    {
        var policyMap = policies.ToDictionary(p => p.PolicyId);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();
        var cycles = new List<string>();

        _logger.LogInformation("Validating dependency graph for {Count} policies...", policies.Length);

        foreach (var policy in policies)
        {
            ValidateVisit(policy.PolicyId);
        }

        if (cycles.Count > 0)
        {
            _logger.LogError("Dependency graph validation failed. Found {Count} cycle(s).", cycles.Count);
            throw new InvalidOperationException(
                "Circular dependency detected in policy graph:\n" + string.Join("\n", cycles.Distinct()));
        }

        _logger.LogInformation("Dependency graph validation successful (no cycles found). ");

        void ValidateVisit(string policyId)
        {
            if (visited.Contains(policyId))
            {
                return;
            }

            if (visiting.Contains(policyId))
            {
                var startIndex = stack.IndexOf(policyId);
                if (startIndex >= 0)
                {
                    var cyclePath = stack.Skip(startIndex).Concat(new[] { policyId });
                    cycles.Add(string.Join(" -> ", cyclePath));
                }
                else
                {
                    cycles.Add(policyId);
                }

                return;
            }

            if (!policyMap.TryGetValue(policyId, out var policy))
            {
                // Missing dependency references are handled elsewhere as warnings; not a graph-cycle issue.
                visited.Add(policyId);
                return;
            }

            visiting.Add(policyId);
            stack.Add(policyId);

            foreach (var dependency in policy.Dependencies)
            {
                var depId = dependency.PolicyId;

                // Mirror the same traversal semantics used during apply ordering.
                if (dependency.Type == DependencyType.Required || dependency.Type == DependencyType.Prerequisite)
                {
                    ValidateVisit(depId);
                }
                else if (dependency.Type == DependencyType.Recommended)
                {
                    if (!dependency.UserCanOverride)
                    {
                        ValidateVisit(depId);
                    }
                }
                // Conflicts do not participate in ordering and should not create cycles.
            }

            stack.RemoveAt(stack.Count - 1);
            visiting.Remove(policyId);
            visited.Add(policyId);
        }
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
