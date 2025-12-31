using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningService.Executors;

/// <summary>
/// Executes signed PowerShell script policies in constrained mode
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PowerShellExecutor : IExecutor
{
    private readonly ILogger<PowerShellExecutor> _logger;
    private readonly string _scriptsDirectory;

    public MechanismType MechanismType => MechanismType.PowerShell;

    public PowerShellExecutor(ILogger<PowerShellExecutor> logger)
    {
        _logger = logger;
        _scriptsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PrivacyHardeningFramework",
            "scripts");
    }

    public async Task<bool> IsAppliedAsync(PolicyDefinition policy, CancellationToken cancellationToken)
    {
        // PowerShell scripts don't have a standard "IsApplied" check
        await Task.CompletedTask;
        return false;
    }

    public async Task<string?> GetCurrentValueAsync(PolicyDefinition policy, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        return "PowerShell script - state varies by script";
    }

    public async Task<ChangeRecord> ApplyAsync(PolicyDefinition policy, CancellationToken cancellationToken)
    {
        var details = ParsePowerShellDetails(policy.MechanismDetails);
        if (details == null)
        {
            return CreateErrorRecord(policy, "Invalid PowerShell mechanism details");
        }

        try
        {
            var scriptPath = Path.Combine(_scriptsDirectory, details.ScriptPath);

            if (!File.Exists(scriptPath))
            {
                _logger.LogError("PowerShell script not found: {ScriptPath}", scriptPath);
                return CreateErrorRecord(policy, $"Script file not found: {scriptPath}");
            }

            // Verify signature if required
            if (details.RequiresSignature)
            {
                var isValid = await VerifyScriptSignatureAsync(scriptPath, cancellationToken);
                if (!isValid)
                {
                    _logger.LogError("Script signature validation failed: {ScriptPath}", scriptPath);
                    return CreateErrorRecord(policy, "Script signature validation failed");
                }
            }

            // Optionally capture snapshot (previous state) before applying
            string? previousState = null;
            if (!string.IsNullOrEmpty(details.SnapshotScriptPath))
            {
                var snapshotPath = Path.Combine(_scriptsDirectory, details.SnapshotScriptPath);
                if (File.Exists(snapshotPath))
                {
                    var snapRes = await ExecuteScriptAsync(snapshotPath, details.Parameters, cancellationToken);
                    if (snapRes.Success)
                    {
                        previousState = snapRes.Output;
                        _logger.LogInformation("Captured snapshot for policy {PolicyId}", policy.PolicyId);
                    }
                    else
                    {
                        _logger.LogWarning("Snapshot script failed for {PolicyId}: {Error}", policy.PolicyId, snapRes.ErrorMessage);
                    }
                }
                else
                {
                    _logger.LogWarning("Snapshot script not found: {Path}", snapshotPath);
                }
            }

            // Execute main script
            var result = await ExecuteScriptAsync(scriptPath, details.Parameters, cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation("Successfully executed PowerShell script: {ScriptPath}", details.ScriptPath);
                return new ChangeRecord
                {
                    ChangeId = Guid.NewGuid().ToString(),
                    PolicyId = policy.PolicyId,
                    AppliedAt = DateTime.UtcNow,
                    Mechanism = MechanismType.PowerShell,
                    Description = $"Executed script: {details.ScriptPath}",
                    PreviousState = previousState,
                    NewState = result.Output ?? "Script executed",
                    Success = true
                };
            }
            else
            {
                _logger.LogError("PowerShell script failed: {ScriptPath}, Error: {Error}",
                    details.ScriptPath, result.ErrorMessage);
                return CreateErrorRecord(policy, result.ErrorMessage ?? "Script execution failed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute PowerShell script for policy: {PolicyId}", policy.PolicyId);
            return CreateErrorRecord(policy, ex.Message);
        }
    }

    public async Task<ChangeRecord> RevertAsync(PolicyDefinition policy, ChangeRecord originalChange, CancellationToken cancellationToken)
    {
        var details = ParsePowerShellDetails(policy.MechanismDetails);
        if (details == null)
        {
            return CreateErrorRecord(policy, "Invalid PowerShell mechanism details");
        }

        // If a dedicated revert script is provided, run it. Pass previous state as parameter if available.
        if (!string.IsNullOrEmpty(details.RevertScriptPath))
        {
            var revertPath = Path.Combine(_scriptsDirectory, details.RevertScriptPath);
            if (!File.Exists(revertPath))
            {
                _logger.LogError("Revert script not found: {Path}", revertPath);
                return CreateErrorRecord(policy, $"Revert script not found: {revertPath}");
            }

            // Merge parameters and include PreviousState
            var mergedParams = new Dictionary<string, string>(details.Parameters ?? new Dictionary<string, string>());
            if (!string.IsNullOrEmpty(originalChange.PreviousState))
            {
                mergedParams["PreviousState"] = originalChange.PreviousState!;
            }

            var res = await ExecuteScriptAsync(revertPath, mergedParams, cancellationToken);
            if (res.Success)
            {
                _logger.LogInformation("Successfully reverted policy {PolicyId} using revert script", policy.PolicyId);
                return new ChangeRecord
                {
                    ChangeId = Guid.NewGuid().ToString(),
                    PolicyId = policy.PolicyId,
                    AppliedAt = DateTime.UtcNow,
                    Mechanism = MechanismType.PowerShell,
                    Description = $"Reverted via script: {details.RevertScriptPath}",
                    PreviousState = originalChange.PreviousState,
                    NewState = res.Output ?? "Revert executed",
                    Success = true
                };
            }
            else
            {
                _logger.LogError("Revert script failed for policy {PolicyId}: {Error}", policy.PolicyId, res.ErrorMessage);
                return CreateErrorRecord(policy, res.ErrorMessage ?? "Revert script failed");
            }
        }

        // If no revert script but we have a snapshot, attempt to restore using a generic restore script pattern (not implemented)
        if (!string.IsNullOrEmpty(originalChange.PreviousState))
        {
            _logger.LogWarning("No revert script provided; automatic restore from snapshot is not implemented for policy {PolicyId}", policy.PolicyId);
            return CreateErrorRecord(policy, "Automatic restore from snapshot not implemented; provide a revert script in mechanism details");
        }

        _logger.LogWarning("PowerShell script revert not implemented for policy: {PolicyId}", policy.PolicyId);
        return CreateErrorRecord(policy, "PowerShell script revert requires a revert script or manual intervention");
    }

    private async Task<bool> VerifyScriptSignatureAsync(string scriptPath, CancellationToken cancellationToken)
    {
        try
        {
            using var ps = PowerShell.Create();
            ps.AddCommand("Get-AuthenticodeSignature")
              .AddParameter("FilePath", scriptPath);

            var results = await Task.Run(() => ps.Invoke(), cancellationToken);

            if (results.Count > 0)
            {
                var signature = results[0];
                var status = signature.Properties["Status"]?.Value?.ToString();

                if (status == "Valid")
                {
                    _logger.LogInformation("Script signature valid: {ScriptPath}", scriptPath);
                    return true;
                }
                else
                {
                    _logger.LogWarning("Script signature status: {Status} for {ScriptPath}", status, scriptPath);
                    return false;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying script signature: {ScriptPath}", scriptPath);
            return false;
        }
    }

    private async Task<PowerShellExecutionResult> ExecuteScriptAsync(
        string scriptPath,
        Dictionary<string, string>? parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            // Create initial session state with constrained language mode
            var iss = InitialSessionState.CreateDefault();
            iss.LanguageMode = PSLanguageMode.ConstrainedLanguage;

            using var runspace = RunspaceFactory.CreateRunspace(iss);
            runspace.Open();

            using var ps = PowerShell.Create();
            ps.Runspace = runspace;

            // Load script
            var scriptContent = await File.ReadAllTextAsync(scriptPath, cancellationToken);
            ps.AddScript(scriptContent);

            // Add parameters
            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    // Expand environment variables in parameter values
                    var expandedValue = Environment.ExpandEnvironmentVariables(param.Value);
                    ps.AddParameter(param.Key, expandedValue);
                }
            }

            // Set timeout
            var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(5));

            // Execute
            var results = await Task.Run(() => ps.Invoke(), timeoutCts.Token);

            // Collect output
            var output = string.Join("\n", results.Select(r => r?.ToString() ?? ""));

            // Check for errors
            if (ps.HadErrors)
            {
                var errors = string.Join("\n", ps.Streams.Error.Select(e => e.ToString()));
                _logger.LogError("PowerShell script errors: {Errors}", errors);
                return new PowerShellExecutionResult
                {
                    Success = false,
                    Output = output,
                    ErrorMessage = errors
                };
            }

            return new PowerShellExecutionResult
            {
                Success = true,
                Output = output,
                ErrorMessage = null
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("PowerShell script execution timed out: {ScriptPath}", scriptPath);
            return new PowerShellExecutionResult
            {
                Success = false,
                Output = null,
                ErrorMessage = "Script execution timed out after 5 minutes"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PowerShell script execution failed: {ScriptPath}", scriptPath);
            return new PowerShellExecutionResult
            {
                Success = false,
                Output = null,
                ErrorMessage = ex.Message
            };
        }
    }

    private PowerShellDetails? ParsePowerShellDetails(object mechanismDetails)
    {
        try
        {
            var json = JsonSerializer.Serialize(mechanismDetails);
            return JsonSerializer.Deserialize<PowerShellDetails>(json);
        }
        catch
        {
            return null;
        }
    }

    private ChangeRecord CreateErrorRecord(PolicyDefinition policy, string error)
    {
        return new ChangeRecord
        {
            ChangeId = Guid.NewGuid().ToString(),
            PolicyId = policy.PolicyId,
            AppliedAt = DateTime.UtcNow,
            Mechanism = MechanismType.PowerShell,
            Description = "Failed to execute PowerShell script",
            PreviousState = null,
            NewState = "[error]",
            Success = false,
            ErrorMessage = error
        };
    }
}

internal sealed class PowerShellDetails
{
    public required string ScriptPath { get; init; }
    public Dictionary<string, string>? Parameters { get; init; }
    public bool RequiresSignature { get; init; }
    // Optional path to a script that captures current state prior to applying (relative to scripts dir)
    public string? SnapshotScriptPath { get; init; }
    // Optional path to a script used to revert changes (relative to scripts dir)
    public string? RevertScriptPath { get; init; }
}

internal sealed class PowerShellExecutionResult
{
    public required bool Success { get; init; }
    public string? Output { get; init; }
    public string? ErrorMessage { get; init; }
}
