using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using PrivacyHardeningContracts.Commands;
using PrivacyHardeningService.IPC;

namespace PrivacyHardeningTests;

public class CommandValidatorTests
{
    [Fact]
    public void ApplyCommand_MissingPolicyIds_IsInvalid()
    {
        var validator = new CommandValidator(new NullLogger<CommandValidator>());
        var cmd = new ApplyCommand { PolicyIds = Array.Empty<string>() };

        var result = validator.ValidateCommand(cmd);

        Assert.False(result);
    }

    [Fact]
    public void ApplyCommand_WithPolicyIds_IsValid()
    {
        var validator = new CommandValidator(new NullLogger<CommandValidator>());
        var cmd = new ApplyCommand { PolicyIds = new[] { "tel-001" } };

        var result = validator.ValidateCommand(cmd);

        Assert.True(result);
    }

    [Fact]
    public void RevertCommand_MissingAll_IsInvalid()
    {
        var validator = new CommandValidator(new NullLogger<CommandValidator>());
        var cmd = new RevertCommand { PolicyIds = null, SnapshotId = null, RestorePointId = null };

        var result = validator.ValidateCommand(cmd);

        Assert.False(result);
    }

    [Fact]
    public void RevertCommand_WithPolicyIds_IsValid()
    {
        var validator = new CommandValidator(new NullLogger<CommandValidator>());
        var cmd = new RevertCommand { PolicyIds = new[] { "tel-001" } };

        var result = validator.ValidateCommand(cmd);

        Assert.True(result);
    }

    [Fact]
    public void AuditCommand_IsValid()
    {
        var validator = new CommandValidator(new NullLogger<CommandValidator>());
        var cmd = new AuditCommand { IncludeDetails = true };

        var result = validator.ValidateCommand(cmd);

        Assert.True(result);
    }

    [Fact]
    public void Command_Serialization_Polymorphic_Roundtrip()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        CommandBase original = new ApplyCommand { PolicyIds = new[] { "tel-001" } };
        var json = JsonSerializer.Serialize(original, original.GetType(), options);

        var deserialized = JsonSerializer.Deserialize<ApplyCommand>(json, options);

        Assert.NotNull(deserialized);
    }

    [Fact]
    public void ParseIntegrityRidFromSidString_Works()
    {
        Assert.Equal(12288, PrivacyHardeningService.Security.CallerValidator.ParseIntegrityRidFromSidString("S-1-16-12288"));
        Assert.Equal(4096, PrivacyHardeningService.Security.CallerValidator.ParseIntegrityRidFromSidString("S-1-16-4096"));
        Assert.Equal(0, PrivacyHardeningService.Security.CallerValidator.ParseIntegrityRidFromSidString("S-1-5-21"));
    }
}
