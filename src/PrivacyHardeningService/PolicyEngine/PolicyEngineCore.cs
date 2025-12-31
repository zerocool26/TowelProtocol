using Microsoft.Extensions.Logging;
using PrivacyHardeningContracts.Commands;
using PrivacyHardeningContracts.Models;
using PrivacyHardeningContracts.Responses;
using PrivacyHardeningService.Executors;
using PrivacyHardeningService.StateManager;

namespace PrivacyHardeningService.PolicyEngine;

/// <summary>
/// Core policy engine - orchestrates policy operations
/// </summary>
public sealed class PolicyEngineCore
{
    private readonly ILogger<PolicyEngineCore> _logger;
    private readonly PolicyLoader _loader;
    private readonly PolicyValidator _validator;
    private readonly CompatibilityChecker _compatibility;
    private readonly DependencyResolver _dependencyResolver;
    private readonly ExecutorFactory _executorFactory;
    private readonly ChangeLog _changeLog;
    private readonly SystemStateCapture _stateCapture;
    private readonly DriftDetector _driftDetector;

    private PolicyDefinition[]? _cachedPolicies;

    public PolicyEngineCore(
        ILogger<PolicyEngineCore> logger,
        PolicyLoader loader,
        PolicyValidator validator,
        CompatibilityChecker compatibility,
        DependencyResolver dependencyResolver,
        ExecutorFactory executorFactory,
        ChangeLog changeLog,
        SystemStateCapture stateCapture,
        DriftDetector driftDetector)
    {
        _logger = logger;
        _loader = loader;
        _validator = validator;
        _compatibility = compatibility;
        _dependencyResolver = dependencyResolver;
        _executorFactory = executorFactory;
        _changeLog = changeLog;
        _stateCapture = stateCapture;
        _driftDetector = driftDetector;
    }

    public async Task<GetPoliciesResult> GetPoliciesAsync(GetPoliciesCommand command, CancellationToken cancellationToken)
    {
        var policies = await LoadPoliciesAsync(cancellationToken);

        // Filter by category if specified
        if (command.Category.HasValue)
        {
            policies = policies.Where(p => p.Category == command.Category.Value).ToArray();
        }

        // Filter by applicability
        if (command.OnlyApplicable)
        {
            policies = policies.Where(p => _compatibility.IsApplicable(p)).ToArray();
        }

        return new GetPoliciesResult
        {
            CommandId = command.CommandId,
            Success = true,
            Policies = policies,
            ManifestVersion = "1.0.0", // TODO: Load from manifest
            LastUpdated = DateTime.UtcNow
        };
    }

    public async Task<AuditResult> AuditAsync(AuditCommand command, CancellationToken cancellationToken)
    {
        var policies = await LoadPoliciesAsync(cancellationToken);

        // Filter to requested policies
        if (command.PolicyIds != null && command.PolicyIds.Length > 0)
        {
            policies = policies.Where(p => command.PolicyIds.Contains(p.PolicyId)).ToArray();
        }

        var items = new List<PolicyAuditItem>();

        foreach (var policy in policies)
        {
            var isApplicable = _compatibility.IsApplicable(policy);
            string? notApplicableReason = null;

            if (!isApplicable)
            {
                notApplicableReason = "Not applicable to current system configuration";
            }

            bool isApplied = false;
            string? currentValue = null;
            string? expectedValue = null;
            bool matches = false;

            if (isApplicable)
            {
                try
                {
                    var executor = _executorFactory.GetExecutor(policy.Mechanism);
                    isApplied = await executor.IsAppliedAsync(policy, cancellationToken);
                    currentValue = await executor.GetCurrentValueAsync(policy, cancellationToken);
                    matches = isApplied;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error auditing policy {PolicyId}", policy.PolicyId);
                }
            }

            items.Add(new PolicyAuditItem
            {
                PolicyId = policy.PolicyId,
                PolicyName = policy.Name,
                Category = policy.Category,
                RiskLevel = policy.RiskLevel,
                SupportStatus = policy.SupportStatus,
                IsApplied = isApplied,
                IsApplicable = isApplicable,
                NotApplicableReason = notApplicableReason,
                CurrentValue = currentValue,
                ExpectedValue = expectedValue,
                Matches = matches,
                DriftDescription = matches ? null : "Policy not applied or value differs"
            });
        }

        return new AuditResult
        {
            CommandId = command.CommandId,
            Success = true,
            Items = items.ToArray(),
            SystemInfo = await _stateCapture.GetSystemInfoAsync(cancellationToken)
        };
    }

