using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
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

            if (task == null)
            {
                // If action is Delete, strict application means task is gone
                return details.Action == TaskAction.Delete;
            }
            
            // If task exists but action is Delete, then not applied
            if (details.Action == TaskAction.Delete) return false;

            // If action is ModifyTriggers, check if triggers are empty?
            // This is a naive check, real implementation might need more depth
            if (details.Action == TaskAction.ModifyTriggers)
            {
                return task.Definition.Triggers.Count == 0;
            }

            var expectedState = details.Action != TaskAction.Disable; // i.e. Enable
            if (details.Action == TaskAction.Disable) expectedState = false;
            
            // For simple Enable/Disable
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

            return $"Enabled={task.Enabled}, State={task.State}, Triggers={task.Definition.Triggers.Count}, LastRunTime={task.LastRunTime}";
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
            return CreateErrorRecord(policy, ChangeOperation.Apply, "Invalid task mechanism details");
        }

        try
        {
            using var ts = new TaskService();
            var task = ts.GetTask(details.TaskPath);

            if (task == null)
            {
                _logger.LogWarning("Task not found: {TaskPath}", details.TaskPath);
                return CreateErrorRecord(policy, ChangeOperation.Apply, $"Scheduled task '{details.TaskPath}' does not exist on this system");
            }

            // Capture previous state
            var snapshot = new TaskStateSnapshot
            {
                Enabled = task.Enabled,
                State = task.State.ToString(), 
                TriggerCount = task.Definition.Triggers.Count,
                XmlDefinition =  (details.Action == TaskAction.Delete || details.Action == TaskAction.ModifyTriggers) 
                    ? task.Xml 
                    : null
            };
            
            var previousState = JsonSerializer.Serialize(snapshot);

            // Apply action
            var description = $"Modified task: {details.TaskPath}";
            bool applied = false;

            if (details.Action == TaskAction.Delete)
            {
                // Export before delete if requested
                if (details.ExportTaskDefinition && !string.IsNullOrEmpty(details.ExportPath))
                {
                    try 
                    {
                        File.WriteAllText(details.ExportPath, task.Xml);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to export task definition before delete");
                    }
                }
                
                var folder = task.Folder;
                folder.DeleteTask(task.Name);
                description = $"Deleted task: {details.TaskPath}";
                applied = true;
            }
            else if (details.Action == TaskAction.ModifyTriggers)
            {
                task.Definition.Triggers.Clear();
                task.RegisterChanges();
                description = $"Cleared triggers for task: {details.TaskPath}";
                applied = true;
            }
            else
            {
                // Enable/Disable
                var shouldBeEnabled = details.Action != TaskAction.Disable;
                task.Enabled = shouldBeEnabled;
                description = $"Set task Enabled={shouldBeEnabled}: {details.TaskPath}";
                applied = true;
            }

            _logger.LogInformation("{Description}", description);

            return new ChangeRecord
            {
                ChangeId = Guid.NewGuid().ToString(),
                Operation = ChangeOperation.Apply,
                PolicyId = policy.PolicyId,
                AppliedAt = DateTime.UtcNow,
                Mechanism = MechanismType.ScheduledTask,
                Description = description,
                PreviousState = previousState,
                NewState = details.Action.ToString(),
                Success = true
            };
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogError("Access denied modifying task: {TaskPath}", details.TaskPath);
            return CreateErrorRecord(policy, ChangeOperation.Apply, $"Access denied to task '{details.TaskPath}' (may be protected by Tamper Protection)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to modify task: {TaskPath}", details.TaskPath);
            return CreateErrorRecord(policy, ChangeOperation.Apply, ex.Message);
        }
    }

    public async System.Threading.Tasks.Task<ChangeRecord> RevertAsync(PolicyDefinition policy, ChangeRecord originalChange, CancellationToken cancellationToken)
    {
        await System.Threading.Tasks.Task.CompletedTask;

        var details = ParseTaskDetails(policy.MechanismDetails);
        if (details == null)
        {
            return CreateErrorRecord(policy, ChangeOperation.Revert, "Invalid task mechanism details");
        }

        try
        {
            if (string.IsNullOrEmpty(originalChange.PreviousState))
            {
                return CreateErrorRecord(policy, ChangeOperation.Revert, "No previous state recorded, cannot revert");
            }

            TaskStateSnapshot? snapshot = null;
            bool isLegacy = false;

            try 
            {
                snapshot = JsonSerializer.Deserialize<TaskStateSnapshot>(originalChange.PreviousState);
            }
            catch 
            {
                isLegacy = true;
            }

            // Fallback for legacy state string
            if (isLegacy || snapshot == null)
            {
                var previousEnabled = ExtractEnabledStateFromState(originalChange.PreviousState);
                if (previousEnabled == null)
                {
                    return CreateErrorRecord(policy, ChangeOperation.Revert, "Could not parse previous enabled state from legacy record");
                }
                snapshot = new TaskStateSnapshot { Enabled = previousEnabled.Value };
            }

            using var ts = new TaskService();
            var task = ts.GetTask(details.TaskPath);

            // CASE 1: Task Missing (Deleted)
            if (task == null)
            {
                if (!string.IsNullOrEmpty(snapshot.XmlDefinition))
                {
                    // Restore from XML
                    // Need to determine folder path from "TaskPath" (e.g. \Microsoft\Windows\Customer Experience Improvement Program\Consolidator)
                    // The TaskScheduler library usually handles paths in RegisterTaskDefinition
                    
                     ts.RootFolder.RegisterTaskDefinition(
                        details.TaskPath,
                        ts.NewTask(), // Temp definition to parse? No, we need LoadXml
                        TaskCreation.CreateOrUpdate, 
                        null, 
                        null, 
                        TaskLogonType.None
                    );
                    
                    // The above creates a blank one. We need to load XML.
                    // Instead:
                    // var newTask = ts.NewTask();
                    // newTask.Xml = snapshot.XmlDefinition; -- Property is read-only usually?
                    // Microsoft.Win32.TaskScheduler helper:
                    var newTask = ts.NewTask();
                    newTask.XmlText = snapshot.XmlDefinition; // This sets definition from XML
                    
                    ts.RootFolder.RegisterTaskDefinition(
                        details.TaskPath,
                        newTask,
                        TaskCreation.CreateOrUpdate,
                        null,
                        null,
                        TaskLogonType.None
                    );
                    
                     _logger.LogInformation("Restored deleted task: {TaskPath}", details.TaskPath);
                }
                else
                {
                    return CreateErrorRecord(policy, ChangeOperation.Revert, $"Task '{details.TaskPath}' is missing and no XML definition was saved to restore it.");
                }
            }
            else
            {
                // CASE 2: Task Exists
                // If we have XML (e.g. we modified triggers), restoring XML is safest to get back original triggers
                if (!string.IsNullOrEmpty(snapshot.XmlDefinition))
                {
                     var restoreTask = ts.NewTask();
                     restoreTask.XmlText = snapshot.XmlDefinition;
                     
                     ts.RootFolder.RegisterTaskDefinition(
                        details.TaskPath,
                        restoreTask,
                        TaskCreation.CreateOrUpdate,
                        null,
                        null,
                        TaskLogonType.None
                    );
                    _logger.LogInformation("Restored task definition: {TaskPath}", details.TaskPath);
                }
                else
                {
                    // Just restore Enabled state
                    task.Enabled = snapshot.Enabled;
                    _logger.LogInformation("Reverted scheduled task {TaskPath} to Enabled={Enabled}",
                        details.TaskPath, snapshot.Enabled);
                }
            }

            return new ChangeRecord
            {
                ChangeId = Guid.NewGuid().ToString(),
                Operation = ChangeOperation.Revert,
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
            return CreateErrorRecord(policy, ChangeOperation.Revert, ex.Message);
        }
    }

    internal TaskDetails? ParseTaskDetails(object mechanismDetails)
    {
        try
        {
            var json = JsonSerializer.Serialize(mechanismDetails);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            // Check if granular (has "ActionOptions" or "type: ScheduledTaskConfiguration")
            // The YAML loader uses camelCase property names usually
            var isGranular = root.TryGetProperty("type", out var typeProp) && 
                             typeProp.GetString()?.Equals("ScheduledTaskConfiguration", StringComparison.OrdinalIgnoreCase) == true;

            var taskPath = GetStringProperty(root, "taskPath");
            if (string.IsNullOrEmpty(taskPath)) return null;

            if (isGranular)
            {
                // Granular parsing
                // We expect "Action" to be resolved to the selected option value if passed from UI
                // OR we need to look at defaults. 
                // For now, let's assume valid JSON structure matching contracts
                var options = JsonSerializer.Deserialize<TaskConfigOptions>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });

                if (options == null) return null;

                TaskAction finalAction = TaskAction.Disable;
                if (options.Action.SelectedValue is { } selected)
                {
                    finalAction = selected;
                }
                else if (options.Action.RecommendedValue is { } recommended)
                {
                    finalAction = recommended;
                }

                return new TaskDetails
                {
                    TaskPath = taskPath,
                    Action = finalAction,
                    ExportTaskDefinition = options.ExportTaskDefinition,
                    ExportPath = options.ExportPath
                };
            }
            else
            {
                // Legacy parsing
                var actionStr = GetStringProperty(root, "action");
                var action = TaskAction.Disable; // Default safe fallback
                
                if (string.Equals(actionStr, "Disable", StringComparison.OrdinalIgnoreCase)) action = TaskAction.Disable;
                else if (string.Equals(actionStr, "Enable", StringComparison.OrdinalIgnoreCase)) action = TaskAction.Enable;
                
                return new TaskDetails
                {
                    TaskPath = taskPath,
                    Action = action
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse task details");
            return null;
        }
    }

    private string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) || 
            element.TryGetProperty(propertyName.ToLowerInvariant(), out prop))
        {
            return prop.GetString();
        }
        return null;
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

    private ChangeRecord CreateErrorRecord(PolicyDefinition policy, ChangeOperation operation, string error)
    {
        return new ChangeRecord
        {
            ChangeId = Guid.NewGuid().ToString(),
            Operation = operation,
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

internal sealed class TaskStateSnapshot
{
    public bool Enabled { get; set; }
    public string? State { get; set; }
    public int TriggerCount { get; set; }
    public string? XmlDefinition { get; set; }
}

internal sealed class TaskDetails
{
    public required string TaskPath { get; init; }
    public TaskAction Action { get; init; }
    public bool ExportTaskDefinition { get; init; }
    public string? ExportPath { get; init; }
}
