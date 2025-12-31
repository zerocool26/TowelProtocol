using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using PrivacyHardeningContracts.Models;
using PrivacyHardeningService.PolicyEngine;

namespace PrivacyHardeningTests;

/// <summary>
/// Integration tests that load and validate real policy files
/// </summary>
public class PolicyIntegrationTests
{
    private readonly PolicyLoader _loader;
    private readonly PolicyValidator _validator;

    public PolicyIntegrationTests()
    {
        _loader = new PolicyLoader(NullLogger<PolicyLoader>.Instance);
        _validator = new PolicyValidator(NullLogger<PolicyValidator>.Instance);
    }

    [Fact]
    public async Task RealPolicies_AllLoadSuccessfully()
    {
        // Act
        var policies = await _loader.LoadAllPoliciesAsync(CancellationToken.None);

        // Assert
        Assert.NotEmpty(policies);
        Assert.True(policies.Length >= 89, $"Expected at least 89 policies, found {policies.Length}");
    }

    [Fact]
    public async Task RealPolicies_AllPassValidation()
    {
        // Arrange
        var policies = await _loader.LoadAllPoliciesAsync(CancellationToken.None);

        // Act & Assert
        foreach (var policy in policies)
        {
            var isValid = _validator.ValidatePolicy(policy);
            Assert.True(isValid, $"Policy {policy.PolicyId} failed validation");
        }
    }

    [Fact]
    public async Task RealPolicies_AllFollowGranularControl()
    {
        // Arrange
        var policies = await _loader.LoadAllPoliciesAsync(CancellationToken.None);

        // Act & Assert
        foreach (var policy in policies)
        {
            // Validate granular control compliance
            var isValid = _loader.ValidateGranularControlPolicy(policy);
            Assert.True(isValid, $"Policy {policy.PolicyId} violates granular control principles");

            // Explicit checks
            Assert.False(policy.AutoApply, $"Policy {policy.PolicyId} has AutoApply=true");
            Assert.True(policy.RequiresConfirmation, $"Policy {policy.PolicyId} doesn't require confirmation");
            Assert.True(policy.ShowInUI, $"Policy {policy.PolicyId} is hidden from UI");
        }
    }

    [Fact]
    public async Task RealPolicies_CriticalOnesHaveUserMustChoose()
    {
        // Arrange
        var policies = await _loader.LoadAllPoliciesAsync(CancellationToken.None);
        var criticalPolicies = policies.Where(p => p.RiskLevel == RiskLevel.Critical).ToArray();

        // Assert
        Assert.NotEmpty(criticalPolicies); // We should have some critical policies

        foreach (var policy in criticalPolicies)
        {
            Assert.True(policy.UserMustChoose,
                $"Critical policy {policy.PolicyId} should have UserMustChoose=true");
            Assert.False(string.IsNullOrWhiteSpace(policy.HelpText),
                $"Critical policy {policy.PolicyId} should have HelpText");
        }
    }

