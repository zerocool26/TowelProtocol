using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PrivacyHardeningService.PolicyEngine;
using Xunit;

namespace PrivacyHardeningService.Tests.PolicyEngine;

public class PolicyIntegrationTests
{
    [Fact]
    public async Task LoadAllPoliciesAsync_ShouldLoadPoliciesFromDisk_AndTheyShouldBeValid()
    {
        // Arrange
        var resolver = new DependencyResolver(NullLogger<DependencyResolver>.Instance);
        
        // This constructor attempts to locate the real policies folder from the codebase
        using var loader = new PolicyLoader(NullLogger<PolicyLoader>.Instance, resolver);

        // Act
        var policies = await loader.LoadAllPoliciesAsync(CancellationToken.None);

        // Assert
        policies.Should().NotBeEmpty("Policies should be loaded from the 'policies' folder");
        
        foreach (var policy in policies)
        {
            policy.PolicyId.Should().NotBeNullOrWhiteSpace();
            policy.Name.Should().NotBeNullOrWhiteSpace();
            policy.Mechanism.Should().BeDefined();
            policy.Category.Should().BeDefined();
            
            // Check specific types for Firewall if present
            if (policy.Mechanism == PrivacyHardeningContracts.Models.MechanismType.Firewall)
            {
               // Just ensure it loaded without error
            }
        }
    }
}
