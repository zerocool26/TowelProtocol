namespace PrivacyHardeningContracts.Responses;

/// <summary>
/// Error response returned when an operation fails
/// </summary>
public sealed class ErrorResponse : ResponseBase
{
    /// <summary>
    /// Optional details about the failure
    /// </summary>
    public string? Details { get; init; }

    /// <summary>
    /// Optional stack trace (for debugging)
    /// </summary>
    public string? StackTrace { get; init; }
}
