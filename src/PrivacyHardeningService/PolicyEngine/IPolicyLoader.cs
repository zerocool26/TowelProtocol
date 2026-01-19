using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningService.PolicyEngine;

public interface IPolicyLoader
{
    event EventHandler? PoliciesChanged;
    Task<PolicyDefinition[]> LoadAllPoliciesAsync(CancellationToken cancellationToken);
}
