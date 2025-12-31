using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using PrivacyHardeningService.Executors;
using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningTests;

/// <summary>
/// Integration tests for executors with real policy data
/// Tests executor capabilities without actually modifying the system
/// </summary>
public class ExecutorIntegrationTests
{
    private readonly ExecutorFactory _factory;
    private readonly List<IExecutor> _allExecutors;

    public ExecutorIntegrationTests()
    {
        _allExecutors = new List<IExecutor>
        {
            new RegistryExecutor(NullLogger<RegistryExecutor>.Instance),
            new ServiceExecutor(NullLogger<ServiceExecutor>.Instance),
            new TaskExecutor(NullLogger<TaskExecutor>.Instance),
            new PowerShellExecutor(NullLogger<PowerShellExecutor>.Instance),
            new FirewallExecutor(NullLogger<FirewallExecutor>.Instance)
        };

        _factory = new ExecutorFactory(_allExecutors, NullLogger<ExecutorFactory>.Instance);
    }

    [Fact]
    public void AllExecutors_ImplementIExecutorInterface()
    {
        // Assert
        foreach (var executor in _allExecutors)
        {
            Assert.NotNull(executor);
            Assert.IsAssignableFrom<IExecutor>(executor);
            Assert.True(Enum.IsDefined(typeof(MechanismType), executor.MechanismType));
        }
    }

    [Fact]
    public void ExecutorFactory_CanRetrieveAllExecutorTypes()
    {
        // Arrange
        var mechanismTypes = new[]
        {
            MechanismType.Registry,
            MechanismType.Service,
            MechanismType.ScheduledTask,
            MechanismType.PowerShell,
            MechanismType.Firewall
        };

        // Act & Assert
        foreach (var mechanism in mechanismTypes)
        {
            var executor = _factory.GetExecutor(mechanism);
            Assert.NotNull(executor);
            Assert.Equal(mechanism, executor.MechanismType);
        }
    }

    [Fact]
    public void RegistryExecutor_HasCorrectMechanismType()
    {
        // Arrange
        var executor = _factory.GetExecutor(MechanismType.Registry);

        // Assert
        Assert.IsType<RegistryExecutor>(executor);
        Assert.Equal(MechanismType.Registry, executor.MechanismType);
    }

    [Fact]
    public void ServiceExecutor_HasCorrectMechanismType()
    {
        // Arrange
        var executor = _factory.GetExecutor(MechanismType.Service);

        // Assert
        Assert.IsType<ServiceExecutor>(executor);
        Assert.Equal(MechanismType.Service, executor.MechanismType);
    }

    [Fact]
    public void TaskExecutor_HasCorrectMechanismType()
    {
        // Arrange
        var executor = _factory.GetExecutor(MechanismType.ScheduledTask);

        // Assert
        Assert.IsType<TaskExecutor>(executor);
        Assert.Equal(MechanismType.ScheduledTask, executor.MechanismType);
    }

    [Fact]
    public void PowerShellExecutor_HasCorrectMechanismType()
    {
        // Arrange
        var executor = _factory.GetExecutor(MechanismType.PowerShell);

        // Assert
        Assert.IsType<PowerShellExecutor>(executor);
        Assert.Equal(MechanismType.PowerShell, executor.MechanismType);
    }

    [Fact]
    public void FirewallExecutor_HasCorrectMechanismType()
    {
        // Arrange
        var executor = _factory.GetExecutor(MechanismType.Firewall);

        // Assert
        Assert.IsType<FirewallExecutor>(executor);
        Assert.Equal(MechanismType.Firewall, executor.MechanismType);
    }

    [Fact]
    public void ExecutorFactory_ThrowsForUnsupportedMechanism()
    {
        // Assert
        Assert.Throws<NotSupportedException>(() =>
            _factory.GetExecutor(MechanismType.GroupPolicy));

        Assert.Throws<NotSupportedException>(() =>
            _factory.GetExecutor(MechanismType.MDM));

        Assert.Throws<NotSupportedException>(() =>
            _factory.GetExecutor(MechanismType.HostsFile));

        Assert.Throws<NotSupportedException>(() =>
            _factory.GetExecutor(MechanismType.WFPDriver));
    }

    [Fact]
    public void AllExecutors_HaveUniqueMethodTypes()
    {
        // Arrange
        var mechanismTypes = new HashSet<MechanismType>();

        // Act
        foreach (var executor in _allExecutors)
        {
            mechanismTypes.Add(executor.MechanismType);
        }

        // Assert - Each executor should have a unique mechanism type
        Assert.Equal(_allExecutors.Count, mechanismTypes.Count);
    }

    [Theory]
    [InlineData(MechanismType.Registry)]
    [InlineData(MechanismType.Service)]
    [InlineData(MechanismType.ScheduledTask)]
    [InlineData(MechanismType.PowerShell)]
    [InlineData(MechanismType.Firewall)]
    public void ExecutorFactory_ReturnsConsistentInstance(MechanismType mechanism)
    {
        // Act
        var executor1 = _factory.GetExecutor(mechanism);
        var executor2 = _factory.GetExecutor(mechanism);

        // Assert - Factory should return the same instance
        Assert.Same(executor1, executor2);
    }

    [Fact]
    public void AllExecutors_AreNotNull()
    {
        // Assert
        Assert.All(_allExecutors, executor => Assert.NotNull(executor));
    }

    [Fact]
    public void ExecutorFactory_CoversAllImplementedMechanisms()
    {
        // Arrange - These are the mechanisms we have executors for
        var implementedMechanisms = new[]
        {
            MechanismType.Registry,
            MechanismType.Service,
            MechanismType.ScheduledTask,
            MechanismType.PowerShell,
            MechanismType.Firewall
        };

        // Act & Assert
        foreach (var mechanism in implementedMechanisms)
        {
            var executor = _factory.GetExecutor(mechanism);
            Assert.NotNull(executor);
            Assert.Equal(mechanism, executor.MechanismType);
        }
    }
}
