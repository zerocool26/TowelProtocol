using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningContracts.Responses;

/// <summary>
/// Result containing current service configuration
/// </summary>
public sealed class GetServiceConfigResult : ResponseBase
{
    public required ServiceConfiguration Configuration { get; init; }
}
