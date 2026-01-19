using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningContracts.Commands;

/// <summary>
/// Command to analyze system and return smart recommendations
/// </summary>
public sealed class GetRecommendationsCommand : CommandBase
{
    public override string CommandType => "GetRecommendations";
}
