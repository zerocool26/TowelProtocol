using Microsoft.Extensions.Logging;
using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningService.Executors;

/// <summary>
/// Factory to get appropriate executor for a policy mechanism
/// </summary>
public sealed class ExecutorFactory
{
    private readonly Dictionary<MechanismType, IExecutor> _executors;
    private readonly ILogger<ExecutorFactory> _logger;

    public ExecutorFactory(IEnumerable<IExecutor> executors, ILogger<ExecutorFactory> logger)
    {
        _executors = executors.ToDictionary(e => e.MechanismType);
        _logger = logger;
    }

    public IExecutor GetExecutor(MechanismType mechanism)
    {
        if (_executors.TryGetValue(mechanism, out var executor))
        {
            return executor;
        }

        throw new NotSupportedException($"No executor available for mechanism: {mechanism}");
    }
}
