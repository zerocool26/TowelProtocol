using System;

namespace PrivacyHardeningUI.Services;

/// <summary>
/// Thrown when the background Windows service is not reachable.
/// Used to distinguish "service down" from other operational errors.
/// </summary>
public sealed class ServiceUnavailableException : Exception
{
    public ServiceUnavailableException(string message) : base(message) { }

    public ServiceUnavailableException(string message, Exception innerException) : base(message, innerException) { }
}
