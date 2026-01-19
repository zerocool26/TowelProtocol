using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PrivacyHardeningContracts.Models;
using PrivacyHardeningService.Executors;
using System.Text.Json;
using Xunit;

namespace PrivacyHardeningService.Tests;

public sealed class RegistryExecutorTests
{
    [Fact]
    public async Task GetCurrentValueAsync_WorksWithPartialDetails()
    {
        // Arrange
        var logger = NullLogger<RegistryExecutor>.Instance;
        var executor = new RegistryExecutor(logger);

        // This simulates the object structure coming from YAML deserializer
        // targeting a key that is guaranteed to exist on Windows
        // MISSING valueData and expectedValue, and using Hive/Path split
        var mechanismDetails = new
        {
            type = "RegistryValue",
            hive = "HKLM",
            path = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            valueName = "ProductName",
            valueType = "String" 
        };

        var policy = new PolicyDefinition
        {
            PolicyId = "test-001",
            Version = "1.0.0",
            Name = "Test",
            Category = PolicyCategory.Telemetry,
            Description = "Test policy",
            SupportStatus = SupportStatus.Supported,
            RiskLevel = RiskLevel.Low,
            Reversible = true,
            Applicability = new PolicyApplicability { SupportedSkus = new[] { "All" } },
            Mechanism = MechanismType.Registry,
            MechanismDetails = mechanismDetails
        };

        // Act
        var result = await executor.GetCurrentValueAsync(policy, CancellationToken.None);
        
        // Assert
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
             result.Should().NotBeNull();
             result.Should().Contain("Windows"); 
        }
    }
}
