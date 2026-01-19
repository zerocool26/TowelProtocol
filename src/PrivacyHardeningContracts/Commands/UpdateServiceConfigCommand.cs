using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningContracts.Commands;

/// <summary>
/// Command to update service configuration
/// </summary>
public sealed class UpdateServiceConfigCommand : CommandBase
{
    public override string CommandType => "UpdateConfig";

    public required ServiceConfiguration Configuration { get; init; }
}
