using PrivacyHardeningContracts.Responses;

namespace PrivacyHardeningContracts.Commands;

/// <summary>
/// Command to retrieve current service configuration
/// </summary>
public sealed class GetServiceConfigCommand : CommandBase
{
    public override string CommandType => "GetConfig";
}
