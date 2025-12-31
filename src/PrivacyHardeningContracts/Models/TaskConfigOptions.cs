namespace PrivacyHardeningContracts.Models;

/// <summary>
/// Granular configuration options for scheduled tasks.
/// Provides multiple action choices beyond simple enable/disable.
/// </summary>
public sealed record TaskConfigOptions
{
    /// <summary>
    /// Primary action to take on the task
    /// </summary>
    public required SelectableOption<TaskAction> Action { get; init; }

    /// <summary>
    /// If action is ModifyTriggers, these are the available trigger options
    /// </summary>
    public TaskTriggerOption[]? TriggerOptions { get; init; }

    /// <summary>
    /// Whether to export task definition before modification (for backup)
    /// </summary>
    public bool ExportTaskDefinition { get; init; } = true;

    /// <summary>
    /// Path to export task definition XML
    /// </summary>
    public string? ExportPath { get; init; }
}

/// <summary>
/// Actions that can be taken on a scheduled task
/// </summary>
public enum TaskAction
{
    /// <summary>
    /// Disable the task (can be re-enabled)
    /// </summary>
    Disable,

    /// <summary>
    /// Enable the task
    /// </summary>
    Enable,

    /// <summary>
    /// Delete the task permanently (requires restore point to undo)
    /// </summary>
    Delete,

    /// <summary>
    /// Keep task but remove all triggers (task exists but never runs)
    /// </summary>
    ModifyTriggers,

    /// <summary>
    /// Export task definition to XML file (no modification)
    /// </summary>
    ExportOnly
}

/// <summary>
/// Represents a trigger that can be enabled/disabled on a scheduled task
/// </summary>
public sealed record TaskTriggerOption
{
    /// <summary>
    /// Unique identifier for this trigger
    /// </summary>
    public required string TriggerId { get; init; }

    /// <summary>
    /// User-friendly description of when this trigger fires
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Whether this trigger is currently enabled
    /// </summary>
    public bool CurrentlyEnabled { get; init; }

    /// <summary>
    /// Whether user can disable this trigger
    /// </summary>
    public bool UserCanDisable { get; init; } = true;

    /// <summary>
    /// Trigger type (Daily, On Idle, On Logon, etc.)
    /// </summary>
    public string? TriggerType { get; init; }

    /// <summary>
    /// Schedule details (e.g., "Daily at 3:00 AM", "On system idle")
    /// </summary>
    public string? Schedule { get; init; }
}
