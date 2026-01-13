using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PrivacyHardeningContracts.Models;
using PrivacyHardeningService.PolicyEngine;
using Xunit;

namespace PrivacyHardeningService.Tests;

public sealed class DependencyResolverTests
{
    private static PolicyDefinition Policy(string id, params PolicyDependency[] deps)
    {
        return new PolicyDefinition
        {
            PolicyId = id,
            Version = "1.0.0",
            Name = id,
            Category = PolicyCategory.Telemetry,
            Description = "Test policy",
            Mechanism = MechanismType.Registry,
            MechanismDetails = new { },
            SupportStatus = SupportStatus.Supported,
            RiskLevel = RiskLevel.Low,
            Reversible = true,
            Applicability = new PolicyApplicability { SupportedSkus = new[] { "*" } },
            Dependencies = deps
        };
    }

    private static PolicyDependency Dep(string id, DependencyType type, bool userCanOverride = false)
    {
        return new PolicyDependency
        {
            PolicyId = id,
            Reason = "test",
            Type = type,
            UserCanOverride = userCanOverride,
            Optional = false,
            AutoSelect = true
        };
    }

    [Fact]
    public void ValidateGraph_NoCycles_DoesNotThrow()
    {
        var resolver = new DependencyResolver(NullLogger<DependencyResolver>.Instance);

        var a = Policy("A", Dep("B", DependencyType.Required));
        var b = Policy("B", Dep("C", DependencyType.Required));
        var c = Policy("C");

        var act = () => resolver.ValidateGraph(new[] { a, b, c });

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateGraph_RequiredCycle_ThrowsAndContainsCyclePath()
    {
        var resolver = new DependencyResolver(NullLogger<DependencyResolver>.Instance);

        var a = Policy("A", Dep("B", DependencyType.Required));
        var b = Policy("B", Dep("A", DependencyType.Required));

        var act = () => resolver.ValidateGraph(new[] { a, b });

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Circular dependency detected*")
            .WithMessage("*A*")
            .WithMessage("*B*");
    }

    [Fact]
    public void ValidateGraph_ConflictOnly_DoesNotThrow()
    {
        var resolver = new DependencyResolver(NullLogger<DependencyResolver>.Instance);

        var a = Policy("A", Dep("B", DependencyType.Conflict));
        var b = Policy("B", Dep("A", DependencyType.Conflict));

        var act = () => resolver.ValidateGraph(new[] { a, b });

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateGraph_RecommendedOverridableCycle_IsIgnored_DoesNotThrow()
    {
        var resolver = new DependencyResolver(NullLogger<DependencyResolver>.Instance);

        // These create a "cycle" only through Recommended dependencies that can be overridden,
        // which the resolver intentionally does NOT traverse.
        var a = Policy("A", Dep("B", DependencyType.Recommended, userCanOverride: true));
        var b = Policy("B", Dep("A", DependencyType.Recommended, userCanOverride: true));

        var act = () => resolver.ValidateGraph(new[] { a, b });

        act.Should().NotThrow();
    }
}
