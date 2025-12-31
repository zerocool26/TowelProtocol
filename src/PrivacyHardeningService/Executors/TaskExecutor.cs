using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.TaskScheduler;
using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningService.Executors;

/// <summary>
/// Executes Scheduled Task configuration policies
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TaskExecutor : IExecutor
{
    private readonly ILogger<TaskExecutor> _logger;

    public MechanismType MechanismType => MechanismType.ScheduledTask;

    public TaskExecutor(ILogger<TaskExecutor> logger)
    {
        _logger = logger;
    }

    public async System.Threading.Tasks.Task<bool> IsAppliedAsync(PolicyDefinition policy, CancellationToken cancellationToken)
    {
        await System.Threading.Tasks.Task.CompletedTask; // Task operations are synchronous

        var details = ParseTaskDetails(policy.MechanismDetails);
        if (details == null) return false;

        try
        {
            using var ts = new TaskService();
            var task = ts.GetTask(details.TaskPath);

            if (task == null) return false;

            var expectedState = details.Action.ToLowerInvariant() == "disable" ? false : true;
            return task.Enabled == expectedState;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check task status: {TaskPath}", details.TaskPath);
            return false;
        }
    }

    public async System.Threading.Tasks.Task<string?> GetCurrentValueAsync(PolicyDefinition policy, CancellationToken cancellationToken)
    {
        await System.Threading.Tasks.Task.CompletedTask;

        var details = ParseTaskDetails(policy.MechanismDetails);
        if (details == null) return null;

        try
        {
            using var ts = new TaskService();
            var task = ts.GetTask(details.TaskPath);

            if (task == null)
            {
                return "Task not found";
            }

            return $"Enabled={task.Enabled}, State={task.State}, LastRunTime={task.LastRunTime}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read task state: {TaskPath}", details.TaskPath);
            return null;
        }
    }

    public async System.Threading.Tasks.Task<ChangeRecord> ApplyAsync(PolicyDefinition policy, CancellationToken cancellationToken)
    {
        await System.Threading.Tasks.Task.CompletedTask;

        var details = ParseTaskDetails(policy.MechanismDetails);
        if (details == null)
        {
            return CreateErrorRecord(policy, "Invalid task mechanism details");
        }

        try
        {
            using var ts = new TaskService();
            var task = ts.GetTask(details.TaskPath);

            if (task == null)
            {
                _logger.LogWarning("Task not found: {TaskPath}", details.TaskPath);
                return CreateErrorRecord(policy, $"Scheduled task '{details.TaskPath}' does not exist on this system");
            }

            // Capture previous state
            var previousEnabled = task.Enabled;
            var previousState = $"Enabled={previousEnabled}, State={task.State}";

            // Apply action
            var shouldBeEnabled = details.Action.ToLowerInvariant() != "disable";
            task.Enabled = shouldBeEnabled;

            _logger.LogInformation("Modified scheduled task {TaskPath}: Enabled={OldState} -> {NewState}",
                details.TaskPath, previousEnabled, shouldBeEnabled);

            var newState = $"Enabled={shouldBeEnabled}";

            return new ChangeRecord
            {
                ChangeId = Guid.NewGuid().ToString(),
                PolicyId = policy.PolicyId,
                AppliedAt = DateTime.UtcNow,
                Mechanism = MechanismType.ScheduledTask,
                Description = $"Modified task: {details.TaskPath}",
                PreviousState = previousState,
                NewState = newState,
                Success = true
            };
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogError("Access denied modifying task: {TaskPath}", details.TaskPath);
            return CreateErrorRecord(policy, $"Access denied to task '{details.TaskPath}' (may be protected by Tamper Protection)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to modify task: {TaskPath}", details.TaskPath);
            return CreateErrorRecord(policy, ex.Message);
        }
    }

    public async System.Threading.Tasks.Task<ChangeRecord> RevertAsync(PolicyDefinition policy, ChangeRecord originalChange, CancellationToken cancellationToken)
    {
        await System.Threading.Tasks.Task.CompletedTask;

        var details = ParseTaskDetails(policy.MechanismDetails);
        if (details == null)
        {
            return CreateErrorRecord(policy, "Invalid task mechanism details");
        }

        try
        {
            if (string.IsNullOrEmpty(originalChange.PreviousState))
            {
                return CreateErrorRecord(policy, "No previous state recorded, cannot revert");
            }

            // Parse previous enabled state
            var previousEnabled = ExtractEnabledStateFromState(originalChange.PreviousState);
            if (previousEnabled == null)
            {
                return CreateErrorRecord(policy, "Could not parse previous enabled state");
            }

            using var ts = new TaskService();
            var task = ts.GetTask(details.TaskPath);

            if (task == null)
            {
                return CreateErrorRecord(policy, $"Task '{details.TaskPath}' no longer exists");
            }

            task.Enabled = previousEnabled.Value;

            _logger.LogInformation("Reverted scheduled task {TaskPath} to Enabled={Enabled}",
                details.TaskPath, previousEnabled.Value);

            return new ChangeRecord
            {
                ChangeId = Guid.NewGuid().ToString(),
                PolicyId = policy.PolicyId,
                AppliedAt = DateTime.UtcNow,
                Mechanism = MechanismType.ScheduledTask,
                Description = $"Reverted task: {details.TaskPath}",
                PreviousState = originalChange.NewState,
                NewState = originalChange.PreviousState,
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revert task: {TaskPath}", details.TaskPath);
            return CreateErrorRecord(policy, ex.Message);
        }
    }

    private TaskDetails? ParseTaskDetails(object mechanismDetails)
    {
        try
        {
            var json = JsonSerializer.Serialize(mechanismDetails);
            return JsonSerializer.Deserialize<TaskDetails>(json);
        }
        catch
        {
            return null;
        }
    }

    private bool? ExtractEnabledStateFromState(string state)
    {
        // Parse "Enabled=True, State=Running" format
        var parts = state.Split(',');
        var enabledPart = parts.FirstOrDefault(p => p.Trim().StartsWith("Enabled="));
        if (enabledPart == null) return null;

        var value = enabledPart.Split('=')[1].Trim();
        return bool.TryParse(value, out var enabled) ? enabled : null;
    }

    private ChangeRecord CreateErrorRecord(PolicyDefinition policy, string error)
    {
        return new ChangeRecord
        {
            ChangeId = Guid.NewGuid().ToString(),
            PolicyId = policy.PolicyId,
            AppliedAt = DateTime.UtcNow,
            Mechanism = MechanismType.ScheduledTask,
            Description = "Failed to apply task policy",
            PreviousState = null,
            NewState = "[error]",
            Success = false,
            ErrorMessage = error
        };
    }
}

internal sealed class TaskDetails
{
    public required string TaskPath { get; init; }
    public required string Action { get; init; }
}
