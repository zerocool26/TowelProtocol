namespace PrivacyHardeningContracts.Responses;

/// <summary>
/// Base class for all service responses
/// </summary>
public abstract class ResponseBase
{
    /// <summary>
    /// Corresponding command ID
    /// </summary>
    public required string CommandId { get; init; }

    /// <summary>
    /// Overall success status
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Response timestamp
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Error messages (if any)
    /// </summary>
    public ErrorInfo[] Errors { get; init; } = Array.Empty<ErrorInfo>();

    /// <summary>
    /// Warning messages
    /// </summary>
    public string[] Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Detailed error information
/// </summary>
public sealed class ErrorInfo
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? Details { get; init; }
    public string? PolicyId { get; init; }
}