    [Fact]
    public async Task RealPolicies_RecallPolicyIsCritical()
    {
        // Arrange
        var policies = await _loader.LoadAllPoliciesAsync(CancellationToken.None);
        var recallPolicy = policies.FirstOrDefault(p => p.PolicyId == "cp-002");

        // Assert - Recall is one of the most invasive features
        Assert.NotNull(recallPolicy);
        Assert.Equal(RiskLevel.Critical, recallPolicy.RiskLevel);
        Assert.True(recallPolicy.UserMustChoose);
        Assert.Contains("Recall", recallPolicy.Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RealPolicies_DefenderPoliciesExist()
    {
        // Arrange
        var policies = await _loader.LoadAllPoliciesAsync(CancellationToken.None);
        var defenderPolicies = policies.Where(p => p.PolicyId.StartsWith("def-")).ToArray();

        // Assert - We created 8 defender policies (def-001 through def-008)
        Assert.True(defenderPolicies.Length >= 8,
            $"Expected at least 8 Defender policies, found {defenderPolicies.Length}");

        // Check specific critical defender policies
        var behaviorMonitoring = policies.FirstOrDefault(p => p.PolicyId == "def-005");
        var realtimeMonitoring = policies.FirstOrDefault(p => p.PolicyId == "def-006");

        Assert.NotNull(behaviorMonitoring);
        Assert.NotNull(realtimeMonitoring);
        Assert.Equal(RiskLevel.Critical, behaviorMonitoring.RiskLevel);
        Assert.Equal(RiskLevel.Critical, realtimeMonitoring.RiskLevel);
    }

    [Fact]
    public async Task RealPolicies_EdgePoliciesExist()
    {
        // Arrange
        var policies = await _loader.LoadAllPoliciesAsync(CancellationToken.None);
        var edgePolicies = policies.Where(p => p.PolicyId.StartsWith("edge-")).ToArray();

        // Assert - We created 6 Edge policies
        Assert.True(edgePolicies.Length >= 6,
            $"Expected at least 6 Edge policies, found {edgePolicies.Length}");

        // All Edge policies should be registry-based
        foreach (var policy in edgePolicies)
        {
            Assert.True(
                policy.Mechanism == MechanismType.Registry ||
                policy.Mechanism == MechanismType.GroupPolicy,
                $"Edge policy {policy.PolicyId} should use Registry or GroupPolicy mechanism");
        }
    }

    [Fact]
    public async Task RealPolicies_AllCategoriesRepresented()
    {
        // Arrange
        var policies = await _loader.LoadAllPoliciesAsync(CancellationToken.None);

        // Act - Group by category
        var categories = policies.GroupBy(p => p.Category).ToArray();

        // Assert - We should have at least 5 categories
        Assert.True(categories.Length >= 5,
            $"Expected at least 5 categories, found {categories.Length}");

        // Verify specific categories exist
        var telemetryPolicies = policies.Where(p => p.Category == PolicyCategory.Telemetry);
        var privacyPolicies = policies.Where(p => p.Category == PolicyCategory.Privacy);

        Assert.NotEmpty(telemetryPolicies);
        Assert.NotEmpty(privacyPolicies);
    }

    [Fact]
    public async Task RealPolicies_ParameterizedPoliciesHaveValidOptions()
    {
        // Arrange
        var policies = await _loader.LoadAllPoliciesAsync(CancellationToken.None);
        var parameterizedPolicies = policies.Where(p => p.AllowedValues != null && p.AllowedValues.Length > 0).ToArray();

        // Assert - We should have at least 6 parameterized policies
        Assert.True(parameterizedPolicies.Length >= 6,
            $"Expected at least 6 parameterized policies, found {parameterizedPolicies.Length}");

        foreach (var policy in parameterizedPolicies)
        {
            Assert.True(policy.AllowedValues.Length >= 2,
                $"Parameterized policy {policy.PolicyId} should have at least 2 options");

            foreach (var option in policy.AllowedValues)
            {
                Assert.False(string.IsNullOrWhiteSpace(option.Label),
                    $"Policy {policy.PolicyId} has option with empty label");
                Assert.False(string.IsNullOrWhiteSpace(option.Description),
                    $"Policy {policy.PolicyId} has option with empty description");
            }
        }
    }

    [Fact]
    public async Task RealPolicies_DiagnosticsShowZeroAutoApply()
    {
        // Arrange
        var policies = await _loader.LoadAllPoliciesAsync(CancellationToken.None);

        // Act
        var diagnostics = _loader.GetDiagnostics(policies);

        // Assert - ABSOLUTE REQUIREMENT
        Assert.Equal(0, diagnostics.AutoApplyPolicies);
        Assert.True(diagnostics.TotalPolicies >= 89);
        Assert.True(diagnostics.ParameterizedPolicies >= 6);
    }

    [Fact]
    public async Task RealPolicies_AllHaveNonEmptyPolicyIds()
    {
        // Arrange
        var policies = await _loader.LoadAllPoliciesAsync(CancellationToken.None);

        // Act & Assert
        var policyIds = policies.Select(p => p.PolicyId).ToArray();

        // No duplicates
        Assert.Equal(policyIds.Length, policyIds.Distinct().Count());

        // All follow naming convention
        foreach (var policyId in policyIds)
        {
            Assert.Matches(@"^[a-z]+-\d{3}$", policyId);
        }
    }

    [Fact]
    public async Task RealPolicies_AllHaveValidApplicability()
    {
        // Arrange
        var policies = await _loader.LoadAllPoliciesAsync(CancellationToken.None);

        // Act & Assert
        foreach (var policy in policies)
        {
            Assert.NotNull(policy.Applicability);
            Assert.True(policy.Applicability.MinBuild > 0,
                $"Policy {policy.PolicyId} has invalid MinBuild: {policy.Applicability.MinBuild}");
            Assert.NotNull(policy.Applicability.SupportedSkus);
            Assert.NotEmpty(policy.Applicability.SupportedSkus);
        }
    }

    [Fact]
    public async Task RealPolicies_NoPolicyHasEnablingByDefault()
    {
        // Arrange
        var policies = await _loader.LoadAllPoliciesAsync(CancellationToken.None);

        // Act & Assert - CRITICAL: No policy should enable by default
        foreach (var policy in policies)
        {
            Assert.False(policy.EnabledByDefault,
                $"VIOLATION: Policy {policy.PolicyId} has EnabledByDefault=true");
        }
    }
}
