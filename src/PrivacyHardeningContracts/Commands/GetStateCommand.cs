namespace PrivacyHardeningContracts.Commands;

/// <summary>
/// Command to retrieve current system state and applied policies
/// </summary>
public sealed class GetStateCommand : CommandBase
{
    public override string CommandType => "GetState";

    /// <summary>
    /// Include full change history
    /// </summary>
    public bool IncludeHistory { get; init; } = false;
}
