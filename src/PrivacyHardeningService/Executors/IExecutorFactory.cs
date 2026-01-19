using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningService.Executors;

/// <summary>
/// Interface for factory to get appropriate executor for a policy mechanism
/// </summary>
public interface IExecutorFactory
{
    /// <summary>
    /// Gets the executor for the specified mechanism type
    /// </summary>
    IExecutor? GetExecutor(MechanismType mechanism);
}
