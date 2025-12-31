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
/// Tests for PolicyValidator and PolicyLoader - ensures all policies meet quality standards
/// </summary>
public class PolicyValidationTests
{
    private readonly PolicyValidator _validator;
    private readonly PolicyLoader _loader;

    public PolicyValidationTests()
    {
        _validator = new PolicyValidator(NullLogger<PolicyValidator>.Instance);
        _loader = new PolicyLoader(NullLogger<PolicyLoader>.Instance);
    }

    [Fact]
    public async Task AllPolicies_HaveRequiredFields()
    {
        // Arrange
        var policies = await _loader.LoadAllPoliciesAsync(CancellationToken.None);

        // Assert
        Assert.NotEmpty(policies);

        foreach (var policy in policies)
        {
            Assert.False(string.IsNullOrWhiteSpace(policy.PolicyId));
            Assert.False(string.IsNullOrWhiteSpace(policy.Name));
            Assert.False(string.IsNullOrWhiteSpace(policy.Description));
            Assert.True(Enum.IsDefined(typeof(MechanismType), policy.Mechanism));
            Assert.NotNull(policy.MechanismDetails);
        }
    }

    [Fact]
    public async Task AllPolicies_FollowGranularControlPrinciples()
    {
        // Arrange
        var policies = await _loader.LoadAllPoliciesAsync(CancellationToken.None);

        // Assert - All policies must follow granular control principles
        foreach (var policy in policies)
        {
            // Validate using loader's granular control validation
            _loader.ValidateGranularControlPolicy(policy);

            // Must not auto-apply
            Assert.False(policy.AutoApply);

            // Must require confirmation
            Assert.True(policy.RequiresConfirmation);

            // Must be visible in UI
            Assert.True(policy.ShowInUI);
        }
    }

    [Fact]
    public async Task AllPolicies_HaveValidVersionNumbers()
    {
        // Arrange
        var policies = await _loader.LoadAllPoliciesAsync(CancellationToken.None);

        // Assert
        foreach (var policy in policies)
        {
            Assert.False(string.IsNullOrWhiteSpace(policy.Version));
            Assert.Matches(@"^\d+\.\d+\.\d+$", policy.Version);
        }
    }

    [Fact]
    public async Task AllPolicies_HaveValidRiskLevels()
    {
        // Arrange
        var policies = await _loader.LoadAllPoliciesAsync(CancellationToken.None);

        // Assert
        foreach (var policy in policies)
        {
            Assert.True(Enum.IsDefined(typeof(RiskLevel), policy.RiskLevel));
        }
    }

    [Fact]
    public async Task CriticalPolicies_RequireUserChoice()
    {
        // Arrange
        var policies = await _loader.LoadAllPoliciesAsync(CancellationToken.None);
        var criticalPolicies = policies.Where(p => p.RiskLevel == RiskLevel.Critical);

        // Assert
        foreach (var policy in criticalPolicies)
        {
            Assert.True(policy.UserMustChoose);
            Assert.False(string.IsNullOrWhiteSpace(policy.HelpText));
        }
    }

    [Fact]
    public async Task AllPolicies_HaveKnownBreakageDocumented()
    {
        // Arrange
        var policies = await _loader.LoadAllPoliciesAsync(CancellationToken.None);

        // Assert
        foreach (var policy in policies)
        {
            Assert.NotNull(policy.KnownBreakage);
        }
    }

    [Fact]
    public async Task AllPolicies_HaveApplicabilityCriteria()
    {
        // Arrange
        var policies = await _loader.LoadAllPoliciesAsync(CancellationToken.None);

        // Assert
        foreach (var policy in policies)
        {
            Assert.NotNull(policy.Applicability);
            Assert.True(policy.Applicability.MinBuild > 0);
        }
    }

