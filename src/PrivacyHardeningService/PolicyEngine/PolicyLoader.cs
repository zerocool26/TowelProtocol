using Microsoft.Extensions.Logging;
using PrivacyHardeningContracts.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PrivacyHardeningService.PolicyEngine;

/// <summary>
/// Loads policy definitions from YAML files
/// </summary>
public sealed class PolicyLoader
{
    private readonly ILogger<PolicyLoader> _logger;
    private readonly IDeserializer _yamlDeserializer;
    private readonly string _policyDirectory;

    public PolicyLoader(ILogger<PolicyLoader> logger)
    {
        _logger = logger;

        // Enhanced deserializer with support for granular control models
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        // Look for policies directory relative to the executable
        var baseDir = AppContext.BaseDirectory;
        var projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        _policyDirectory = Path.Combine(projectRoot, "policies");

        // Fallback to CommonApplicationData if project directory not found
        if (!Directory.Exists(_policyDirectory))
        {
            _policyDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "PrivacyHardeningFramework",
                "policies");
        }

        _logger.LogInformation("Policy directory set to: {Directory}", _policyDirectory);
    }

    public async Task<PolicyDefinition[]> LoadAllPoliciesAsync(CancellationToken cancellationToken)
    {
        var policies = new List<PolicyDefinition>();

        if (!Directory.Exists(_policyDirectory))
        {
            _logger.LogWarning("Policy directory does not exist: {Directory}", _policyDirectory);
            return Array.Empty<PolicyDefinition>();
        }

        // Recursively find all YAML files
        var yamlFiles = Directory.EnumerateFiles(_policyDirectory, "*.yaml", SearchOption.AllDirectories);

        foreach (var file in yamlFiles)
        {
            try
            {
                var yaml = await File.ReadAllTextAsync(file, cancellationToken);
                var policy = _yamlDeserializer.Deserialize<PolicyDefinition>(yaml);

                if (policy != null)
                {
                    policies.Add(policy);
                    _logger.LogDebug("Loaded policy: {PolicyId} from {File}", policy.PolicyId, file);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load policy from {File}", file);
            }
        }

        _logger.LogInformation("Loaded {Count} policies", policies.Count);
        return policies.ToArray();
    }

    /// <summary>
    /// Validates that a policy meets granular control requirements
    /// </summary>
    public bool ValidateGranularControlPolicy(PolicyDefinition policy)
    {
        var isValid = true;

        // MANDATORY: AutoApply must be false for user control
        if (policy.AutoApply)
        {
            _logger.LogWarning(
                "Policy {PolicyId} violates granular control principle: AutoApply is true (must be false)",
                policy.PolicyId);
            isValid = false;
        }

        // MANDATORY: RequiresConfirmation should be true for user control
        if (!policy.RequiresConfirmation)
        {
            _logger.LogWarning(
                "Policy {PolicyId}: RequiresConfirmation is false (recommended: true for user control)",
                policy.PolicyId);
        }

        // MANDATORY: ShowInUI should be true for user visibility
        if (!policy.ShowInUI)
        {
            _logger.LogWarning(
                "Policy {PolicyId}: ShowInUI is false (user cannot see this policy)",
                policy.PolicyId);
        }

        // Check for proper granular control configuration
        var hasGranularControl =
            policy.AllowedValues?.Length > 0 ||
            policy.ServiceConfigOptions != null ||
            policy.TaskConfigOptions != null ||
            policy.FirewallEndpoint != null;

        if (hasGranularControl)
        {
            _logger.LogDebug(
                "Policy {PolicyId} has granular control features configured",
                policy.PolicyId);
        }

        return isValid;
    }

    /// <summary>
    /// Gets diagnostic information about loaded policies
    /// </summary>
    public PolicyLoadDiagnostics GetDiagnostics(PolicyDefinition[] policies)
    {
        var diagnostics = new PolicyLoadDiagnostics
        {
            TotalPolicies = policies.Length,
            ParameterizedPolicies = policies.Count(p => p.AllowedValues?.Length > 0),
            ServicePolicies = policies.Count(p => p.ServiceConfigOptions != null),
            TaskPolicies = policies.Count(p => p.TaskConfigOptions != null),
            FirewallPolicies = policies.Count(p => p.FirewallEndpoint != null),
            AutoApplyPolicies = policies.Count(p => p.AutoApply),
            UserChoiceRequired = policies.Count(p => p.UserMustChoose),
            PoliciesWithDependencies = policies.Count(p => p.Dependencies.Length > 0)
        };

        return diagnostics;
    }
}

/// <summary>
/// Diagnostic information about loaded policies
/// </summary>
public sealed record PolicyLoadDiagnostics
{
    public int TotalPolicies { get; init; }
    public int ParameterizedPolicies { get; init; }
    public int ServicePolicies { get; init; }
    public int TaskPolicies { get; init; }
    public int FirewallPolicies { get; init; }
    public int AutoApplyPolicies { get; init; }
    public int UserChoiceRequired { get; init; }
    public int PoliciesWithDependencies { get; init; }
}