    public async Task<ApplyResult> ApplyAsync(ApplyCommand command, CancellationToken cancellationToken)
    {
        var policies = await LoadPoliciesAsync(cancellationToken);

        // Resolve dependencies
        var policiesToApply = _dependencyResolver.ResolveDependencies(policies, command.PolicyIds);

        var appliedPolicies = new List<string>();
        var failedPolicies = new List<string>();
        var changes = new List<ChangeRecord>();

        foreach (var policy in policiesToApply)
        {
            if (!_compatibility.IsApplicable(policy))
            {
                _logger.LogWarning("Skipping non-applicable policy: {PolicyId}", policy.PolicyId);
                failedPolicies.Add(policy.PolicyId);
                continue;
            }

            try
            {
                var executor = _executorFactory.GetExecutor(policy.Mechanism);
                var change = await executor.ApplyAsync(policy, cancellationToken);
                changes.Add(change);

                if (change.Success)
                {
                    appliedPolicies.Add(policy.PolicyId);
                    _logger.LogInformation("Applied policy: {PolicyId}", policy.PolicyId);
                }
                else
                {
                    failedPolicies.Add(policy.PolicyId);
                    _logger.LogWarning("Failed to apply policy: {PolicyId} - {Error}",
                        policy.PolicyId, change.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying policy: {PolicyId}", policy.PolicyId);
                failedPolicies.Add(policy.PolicyId);

                if (!command.ContinueOnError)
                {
                    break;
                }
            }
        }

        // Save changes to log
        await _changeLog.SaveChangesAsync(changes.ToArray(), cancellationToken);

        return new ApplyResult
        {
            CommandId = command.CommandId,
            Success = failedPolicies.Count == 0,
            AppliedPolicies = appliedPolicies.ToArray(),
            FailedPolicies = failedPolicies.ToArray(),
            Changes = changes.ToArray(),
            SnapshotId = Guid.NewGuid().ToString(),
            CompletedAt = DateTime.UtcNow,
            RestartRecommended = false
        };
    }

    // Overload which reports progress via a callback. Progress percent is approximate based on number of policies processed.
    public async Task<ApplyResult> ApplyAsync(ApplyCommand command, Action<int, string?>? progressCallback, CancellationToken cancellationToken)
    {
        var policies = await LoadPoliciesAsync(cancellationToken);

        // Resolve dependencies
        var policiesToApply = _dependencyResolver.ResolveDependencies(policies, command.PolicyIds);

        var appliedPolicies = new List<string>();
        var failedPolicies = new List<string>();
        var changes = new List<ChangeRecord>();

        int total = Math.Max(1, policiesToApply.Length);
        int processed = 0;

        progressCallback?.Invoke(0, "Starting apply...");

        foreach (var policy in policiesToApply)
        {
            processed++;
            int percent = (int)((processed - 1) * 100.0 / total);
            progressCallback?.Invoke(percent, $"Applying {policy.PolicyId}");

            if (!_compatibility.IsApplicable(policy))
            {
                _logger.LogWarning("Skipping non-applicable policy: {PolicyId}", policy.PolicyId);
                failedPolicies.Add(policy.PolicyId);
                continue;
            }

            try
            {
                var executor = _executorFactory.GetExecutor(policy.Mechanism);
                var change = await executor.ApplyAsync(policy, cancellationToken);
                changes.Add(change);

                if (change.Success)
                {
                    appliedPolicies.Add(policy.PolicyId);
                    _logger.LogInformation("Applied policy: {PolicyId}", policy.PolicyId);
                }
                else
                {
                    failedPolicies.Add(policy.PolicyId);
                    _logger.LogWarning("Failed to apply policy: {PolicyId} - {Error}",
                        policy.PolicyId, change.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying policy: {PolicyId}", policy.PolicyId);
                failedPolicies.Add(policy.PolicyId);

                if (!command.ContinueOnError)
                {
                    break;
                }
            }

            percent = (int)(processed * 100.0 / total);
            progressCallback?.Invoke(percent, $"Completed {policy.PolicyId}");
        }

        // Save changes to log
        await _changeLog.SaveChangesAsync(changes.ToArray(), cancellationToken);

        return new ApplyResult
        {
            CommandId = command.CommandId,
            Success = failedPolicies.Count == 0,
            AppliedPolicies = appliedPolicies.ToArray(),
            FailedPolicies = failedPolicies.ToArray(),
            Changes = changes.ToArray(),
            SnapshotId = Guid.NewGuid().ToString(),
            CompletedAt = DateTime.UtcNow,
            RestartRecommended = false
        };
    }

    public async Task<RevertResult> RevertAsync(RevertCommand command, CancellationToken cancellationToken)
    {
        var policies = await LoadPoliciesAsync(cancellationToken);

        var revertedPolicies = new List<string>();
        var failedPolicies = new List<string>();
        var changes = new List<ChangeRecord>();
        var errors = new List<ErrorInfo>();

        // Get policies to revert
        var policiesToRevert = command.PolicyIds != null && command.PolicyIds.Length > 0
            ? policies.Where(p => command.PolicyIds.Contains(p.PolicyId)).ToArray()
            : Array.Empty<PolicyDefinition>();

        if (policiesToRevert.Length == 0)
        {
            errors.Add(new ErrorInfo
            {
                Code = "NoPoliciesSpecified",
                Message = "No policies specified for revert operation"
            });

            return new RevertResult
            {
                CommandId = command.CommandId,
                Success = false,
                RevertedPolicies = Array.Empty<string>(),
                FailedPolicies = Array.Empty<string>(),
                Changes = Array.Empty<ChangeRecord>(),
                CompletedAt = DateTime.UtcNow,
                Errors = errors.ToArray()
            };
        }

        // Reverse order for dependency handling (revert dependents first)
        var orderedPolicies = Enumerable.Reverse(policiesToRevert).ToArray();

        foreach (var policy in orderedPolicies)
        {
            try
            {
                // Get the most recent change record for this policy
                var policyChanges = await _changeLog.GetChangesForPolicyAsync(policy.PolicyId, cancellationToken);

                if (policyChanges.Length == 0)
                {
                    _logger.LogWarning("No change history found for policy: {PolicyId}", policy.PolicyId);
                    failedPolicies.Add(policy.PolicyId);
                    errors.Add(new ErrorInfo
                    {
                        Code = "NoChangeHistory",
                        Message = $"No change history found for policy {policy.PolicyId}"
                    });
                    continue;
                }

                // Get the most recent successful change
                var lastChange = policyChanges.FirstOrDefault(c => c.Success);

                if (lastChange == null)
                {
                    _logger.LogWarning("No successful changes found for policy: {PolicyId}", policy.PolicyId);
                    failedPolicies.Add(policy.PolicyId);
                    errors.Add(new ErrorInfo
                    {
                        Code = "NoSuccessfulChanges",
                        Message = $"No successful changes found for policy {policy.PolicyId}"
                    });
                    continue;
                }

                // Execute revert using the executor
                var executor = _executorFactory.GetExecutor(policy.Mechanism);
                var revertChange = await executor.RevertAsync(policy, lastChange, cancellationToken);
                changes.Add(revertChange);

                if (revertChange.Success)
                {
                    revertedPolicies.Add(policy.PolicyId);
                    _logger.LogInformation("Reverted policy: {PolicyId}", policy.PolicyId);
                }
                else
                {
                    failedPolicies.Add(policy.PolicyId);
                    _logger.LogWarning("Failed to revert policy: {PolicyId} - {Error}",
                        policy.PolicyId, revertChange.ErrorMessage);

                    errors.Add(new ErrorInfo
                    {
                        Code = "RevertFailed",
                        Message = $"Failed to revert {policy.PolicyId}: {revertChange.ErrorMessage}"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reverting policy: {PolicyId}", policy.PolicyId);
                failedPolicies.Add(policy.PolicyId);

                errors.Add(new ErrorInfo
                {
                    Code = "RevertError",
                    Message = $"Error reverting {policy.PolicyId}: {ex.Message}"
                });

                // Stop on first error
                break;
            }
        }

        // Save revert changes to log
        if (changes.Count > 0)
        {
            await _changeLog.SaveChangesAsync(changes.ToArray(), cancellationToken);
        }

        return new RevertResult
        {
            CommandId = command.CommandId,
            Success = failedPolicies.Count == 0,
            RevertedPolicies = revertedPolicies.ToArray(),
            FailedPolicies = failedPolicies.ToArray(),
            Changes = changes.ToArray(),
            CompletedAt = DateTime.UtcNow,
            Errors = errors.Count > 0 ? errors.ToArray() : Array.Empty<ErrorInfo>()
        };
    }

    public async Task<GetStateResult> GetStateAsync(GetStateCommand command, CancellationToken cancellationToken)
    {
        // TODO: Implement state retrieval
        await Task.CompletedTask;

        return new GetStateResult
        {
            CommandId = command.CommandId,
            Success = false,
            CurrentState = new SystemSnapshot
            {
                SnapshotId = Guid.NewGuid().ToString(),
                CreatedAt = DateTime.UtcNow,
                WindowsBuild = Environment.OSVersion.Version.Build,
                WindowsSku = "Enterprise",
                AppliedPolicies = Array.Empty<string>(),
                ChangeHistory = Array.Empty<ChangeRecord>()
            },
            AppliedPolicies = Array.Empty<string>(),
            SystemInfo = await _stateCapture.GetSystemInfoAsync(cancellationToken),
            Errors = new[] { new ErrorInfo { Code = "NotImplemented", Message = "GetState not fully implemented" } }
        };
    }

    public async Task<DriftDetectionResult> DetectDriftAsync(DetectDriftCommand command, CancellationToken cancellationToken)
    {
        // TODO: Implement drift detection
        await Task.CompletedTask;

        return new DriftDetectionResult
        {
            CommandId = command.CommandId,
            Success = true,
            DriftDetected = false,
            DriftedPolicies = Array.Empty<DriftItem>()
        };
    }

    public async Task<GetStateResult> CreateSnapshotAsync(CreateSnapshotCommand command, CancellationToken cancellationToken)
    {
        // TODO: Implement snapshot creation
        await Task.CompletedTask;

        return new GetStateResult
        {
            CommandId = command.CommandId,
            Success = false,
            CurrentState = new SystemSnapshot
            {
                SnapshotId = Guid.NewGuid().ToString(),
                CreatedAt = DateTime.UtcNow,
                WindowsBuild = Environment.OSVersion.Version.Build,
                WindowsSku = "Unknown",
                AppliedPolicies = Array.Empty<string>(),
                ChangeHistory = Array.Empty<ChangeRecord>()
            },
            AppliedPolicies = Array.Empty<string>(),
            SystemInfo = await _stateCapture.GetSystemInfoAsync(cancellationToken),
            Errors = new[] { new ErrorInfo { Code = "NotImplemented", Message = "CreateSnapshot not yet implemented" } }
        };
    }

    private async Task<PolicyDefinition[]> LoadPoliciesAsync(CancellationToken cancellationToken)
    {
        if (_cachedPolicies == null)
        {
            // Ensure loader result is non-null to avoid possible null-reference assignments
            _cachedPolicies = await _loader.LoadAllPoliciesAsync(cancellationToken) ?? Array.Empty<PolicyDefinition>();
        }

        return _cachedPolicies;
    }
}
