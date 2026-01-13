using Microsoft.Extensions.Logging;
using PrivacyHardeningContracts.Commands;
using PrivacyHardeningContracts.Models;
using PrivacyHardeningContracts.Responses;
using PrivacyHardeningService.Executors;
using PrivacyHardeningService.StateManager;
using System.Threading;
using System.Text.Json;

namespace PrivacyHardeningService.PolicyEngine;

/// <summary>
/// Core policy engine - orchestrates policy operations
/// </summary>
public sealed class PolicyEngineCore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly ILogger<PolicyEngineCore> _logger;
    private readonly PolicyLoader _loader;
    private readonly PolicyValidator _validator;
    private readonly CompatibilityChecker _compatibility;
    private readonly DependencyResolver _dependencyResolver;
    private readonly ExecutorFactory _executorFactory;
    private readonly ChangeLog _changeLog;
    private readonly SystemStateCapture _stateCapture;
    private readonly RestorePointManager _restorePointManager;
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
        RestorePointManager restorePointManager,
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
        _restorePointManager = restorePointManager;
        _driftDetector = driftDetector;

        _loader.PoliciesChanged += (_, _) =>
        {
            _logger.LogInformation("Policy cache invalidated due to on-disk policy changes.");
            Interlocked.Exchange(ref _cachedPolicies, null);
        };
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
            var expectedValue = GetExpectedValueForAudit(policy);
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
                DriftDescription = !isApplicable
                    ? null
                    : matches
                        ? null
                        : $"Expected: {expectedValue ?? "<unknown>"}; Current: {currentValue ?? "<unknown>"}"
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

    private static string? GetExpectedValueForAudit(PolicyDefinition policy)
    {
        try
        {
            var json = JsonSerializer.Serialize(policy.MechanismDetails);

            return policy.Mechanism switch
            {
                MechanismType.Registry => JsonSerializer.Deserialize<RegistryDetails>(json, JsonOptions)?.ExpectedValue,
                MechanismType.Service => BuildServiceExpectedValue(JsonSerializer.Deserialize<ServiceDetails>(json, JsonOptions)),
                MechanismType.ScheduledTask => BuildTaskExpectedValue(JsonSerializer.Deserialize<TaskDetails>(json, JsonOptions)),
                MechanismType.Firewall => BuildFirewallExpectedValue(JsonSerializer.Deserialize<FirewallMechanismDetails>(json, JsonOptions)),
                MechanismType.PowerShell => !string.IsNullOrWhiteSpace(policy.ExpectedOutput)
                    ? policy.ExpectedOutput
                    : !string.IsNullOrWhiteSpace(policy.VerificationCommand)
                        ? $"Verify: {policy.VerificationCommand}"
                        : null,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? BuildServiceExpectedValue(ServiceDetails? details)
    {
        if (details == null || string.IsNullOrWhiteSpace(details.StartupType))
        {
            return null;
        }

        var expected = $"StartupType={details.StartupType}";
        if (details.StopService)
        {
            expected += ", Status=Stopped";
        }

        return expected;
    }

    private static string? BuildTaskExpectedValue(TaskDetails? details)
    {
        if (details == null || string.IsNullOrWhiteSpace(details.Action))
        {
            return null;
        }

        var shouldBeEnabled = !string.Equals(details.Action, "disable", StringComparison.OrdinalIgnoreCase);
        return $"Enabled={shouldBeEnabled}";
    }

    private static string? BuildFirewallExpectedValue(FirewallMechanismDetails? details)
    {
        if (details == null)
        {
            return null;
        }

        if (details.FirewallRule != null && !string.IsNullOrWhiteSpace(details.FirewallRule.RemoteAddress))
        {
            var direction = string.IsNullOrWhiteSpace(details.FirewallRule.Direction) ? "Outbound" : details.FirewallRule.Direction;
            var action = string.IsNullOrWhiteSpace(details.FirewallRule.Action) ? "Block" : details.FirewallRule.Action;
            return $"{action} {direction}: {details.FirewallRule.RemoteAddress}";
        }

        if (!string.IsNullOrWhiteSpace(details.RulePrefix) && details.Endpoints != null && details.Endpoints.Length > 0)
        {
            var direction = string.IsNullOrWhiteSpace(details.Direction) ? "Outbound" : details.Direction;
            var action = string.IsNullOrWhiteSpace(details.Action) ? "Block" : details.Action;
            return $"{action} {direction}: {details.Endpoints.Length} endpoint(s) (prefix {details.RulePrefix})";
        }

        return null;
    }

    public Task<ApplyResult> ApplyAsync(ApplyCommand command, CancellationToken cancellationToken)
        => ApplyInternalAsync(command, progressCallback: null, cancellationToken);

    // Overload which reports progress via a callback. Progress percent is approximate based on number of policies processed.
    public Task<ApplyResult> ApplyAsync(ApplyCommand command, Action<int, string?>? progressCallback, CancellationToken cancellationToken)
        => ApplyInternalAsync(command, progressCallback, cancellationToken);

    private async Task<ApplyResult> ApplyInternalAsync(ApplyCommand command, Action<int, string?>? progressCallback, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var errors = new List<ErrorInfo>();

        var policies = await LoadPoliciesAsync(cancellationToken);

        // Resolve dependencies
        var policiesToApply = _dependencyResolver.ResolveDependencies(policies, command.PolicyIds);

        // Create a snapshot BEFORE applying so we can reliably undo, group changes, and support drift baselines.
        var systemInfo = await _stateCapture.GetSystemInfoAsync(cancellationToken);

        string? restorePointId = null;
        if (command.CreateRestorePoint && !command.DryRun)
        {
            restorePointId = await _restorePointManager.CreateRestorePointAsync(
                $"Privacy Hardening Framework - Apply ({DateTime.UtcNow:O})",
                cancellationToken);

            if (restorePointId == null)
            {
                warnings.Add("System restore point was requested but could not be created (System Restore may be disabled).");
            }
        }

        var snapshotDescription = !string.IsNullOrWhiteSpace(command.ProfileName)
            ? $"Apply profile: {command.ProfileName}"
            : $"Apply ({policiesToApply.Length} policies)";

        string snapshotId;
        var snapshotPersisted = false;
        try
        {
            snapshotId = await _changeLog.CreateSnapshotAsync(snapshotDescription, systemInfo, restorePointId, cancellationToken);
            snapshotPersisted = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create snapshot for apply operation");
            snapshotId = Guid.NewGuid().ToString();
            warnings.Add("Snapshot creation failed; apply will proceed without a persisted snapshot baseline.");
        }

        if (snapshotPersisted)
        {
            try
            {
                var snapshotPolicies = await CaptureSnapshotPolicyStatesAsync(policies, cancellationToken);
                await _changeLog.SaveSnapshotPoliciesAsync(snapshotId, snapshotPolicies, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save snapshot policy states for snapshot {SnapshotId}", snapshotId);
                warnings.Add("Snapshot policy state capture failed; snapshot may be incomplete.");
            }
        }

        if (command.DryRun)
        {
            warnings.Add("DryRun enabled: no changes were applied.");
            return new ApplyResult
            {
                CommandId = command.CommandId,
                Success = true,
                AppliedPolicies = Array.Empty<string>(),
                FailedPolicies = Array.Empty<string>(),
                Changes = Array.Empty<ChangeRecord>(),
                RestorePointId = restorePointId,
                SnapshotId = snapshotId,
                CompletedAt = DateTime.UtcNow,
                RestartRecommended = false,
                Errors = Array.Empty<ErrorInfo>(),
                Warnings = warnings.ToArray()
            };
        }

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
                changes.Add(WithSnapshotId(change, snapshotId));

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

                    errors.Add(new ErrorInfo
                    {
                        Code = "ApplyFailed",
                        PolicyId = policy.PolicyId,
                        Message = $"Failed to apply {policy.PolicyId}: {change.ErrorMessage ?? "Unknown error"}"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying policy: {PolicyId}", policy.PolicyId);
                failedPolicies.Add(policy.PolicyId);
                errors.Add(new ErrorInfo
                {
                    Code = "ApplyError",
                    PolicyId = policy.PolicyId,
                    Message = $"Error applying {policy.PolicyId}: {ex.Message}"
                });

                if (!command.ContinueOnError)
                {
                    break;
                }
            }

            percent = (int)(processed * 100.0 / total);
            progressCallback?.Invoke(percent, $"Completed {policy.PolicyId}");
        }

        // Save changes to log
        if (changes.Count > 0)
        {
            await _changeLog.SaveChangesAsync(changes.ToArray(), cancellationToken);
        }

        return new ApplyResult
        {
            CommandId = command.CommandId,
            Success = failedPolicies.Count == 0,
            AppliedPolicies = appliedPolicies.ToArray(),
            FailedPolicies = failedPolicies.ToArray(),
            Changes = changes.ToArray(),
            RestorePointId = restorePointId,
            SnapshotId = snapshotId,
            CompletedAt = DateTime.UtcNow,
            RestartRecommended = false,
            Errors = errors.Count > 0 ? errors.ToArray() : Array.Empty<ErrorInfo>(),
            Warnings = warnings.Count > 0 ? warnings.ToArray() : Array.Empty<string>()
        };
    }

    public async Task<RevertResult> RevertAsync(RevertCommand command, CancellationToken cancellationToken)
    {
        var policies = await LoadPoliciesAsync(cancellationToken);

        var revertedPolicies = new List<string>();
        var failedPolicies = new List<string>();
        var changes = new List<ChangeRecord>();
        var errors = new List<ErrorInfo>();
        var warnings = new List<string>();

        if (!string.IsNullOrWhiteSpace(command.SnapshotId) || !string.IsNullOrWhiteSpace(command.RestorePointId))
        {
            errors.Add(new ErrorInfo
            {
                Code = "NotImplemented",
                Message = "Revert by SnapshotId/RestorePointId is not implemented yet"
            });

            return new RevertResult
            {
                CommandId = command.CommandId,
                Success = false,
                RevertedPolicies = Array.Empty<string>(),
                FailedPolicies = Array.Empty<string>(),
                Changes = Array.Empty<ChangeRecord>(),
                CompletedAt = DateTime.UtcNow,
                Errors = errors.ToArray(),
                Warnings = Array.Empty<string>()
            };
        }

        // Resolve target policies.
        // - If PolicyIds is null/empty: revert ALL tool-applied policies (based on change history).
        // - If PolicyIds provided: revert the specified policies (if tool history exists).
        var changeByPolicyId = new Dictionary<string, ChangeRecord>(StringComparer.OrdinalIgnoreCase);
        PolicyDefinition[] policiesToRevert;

        if (command.PolicyIds == null || command.PolicyIds.Length == 0)
        {
            var allChanges = await _changeLog.GetAllChangesAsync(cancellationToken);

            var latestSuccessfulByPolicy = allChanges
                .Where(c => c.Success)
                .GroupBy(c => c.PolicyId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(c => c.AppliedAt).First())
                .ToArray();

            // Only revert policies whose latest successful change is an APPLY (not already reverted).
            foreach (var lastChange in latestSuccessfulByPolicy)
            {
                if (LooksLikeRevertChange(lastChange))
                {
                    continue;
                }

                changeByPolicyId[lastChange.PolicyId] = lastChange;
            }

            var policyIdsToRevert = changeByPolicyId.Keys.ToArray();

            if (policyIdsToRevert.Length == 0)
            {
                warnings.Add("No tool-applied policies found to revert.");
                return new RevertResult
                {
                    CommandId = command.CommandId,
                    Success = true,
                    RevertedPolicies = Array.Empty<string>(),
                    FailedPolicies = Array.Empty<string>(),
                    Changes = Array.Empty<ChangeRecord>(),
                    CompletedAt = DateTime.UtcNow,
                    Errors = Array.Empty<ErrorInfo>(),
                    Warnings = warnings.ToArray()
                };
            }

            policiesToRevert = policies.Where(p => policyIdsToRevert.Contains(p.PolicyId, StringComparer.OrdinalIgnoreCase)).ToArray();

            var missingPolicyIds = policyIdsToRevert.Except(policiesToRevert.Select(p => p.PolicyId), StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var missingPolicyId in missingPolicyIds)
            {
                failedPolicies.Add(missingPolicyId);
                errors.Add(new ErrorInfo
                {
                    Code = "PolicyDefinitionMissing",
                    PolicyId = missingPolicyId,
                    Message = $"Policy definition not found for {missingPolicyId}; cannot revert without mechanism details"
                });
            }
        }
        else
        {
            policiesToRevert = policies.Where(p => command.PolicyIds.Contains(p.PolicyId)).ToArray();

            var missingPolicyIds = command.PolicyIds.Except(policiesToRevert.Select(p => p.PolicyId), StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var missingPolicyId in missingPolicyIds)
            {
                failedPolicies.Add(missingPolicyId);
                errors.Add(new ErrorInfo
                {
                    Code = "UnknownPolicyId",
                    PolicyId = missingPolicyId,
                    Message = $"Unknown policy id: {missingPolicyId}"
                });
            }
        }

        // Order by most recently applied change first (best-effort dependency handling).
        // If we don't have a recorded change for a requested policy, it will be handled in the loop.
        var orderedPolicies = policiesToRevert
            .OrderByDescending(p =>
                changeByPolicyId.TryGetValue(p.PolicyId, out var c) ? c.AppliedAt : DateTime.MinValue)
            .ToArray();

        string? restorePointId = null;
        string? snapshotId = null;
        var snapshotPersisted = false;

        if (orderedPolicies.Length > 0)
        {
            if (command.CreateRestorePoint)
            {
                restorePointId = await _restorePointManager.CreateRestorePointAsync(
                    $"Privacy Hardening Framework - Revert ({DateTime.UtcNow:O})",
                    cancellationToken);

                if (restorePointId == null)
                {
                    warnings.Add("System restore point was requested but could not be created (System Restore may be disabled).");
                }
            }

            var systemInfo = await _stateCapture.GetSystemInfoAsync(cancellationToken);
            try
            {
                snapshotId = await _changeLog.CreateSnapshotAsync("Revert operation", systemInfo, restorePointId, cancellationToken);
                snapshotPersisted = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create snapshot for revert operation");
                snapshotId = Guid.NewGuid().ToString();
                warnings.Add("Snapshot creation failed; revert will proceed without a persisted snapshot baseline.");
            }

            if (snapshotPersisted && snapshotId != null)
            {
                try
                {
                    var snapshotPolicies = await CaptureSnapshotPolicyStatesAsync(policies, cancellationToken);
                    await _changeLog.SaveSnapshotPoliciesAsync(snapshotId, snapshotPolicies, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to save snapshot policy states for snapshot {SnapshotId}", snapshotId);
                    warnings.Add("Snapshot policy state capture failed; snapshot may be incomplete.");
                }
            }
        }

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

                // Get the most recent successful change (this is what currently "wins" for tool state)
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

                // If the last successful change is a revert, then the policy is already reverted from the tool's perspective.
                if (LooksLikeRevertChange(lastChange))
                {
                    warnings.Add($"Policy {policy.PolicyId} already reverted; skipping.");
                    continue;
                }

                // Execute revert using the executor
                var executor = _executorFactory.GetExecutor(policy.Mechanism);
                var revertChange = await executor.RevertAsync(policy, lastChange, cancellationToken);
                changes.Add(snapshotId != null ? WithSnapshotId(revertChange, snapshotId) : revertChange);

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
            RestorePointId = restorePointId,
            CompletedAt = DateTime.UtcNow,
            Errors = errors.Count > 0 ? errors.ToArray() : Array.Empty<ErrorInfo>(),
            Warnings = warnings.Count > 0 ? warnings.ToArray() : Array.Empty<string>()
        };
    }

    private static bool LooksLikeRevertChange(ChangeRecord change)
    {
        if (change.Operation == ChangeOperation.Revert)
        {
            return true;
        }

        if (change.Operation == ChangeOperation.Apply)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(change.Description))
        {
            return false;
        }

        var description = change.Description.Trim();

        // Most executors use an explicit "Reverted ..." description for revert operations.
        if (description.StartsWith("Reverted", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Firewall executor uses a "Removed firewall rules ..." description on revert.
        if (description.StartsWith("Removed firewall", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public async Task<GetStateResult> GetStateAsync(GetStateCommand command, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        var systemInfo = await _stateCapture.GetSystemInfoAsync(cancellationToken);

        var allChanges = await _changeLog.GetAllChangesAsync(cancellationToken);
        var toolAppliedPolicyIds = GetToolAppliedPolicyIds(allChanges);

        var latestSnapshot = await _changeLog.GetLatestSnapshotAsync(includeHistory: false, cancellationToken);

        var history = command.IncludeHistory ? allChanges : Array.Empty<ChangeRecord>();

        SystemSnapshot currentState;
        if (latestSnapshot != null)
        {
            currentState = new SystemSnapshot
            {
                SnapshotId = latestSnapshot.SnapshotId,
                CreatedAt = latestSnapshot.CreatedAt,
                WindowsBuild = latestSnapshot.WindowsBuild == 0 ? systemInfo.WindowsBuild : latestSnapshot.WindowsBuild,
                WindowsSku = string.IsNullOrWhiteSpace(latestSnapshot.WindowsSku) ? systemInfo.WindowsSku : latestSnapshot.WindowsSku,
                AppliedPolicies = latestSnapshot.AppliedPolicies,
                ChangeHistory = history,
                RestorePointId = latestSnapshot.RestorePointId,
                Description = latestSnapshot.Description
            };
        }
        else
        {
            warnings.Add("No snapshots found. Use CreateSnapshot, Apply, or Revert to create a baseline.");
            currentState = new SystemSnapshot
            {
                SnapshotId = Guid.NewGuid().ToString(),
                CreatedAt = DateTime.UtcNow,
                WindowsBuild = systemInfo.WindowsBuild,
                WindowsSku = systemInfo.WindowsSku,
                AppliedPolicies = Array.Empty<string>(),
                ChangeHistory = history,
                RestorePointId = null,
                Description = "Live state (no snapshots available)"
            };
        }

        return new GetStateResult
        {
            CommandId = command.CommandId,
            Success = true,
            CurrentState = currentState,
            AppliedPolicies = toolAppliedPolicyIds,
            SystemInfo = systemInfo,
            Errors = Array.Empty<ErrorInfo>(),
            Warnings = warnings.Count > 0 ? warnings.ToArray() : Array.Empty<string>()
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
        var warnings = new List<string>();
        var errors = new List<ErrorInfo>();

        var policies = await LoadPoliciesAsync(cancellationToken);
        var systemInfo = await _stateCapture.GetSystemInfoAsync(cancellationToken);

        string? restorePointId = null;
        if (command.CreateRestorePoint)
        {
            restorePointId = await _restorePointManager.CreateRestorePointAsync(
                $"Privacy Hardening Framework - Snapshot ({DateTime.UtcNow:O})",
                cancellationToken);

            if (restorePointId == null)
            {
                warnings.Add("System restore point was requested but could not be created (System Restore may be disabled).");
            }
        }

        string snapshotId;
        try
        {
            snapshotId = await _changeLog.CreateSnapshotAsync(command.Description, systemInfo, restorePointId, cancellationToken);
        }
        catch (Exception ex)
        {
            errors.Add(new ErrorInfo
            {
                Code = "SnapshotFailed",
                Message = $"Failed to create snapshot: {ex.Message}"
            });

            return new GetStateResult
            {
                CommandId = command.CommandId,
                Success = false,
                CurrentState = new SystemSnapshot
                {
                    SnapshotId = Guid.NewGuid().ToString(),
                    CreatedAt = DateTime.UtcNow,
                    WindowsBuild = systemInfo.WindowsBuild,
                    WindowsSku = systemInfo.WindowsSku,
                    AppliedPolicies = Array.Empty<string>(),
                    ChangeHistory = Array.Empty<ChangeRecord>(),
                    RestorePointId = null,
                    Description = "Snapshot creation failed"
                },
                AppliedPolicies = GetToolAppliedPolicyIds(await _changeLog.GetAllChangesAsync(cancellationToken)),
                SystemInfo = systemInfo,
                Errors = errors.ToArray(),
                Warnings = warnings.Count > 0 ? warnings.ToArray() : Array.Empty<string>()
            };
        }

        SnapshotPolicyState[] snapshotPolicies;
        try
        {
            snapshotPolicies = await CaptureSnapshotPolicyStatesAsync(policies, cancellationToken);
            await _changeLog.SaveSnapshotPoliciesAsync(snapshotId, snapshotPolicies, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Snapshot policy capture failed for snapshot {SnapshotId}", snapshotId);
            snapshotPolicies = Array.Empty<SnapshotPolicyState>();
            warnings.Add("Snapshot policy state capture failed; snapshot may be incomplete.");
        }

        var appliedAtSnapshot = snapshotPolicies
            .Where(p => p.IsApplied)
            .Select(p => p.PolicyId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new GetStateResult
        {
            CommandId = command.CommandId,
            Success = true,
            CurrentState = new SystemSnapshot
            {
                SnapshotId = snapshotId,
                CreatedAt = DateTime.UtcNow,
                WindowsBuild = systemInfo.WindowsBuild,
                WindowsSku = systemInfo.WindowsSku,
                AppliedPolicies = appliedAtSnapshot,
                ChangeHistory = Array.Empty<ChangeRecord>(),
                RestorePointId = restorePointId,
                Description = command.Description
            },
            AppliedPolicies = GetToolAppliedPolicyIds(await _changeLog.GetAllChangesAsync(cancellationToken)),
            SystemInfo = systemInfo,
            Errors = Array.Empty<ErrorInfo>(),
            Warnings = warnings.Count > 0 ? warnings.ToArray() : Array.Empty<string>()
        };
    }

    private static ChangeRecord WithSnapshotId(ChangeRecord change, string snapshotId)
    {
        if (string.Equals(change.SnapshotId, snapshotId, StringComparison.OrdinalIgnoreCase))
        {
            return change;
        }

        return new ChangeRecord
        {
            ChangeId = change.ChangeId,
            Operation = change.Operation,
            PolicyId = change.PolicyId,
            AppliedAt = change.AppliedAt,
            Mechanism = change.Mechanism,
            Description = change.Description,
            PreviousState = change.PreviousState,
            NewState = change.NewState,
            Success = change.Success,
            ErrorMessage = change.ErrorMessage,
            SnapshotId = snapshotId
        };
    }

    private static string[] GetToolAppliedPolicyIds(ChangeRecord[] allChanges)
    {
        return allChanges
            .Where(c => c.Success)
            .GroupBy(c => c.PolicyId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(c => c.AppliedAt).First())
            .Where(c => !LooksLikeRevertChange(c))
            .Select(c => c.PolicyId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<SnapshotPolicyState[]> CaptureSnapshotPolicyStatesAsync(PolicyDefinition[] policies, CancellationToken cancellationToken)
    {
        var states = new List<SnapshotPolicyState>(policies.Length);

        foreach (var policy in policies)
        {
            var isApplicable = false;
            try
            {
                isApplicable = _compatibility.IsApplicable(policy);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to evaluate applicability for policy {PolicyId}", policy.PolicyId);
            }

            if (!isApplicable)
            {
                states.Add(new SnapshotPolicyState
                {
                    PolicyId = policy.PolicyId,
                    IsApplied = false,
                    CurrentValue = "Not applicable"
                });
                continue;
            }

            try
            {
                var executor = _executorFactory.GetExecutor(policy.Mechanism);
                var isApplied = await executor.IsAppliedAsync(policy, cancellationToken);
                var currentValue = await executor.GetCurrentValueAsync(policy, cancellationToken);

                states.Add(new SnapshotPolicyState
                {
                    PolicyId = policy.PolicyId,
                    IsApplied = isApplied,
                    CurrentValue = currentValue
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to capture policy state for snapshot: {PolicyId}", policy.PolicyId);
                states.Add(new SnapshotPolicyState
                {
                    PolicyId = policy.PolicyId,
                    IsApplied = false,
                    CurrentValue = $"Error: {ex.Message}"
                });
            }
        }

        return states.ToArray();
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
