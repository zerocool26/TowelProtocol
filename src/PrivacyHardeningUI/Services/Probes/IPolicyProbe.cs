using System.Threading;
using System.Threading.Tasks;

namespace PrivacyHardeningUI.Services.Probes;

/// <summary>
/// Probe interface to determine the current state of a policy/setting on the system.
/// Implementations should be lightweight and safe to run without elevated privileges
/// when possible; they should return evidence and whether elevation is required.
/// </summary>
public interface IPolicyProbe
{
    string PolicyId { get; }
    Task<ProbeResult> ProbeAsync(CancellationToken cancellationToken = default);
}
