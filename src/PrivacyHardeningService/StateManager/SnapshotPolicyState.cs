namespace PrivacyHardeningService.StateManager;

public sealed class SnapshotPolicyState
{
    public required string PolicyId { get; init; }
    public required bool IsApplied { get; init; }
    public string? CurrentValue { get; init; }
}

