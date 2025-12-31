using System;
using System.Collections.Generic;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using PrivacyHardeningService.Executors;
using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningTests;

/// <summary>
/// Tests for executor factory and basic executor functionality
/// </summary>
public class ExecutorTests
{
    private readonly ExecutorFactory _factory;

    public ExecutorTests()
    {
        // Create all executors
        var executors = new List<IExecutor>
        {
            new RegistryExecutor(NullLogger<RegistryExecutor>.Instance),
            new ServiceExecutor(NullLogger<ServiceExecutor>.Instance),
            new TaskExecutor(NullLogger<TaskExecutor>.Instance),
            new PowerShellExecutor(NullLogger<PowerShellExecutor>.Instance),
            new FirewallExecutor(NullLogger<FirewallExecutor>.Instance)
        };

        _factory = new ExecutorFactory(executors, NullLogger<ExecutorFactory>.Instance);
    }

    [Fact]
    public void GetExecutor_ReturnsRegistryExecutor_ForRegistryMechanism()
    {
        // Act
        var executor = _factory.GetExecutor(MechanismType.Registry);

        // Assert
        Assert.NotNull(executor);
        Assert.IsType<RegistryExecutor>(executor);
        Assert.Equal(MechanismType.Registry, executor.MechanismType);
    }

    [Fact]
    public void GetExecutor_ReturnsServiceExecutor_ForServiceMechanism()
    {
        // Act
        var executor = _factory.GetExecutor(MechanismType.Service);

        // Assert
        Assert.NotNull(executor);
        Assert.IsType<ServiceExecutor>(executor);
        Assert.Equal(MechanismType.Service, executor.MechanismType);
    }

    [Fact]
    public void GetExecutor_ReturnsTaskExecutor_ForScheduledTaskMechanism()
    {
        // Act
        var executor = _factory.GetExecutor(MechanismType.ScheduledTask);

        // Assert
        Assert.NotNull(executor);
        Assert.IsType<TaskExecutor>(executor);
        Assert.Equal(MechanismType.ScheduledTask, executor.MechanismType);
    }

    [Fact]
    public void GetExecutor_ReturnsPowerShellExecutor_ForPowerShellMechanism()
    {
        // Act
        var executor = _factory.GetExecutor(MechanismType.PowerShell);

        // Assert
        Assert.NotNull(executor);
        Assert.IsType<PowerShellExecutor>(executor);
        Assert.Equal(MechanismType.PowerShell, executor.MechanismType);
    }

    [Fact]
    public void GetExecutor_ReturnsFirewallExecutor_ForFirewallMechanism()
    {
        // Act
        var executor = _factory.GetExecutor(MechanismType.Firewall);

        // Assert
        Assert.NotNull(executor);
        Assert.IsType<FirewallExecutor>(executor);
        Assert.Equal(MechanismType.Firewall, executor.MechanismType);
    }

    [Fact]
    public void GetExecutor_ThrowsException_ForUnsupportedMechanism()
    {
        // Act & Assert
        Assert.Throws<NotSupportedException>(() =>
            _factory.GetExecutor(MechanismType.GroupPolicy));
    }

    [Fact]
    public void AllExecutors_HaveCorrectMechanismType()
    {
        // Arrange
        var mechanisms = new[]
        {
            MechanismType.Registry,
            MechanismType.Service,
            MechanismType.ScheduledTask,
            MechanismType.PowerShell,
            MechanismType.Firewall
        };

        // Act & Assert
        foreach (var mechanism in mechanisms)
        {
            var executor = _factory.GetExecutor(mechanism);
            Assert.Equal(mechanism, executor.MechanismType);
        }
    }
}
