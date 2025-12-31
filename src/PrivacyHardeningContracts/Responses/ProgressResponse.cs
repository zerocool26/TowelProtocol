namespace PrivacyHardeningContracts.Responses;

/// <summary>
/// Progress update emitted during long-running operations (Apply)
/// </summary>
public sealed class ProgressResponse : ResponseBase
{
    /// <summary>
    /// Percent complete (0-100)
    /// </summary>
    public int Percent { get; init; }

    /// <summary>
    /// Human-readable message about current step
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Optional current policy being processed
    /// </summary>
    public string? CurrentPolicyId { get; init; }
}
