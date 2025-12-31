using Microsoft.Extensions.Logging;

namespace PrivacyHardeningService.StateManager;

/// <summary>
/// Detects configuration drift after Windows updates
/// </summary>
public sealed class DriftDetector
{
    private readonly ILogger<DriftDetector> _logger;

    public DriftDetector(ILogger<DriftDetector> logger)
    {
        _logger = logger;
    }

    // TODO: Implement drift detection logic
}
