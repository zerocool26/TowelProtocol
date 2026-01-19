using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PrivacyHardeningContracts.Models;
using PrivacyHardeningService.Executors;
using PrivacyHardeningService.PolicyEngine;
using PrivacyHardeningService.StateManager;
using System.Threading;
using Xunit;

namespace PrivacyHardeningService.Tests.StateManager;

public class DriftDetectorTests
{
    // Mocks
    class MockExecutor : IExecutor
    {
        public MechanismType MechanismType => MechanismType.Registry;
        public bool IsApplied { get; set; } = true;
        public string? CurrentValue { get; set; } = "1";

        public Task<bool> IsAppliedAsync(PolicyDefinition policy, CancellationToken cancellationToken)
            => Task.FromResult(IsApplied);
        
        public Task<string?> GetCurrentValueAsync(PolicyDefinition policy, CancellationToken cancellationToken)
            => Task.FromResult(CurrentValue);

        public Task<ChangeRecord> ApplyAsync(PolicyDefinition policy, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ChangeRecord> RevertAsync(PolicyDefinition policy, ChangeRecord changeToRevert, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    class MockExecutorFactory : IExecutorFactory
    {
        public MockExecutor Executor { get; } = new();
        public IExecutor? GetExecutor(MechanismType mechanism) => Executor;
    }

    class MockPolicyLoader : IPolicyLoader
    {
        public event EventHandler? PoliciesChanged;
        public PolicyDefinition[] Policies { get; set; } = Array.Empty<PolicyDefinition>();
        public Task<PolicyDefinition[]> LoadAllPoliciesAsync(CancellationToken cancellationToken) => Task.FromResult(Policies);
    }

    private PolicyDefinition CreateTestPolicy()
    {
        return new PolicyDefinition 
        { 
            PolicyId = "TestPolicy", 
            Name = "Test Policy", 
            Mechanism = MechanismType.Registry,
            Version = "1.0.0",
            Category = PolicyCategory.Telemetry,
            Description = "Description",
            MechanismDetails = new { },
            SupportStatus = SupportStatus.Supported,
            RiskLevel = RiskLevel.Low,
            Reversible = true,
            Applicability = new PolicyApplicability 
            {
                SupportedSkus = new[] { "All" }
            }
        };
    }

    [Fact]
    public async Task DetectDrift_ShouldReturnDrift_WhenPolicyNoLongerApplied()
    {
        // Arrange
        var mockExecutorFactory = new MockExecutorFactory();
        var mockPolicyLoader = new MockPolicyLoader();
        
        var policy = CreateTestPolicy();
        mockPolicyLoader.Policies = new[] { policy };

        // Expected: Applied.
        var expectedState = new SnapshotPolicyState 
        { 
            PolicyId = "TestPolicy", 
            IsApplied = true, 
            CurrentValue = "1" 
        };
        
        var snapshot = new[] { expectedState };

        // Actual: Not applied.
        mockExecutorFactory.Executor.IsApplied = false;
        mockExecutorFactory.Executor.CurrentValue = "0";

        var detector = new DriftDetector(
            NullLogger<DriftDetector>.Instance,
            mockExecutorFactory);

        // Act
        var driftItems = await detector.DetectDriftAsync(snapshot, mockPolicyLoader.Policies, CancellationToken.None);

        // Assert
        driftItems.Should().ContainSingle();
        var drift = driftItems.First();
        drift.PolicyId.Should().Be("TestPolicy");
        drift.ExpectedValue.Should().Be("1"); 
        drift.CurrentValue.Should().Be("0");
    }

    [Fact]
    public async Task DetectDrift_ShouldReturnNoDrift_WhenPolicyStillApplied()
    {
        // Arrange
        var mockExecutorFactory = new MockExecutorFactory();
        var mockPolicyLoader = new MockPolicyLoader();
        
        var policy = CreateTestPolicy();
        mockPolicyLoader.Policies = new[] { policy };

        var expectedState = new SnapshotPolicyState 
        { 
            PolicyId = "TestPolicy", 
            IsApplied = true, 
            CurrentValue = "1" 
        };
        
        var snapshot = new[] { expectedState };

        mockExecutorFactory.Executor.IsApplied = true;
        mockExecutorFactory.Executor.CurrentValue = "1";

        var detector = new DriftDetector(
            NullLogger<DriftDetector>.Instance,
            mockExecutorFactory);

        // Act
        var driftItems = await detector.DetectDriftAsync(snapshot, mockPolicyLoader.Policies, CancellationToken.None);

        // Assert
        driftItems.Should().BeEmpty();
    }
}
