using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PrivacyHardeningUI.Services;

/// <summary>
/// Helper service to run registered IPolicyProbe instances and collect results.
/// Register individual probes in DI and then resolve this service to run them.
/// </summary>
public class ProbeRunner
{
    private readonly IEnumerable<Services.Probes.IPolicyProbe> _probes;

    public ProbeRunner(IEnumerable<Services.Probes.IPolicyProbe> probes)
    {
        _probes = probes ?? Enumerable.Empty<Services.Probes.IPolicyProbe>();
    }

    public async Task<List<Services.Probes.ProbeResult>> RunAllAsync(CancellationToken ct = default)
    {
        var tasks = _probes.Select(p => p.ProbeAsync(ct));
        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }
}
