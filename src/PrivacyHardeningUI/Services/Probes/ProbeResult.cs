using System;

namespace PrivacyHardeningUI.Services.Probes;

public enum ProbeType
{
    Unknown,
    Registry,
    Service,
    File,
    Process,
    Other
}

public class ProbeResult
{
    public string PolicyId { get; set; } = string.Empty;
    public ProbeType Type { get; set; } = ProbeType.Unknown;
    /// <summary>
    /// True = enabled, False = disabled, Null = unknown / not applicable
    /// </summary>
    public bool? IsEnabled { get; set; }
    public string Evidence { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool RequiresElevation { get; set; }
    /// <summary>Confidence 0.0-1.0</summary>
    public double Confidence { get; set; } = 1.0;
}
