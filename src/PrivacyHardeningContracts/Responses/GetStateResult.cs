using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningContracts.Responses;

/// <summary>
/// Current system state result
/// </summary>
public sealed class GetStateResult : ResponseBase
{
    /// <summary>
    /// Current system snapshot
    /// </summary>
    public required SystemSnapshot CurrentState { get; init; }

    /// <summary>
    /// Currently applied policy IDs
    /// </summary>
    public required string[] AppliedPolicies { get; init; }

    /// <summary>
    /// System information
    /// </summary>
    public required SystemInfo SystemInfo { get; init; }
}
