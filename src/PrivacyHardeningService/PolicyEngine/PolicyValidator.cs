using Microsoft.Extensions.Logging;
using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningService.PolicyEngine;

/// <summary>
/// Validates policy definitions and signatures
/// </summary>
public sealed class PolicyValidator
{
    private readonly ILogger<PolicyValidator> _logger;

    public PolicyValidator(ILogger<PolicyValidator> logger)
    {
        _logger = logger;
    }

    public bool ValidatePolicy(PolicyDefinition policy)
    {
        // TODO: Implement signature verification
        // TODO: Validate policy schema

        if (string.IsNullOrWhiteSpace(policy.PolicyId))
        {
            _logger.LogWarning("Policy missing PolicyId");
            return false;
        }

        if (string.IsNullOrWhiteSpace(policy.Name))
        {
            _logger.LogWarning("Policy {PolicyId} missing Name", policy.PolicyId);
            return false;
        }

        return true;
    }
}
