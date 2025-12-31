using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningContracts.Commands;

/// <summary>
/// Command to retrieve available policy definitions
/// </summary>
public sealed class GetPoliciesCommand : CommandBase
{
    public override string CommandType => "GetPolicies";

    /// <summary>
    /// Filter by category
    /// </summary>
    public PolicyCategory? Category { get; init; }

    /// <summary>
    /// Filter by profile
    /// </summary>
    public string? ProfileName { get; init; }

    /// <summary>
    /// Include only policies applicable to current system
    /// </summary>
    public bool OnlyApplicable { get; init; } = true;
}