    [Fact]
    public async Task ParameterizedPolicies_HaveAllowedValues()
    {
        // Arrange
        var policies = await _loader.LoadAllPoliciesAsync(CancellationToken.None);
        var parameterizedPolicies = policies.Where(p => p.AllowedValues != null && p.AllowedValues.Length > 0);

        // Assert
        foreach (var policy in parameterizedPolicies)
        {
            Assert.True(policy.AllowedValues.Length >= 2);

            foreach (var value in policy.AllowedValues)
            {
                Assert.False(string.IsNullOrWhiteSpace(value.Label));
                Assert.False(string.IsNullOrWhiteSpace(value.Description));
            }
        }
    }

    [Fact]
    public void ValidatePolicy_ReturnsTrue_ForValidPolicy()
    {
        // Arrange
        var validPolicy = new PolicyDefinition
        {
            PolicyId = "test-001",
            Version = "1.0.0",
            Name = "Test Policy",
            Category = PolicyCategory.Telemetry,
            Description = "Test description",
            Mechanism = MechanismType.Registry,
            MechanismDetails = new { },
            SupportStatus = SupportStatus.Supported,
            RiskLevel = RiskLevel.Low,
            Reversible = true,
            RevertMechanism = "Test revert",
            Applicability = new PolicyApplicability
            {
                MinBuild = 22000,
                SupportedSkus = new[] { "Enterprise", "Pro" }
            },
            Dependencies = Array.Empty<PolicyDependency>(),
            KnownBreakage = Array.Empty<BreakageScenario>(),
            Tags = new[] { "test" },
            EnabledByDefault = false,
            AutoApply = false,
            RequiresConfirmation = true,
            ShowInUI = true
        };

        // Act
        var result = _validator.ValidatePolicy(validPolicy);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidatePolicy_ReturnsFalse_ForInvalidPolicy()
    {
        // Arrange
        var invalidPolicy = new PolicyDefinition
        {
            PolicyId = "", // Invalid - empty
            Version = "1.0.0",
            Name = "", // Invalid - empty
            Category = PolicyCategory.Telemetry,
            Description = "Test",
            Mechanism = MechanismType.Registry,
            MechanismDetails = new { },
            SupportStatus = SupportStatus.Supported,
            RiskLevel = RiskLevel.Low,
            Reversible = true,
            RevertMechanism = "Test",
            Applicability = new PolicyApplicability { MinBuild = 22000, SupportedSkus = new[] { "Pro" } },
            Dependencies = Array.Empty<PolicyDependency>(),
            KnownBreakage = Array.Empty<BreakageScenario>(),
            Tags = Array.Empty<string>(),
            EnabledByDefault = false,
            AutoApply = false,
            RequiresConfirmation = true,
            ShowInUI = true
        };

        // Act
        var result = _validator.ValidatePolicy(invalidPolicy);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task PolicyLoader_GetDiagnostics_ReturnsAccurateMetrics()
    {
        // Arrange
        var policies = await _loader.LoadAllPoliciesAsync(CancellationToken.None);

        // Act
        var diagnostics = _loader.GetDiagnostics(policies);

        // Assert
        Assert.Equal(policies.Length, diagnostics.TotalPolicies);
        Assert.True(diagnostics.TotalPolicies > 0);
        Assert.True(diagnostics.ParameterizedPolicies >= 0);
        Assert.Equal(0, diagnostics.AutoApplyPolicies); // CRITICAL: No policy should have AutoApply=true
    }

    [Fact]
    public async Task AllPolicies_NoAutoApply_EnforcesUserControl()
    {
        // Arrange
        var policies = await _loader.LoadAllPoliciesAsync(CancellationToken.None);

        // Act
        var diagnostics = _loader.GetDiagnostics(policies);

        // Assert - ABSOLUTE REQUIREMENT: Zero AutoApply policies
        Assert.Equal(0, diagnostics.AutoApplyPolicies);

        // Double-check each policy individually
        foreach (var policy in policies)
        {
            Assert.False(policy.AutoApply);
        }
    }
}
