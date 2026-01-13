using System.Diagnostics;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningService.Executors;

/// <summary>
/// Executes signed PowerShell script policies using Windows PowerShell (powershell.exe)
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

        // Prefer repo-local scripts when running from source; fall back to ProgramData when installed.
        var baseDir = AppContext.BaseDirectory;
        var projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        var repoScripts = Path.Combine(projectRoot, "scripts");

        _scriptsDirectory = Directory.Exists(repoScripts)
            ? repoScripts
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "PrivacyHardeningFramework",
                "scripts");

        _scriptsDirectory = Path.GetFullPath(_scriptsDirectory);
    }

    public async Task<bool> IsAppliedAsync(PolicyDefinition policy, CancellationToken cancellationToken)
    {
        var (isApplied, _) = await TryAuditFromVerificationCommandAsync(policy, cancellationToken);
        return isApplied;
    }

    public async Task<string?> GetCurrentValueAsync(PolicyDefinition policy, CancellationToken cancellationToken)
    {
        var (_, currentValue) = await TryAuditFromVerificationCommandAsync(policy, cancellationToken);
        return currentValue ?? "PowerShell script - audit not available";
    }

    public async Task<ChangeRecord> ApplyAsync(PolicyDefinition policy, CancellationToken cancellationToken)
    {
        var details = ParsePowerShellDetails(policy.MechanismDetails);
        if (details == null)
        {
            return CreateErrorRecord(policy, ChangeOperation.Apply, "Invalid PowerShell mechanism details");
        }

        try
        {
            var scriptPath = ResolveScriptPath(details.ScriptPath);

            if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
            {
                _logger.LogError("PowerShell script not found: {ScriptPath}", scriptPath);
                return CreateErrorRecord(policy, ChangeOperation.Apply, $"Script file not found: {scriptPath}");
            }

            // Verify signature if required
            if (details.RequiresSignature)
            {
                var isValid = await VerifyScriptSignatureAsync(scriptPath, cancellationToken);
                if (!isValid)
                {
                    _logger.LogError("Script signature validation failed: {ScriptPath}", scriptPath);
                    return CreateErrorRecord(policy, ChangeOperation.Apply, "Script signature validation failed");
                }
            }

            // Optionally capture snapshot (previous state) before applying
            string? previousState = null;
            if (!string.IsNullOrEmpty(details.SnapshotScriptPath))
            {
                var snapshotPath = ResolveScriptPath(details.SnapshotScriptPath);
                if (!string.IsNullOrWhiteSpace(snapshotPath) && File.Exists(snapshotPath))
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
                    Operation = ChangeOperation.Apply,
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
                return CreateErrorRecord(policy, ChangeOperation.Apply, result.ErrorMessage ?? "Script execution failed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute PowerShell script for policy: {PolicyId}", policy.PolicyId);
            return CreateErrorRecord(policy, ChangeOperation.Apply, ex.Message);
        }
    }

    public async Task<ChangeRecord> RevertAsync(PolicyDefinition policy, ChangeRecord originalChange, CancellationToken cancellationToken)
    {
        var details = ParsePowerShellDetails(policy.MechanismDetails);
        if (details == null)
        {
            return CreateErrorRecord(policy, ChangeOperation.Revert, "Invalid PowerShell mechanism details");
        }

        // If a dedicated revert script is provided, run it. Pass previous state as parameter if available.
        if (!string.IsNullOrEmpty(details.RevertScriptPath))
        {
            var revertPath = ResolveScriptPath(details.RevertScriptPath);
            if (string.IsNullOrWhiteSpace(revertPath) || !File.Exists(revertPath))
            {
                _logger.LogError("Revert script not found: {Path}", revertPath);
                return CreateErrorRecord(policy, ChangeOperation.Revert, $"Revert script not found: {revertPath}");
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
                    Operation = ChangeOperation.Revert,
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
                return CreateErrorRecord(policy, ChangeOperation.Revert, res.ErrorMessage ?? "Revert script failed");
            }
        }

        // If no revert script but we have a snapshot, attempt to restore using a generic restore script pattern (not implemented)
        if (!string.IsNullOrEmpty(originalChange.PreviousState))
        {
            _logger.LogWarning("No revert script provided; automatic restore from snapshot is not implemented for policy {PolicyId}", policy.PolicyId);
            return CreateErrorRecord(policy, ChangeOperation.Revert, "Automatic restore from snapshot not implemented; provide a revert script in mechanism details");
        }

        _logger.LogWarning("PowerShell script revert not implemented for policy: {PolicyId}", policy.PolicyId);
        return CreateErrorRecord(policy, ChangeOperation.Revert, "PowerShell script revert requires a revert script or manual intervention");
    }

    private async Task<bool> VerifyScriptSignatureAsync(string scriptPath, CancellationToken cancellationToken)
    {
        try
        {
            var psi = CreatePowerShellProcessStartInfo();
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add("& { param([string]$p) (Get-AuthenticodeSignature -LiteralPath $p).Status.ToString() }");
            psi.ArgumentList.Add(scriptPath);

            var res = await RunProcessAsync(psi, TimeSpan.FromSeconds(30), cancellationToken);
            if (!res.Success)
            {
                _logger.LogWarning("Script signature verification failed: {ScriptPath}. {Error}", scriptPath, res.ErrorMessage);
                return false;
            }

            var status = (res.Output ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? string.Empty;

            if (status.Equals("Valid", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Script signature valid: {ScriptPath}", scriptPath);
                return true;
            }

            _logger.LogWarning("Script signature status: {Status} for {ScriptPath}", status, scriptPath);
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
            var psi = CreatePowerShellProcessStartInfo();
            psi.WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? _scriptsDirectory;

            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(scriptPath);

            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    if (!IsSafePowerShellParameterName(param.Key))
                    {
                        return new PowerShellExecutionResult
                        {
                            Success = false,
                            Output = null,
                            ErrorMessage = $"Unsafe PowerShell parameter name: '{param.Key}'"
                        };
                    }

                    psi.ArgumentList.Add("-" + param.Key);
                    psi.ArgumentList.Add(Environment.ExpandEnvironmentVariables(param.Value));
                }
            }

            var res = await RunProcessAsync(psi, TimeSpan.FromMinutes(5), cancellationToken);
            return new PowerShellExecutionResult
            {
                Success = res.Success,
                Output = res.Output,
                ErrorMessage = res.Success ? null : res.ErrorMessage
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("PowerShell script execution cancelled: {ScriptPath}", scriptPath);
            return new PowerShellExecutionResult
            {
                Success = false,
                Output = null,
                ErrorMessage = "Script execution cancelled"
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

    private string? ResolveScriptPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var normalized = relativePath.Trim()
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        // Policies may store paths that already include the "scripts/" prefix; normalize to our scripts root.
        var prefix = "scripts" + Path.DirectorySeparatorChar;
        if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(prefix.Length);
        }

        var fullPath = Path.GetFullPath(Path.Combine(_scriptsDirectory, normalized));
        return IsUnderRoot(fullPath, _scriptsDirectory) ? fullPath : null;
    }

    private static bool IsUnderRoot(string fullPath, string rootFullPath)
    {
        var relative = Path.GetRelativePath(rootFullPath, fullPath);
        return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private static bool IsSafePowerShellParameterName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        // Conservative: disallow anything that could be interpreted as an expression or another switch.
        foreach (var ch in name)
        {
            if (!(char.IsLetterOrDigit(ch) || ch == '_'))
            {
                return false;
            }
        }

        return true;
    }

    private static ProcessStartInfo CreatePowerShellProcessStartInfo()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");

        return psi;
    }

    private static async Task<ExternalProcessResult> RunProcessAsync(ProcessStartInfo psi, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best-effort.
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            var stdoutTimedOut = await SafeReadToEndAsync(stdoutTask);
            var stderrTimedOut = await SafeReadToEndAsync(stderrTask);
            return new ExternalProcessResult(
                Success: false,
                Output: stdoutTimedOut,
                ErrorMessage: $"Script execution timed out after {timeout.TotalMinutes:0} minutes. {stderrTimedOut}".Trim());
        }

        var stdout = await SafeReadToEndAsync(stdoutTask);
        var stderr = await SafeReadToEndAsync(stderrTask);

        if (process.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(stderr) ? $"PowerShell exited with code {process.ExitCode}." : stderr.Trim();
            return new ExternalProcessResult(false, stdout, error);
        }

        return new ExternalProcessResult(true, stdout, null);
    }

    private static async Task<string> SafeReadToEndAsync(Task<string> task)
    {
        try
        {
            return await task;
        }
        catch
        {
            return string.Empty;
        }
    }

    private PowerShellDetails? ParsePowerShellDetails(object mechanismDetails)
    {
        try
        {
            var json = JsonSerializer.Serialize(mechanismDetails);
            return JsonSerializer.Deserialize<PowerShellDetails>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    private async Task<(bool IsApplied, string? CurrentValue)> TryAuditFromVerificationCommandAsync(PolicyDefinition policy, CancellationToken cancellationToken)
    {
        var verification = policy.VerificationCommand;
        if (string.IsNullOrWhiteSpace(verification))
        {
            return (false, null);
        }

        var normalized = verification.Replace("\r", " ").Replace("\n", " ").Trim();

        // Pattern 1: Get-Service <svc1,svc2,...> | Select-Object ...
        var services = TryParseGetServiceNames(normalized);
        if (services != null && services.Length > 0)
        {
            return await AuditServicesDisabledAsync(services, cancellationToken);
        }

        // Pattern 2: Get-NetFirewallRule -DisplayName '<pattern>' | Measure-Object
        var displayNamePattern = TryParseFirewallDisplayNamePattern(normalized);
        if (!string.IsNullOrWhiteSpace(displayNamePattern))
        {
            return await AuditFirewallRuleDisplayNamePatternAsync(displayNamePattern, cancellationToken);
        }

        // Not a recognized safe pattern.
        return (false, "Audit not supported for this PowerShell policy (unrecognized verificationCommand).");
    }

    private static string[]? TryParseGetServiceNames(string command)
    {
        if (!command.StartsWith("Get-Service ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var remainder = command.Substring("Get-Service ".Length).Trim();
        var pipeIdx = remainder.IndexOf('|');
        if (pipeIdx >= 0)
        {
            remainder = remainder.Substring(0, pipeIdx).Trim();
        }

        if (string.IsNullOrWhiteSpace(remainder))
        {
            return null;
        }

        return remainder
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();
    }

    private static string? TryParseFirewallDisplayNamePattern(string command)
    {
        if (!command.StartsWith("Get-NetFirewallRule", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var displayNameTokenIdx = command.IndexOf("-DisplayName", StringComparison.OrdinalIgnoreCase);
        if (displayNameTokenIdx < 0)
        {
            return null;
        }

        var remainder = command.Substring(displayNameTokenIdx + "-DisplayName".Length).Trim();
        if (remainder.Length == 0)
        {
            return null;
        }

        if (remainder[0] == '\'' || remainder[0] == '"')
        {
            var quote = remainder[0];
            var endIdx = remainder.IndexOf(quote, 1);
            if (endIdx > 1)
            {
                return remainder.Substring(1, endIdx - 1);
            }
        }

        // Unquoted: take until whitespace or pipe
        var cutIdx = remainder.IndexOfAny(new[] { ' ', '\t', '|' });
        if (cutIdx < 0)
        {
            return remainder;
        }

        return remainder.Substring(0, cutIdx).Trim();
    }

    private async Task<(bool IsApplied, string? CurrentValue)> AuditServicesDisabledAsync(string[] serviceNames, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var allDisabledAndStopped = true;
        var parts = new List<string>(serviceNames.Length);

        foreach (var serviceName in serviceNames)
        {
            try
            {
                var startMode = GetServiceStartupType(serviceName);
                using var sc = new ServiceController(serviceName);
                sc.Refresh();

                parts.Add($"{serviceName}: StartType={startMode}, Status={sc.Status}");

                if (startMode != ServiceStartMode.Disabled || sc.Status != ServiceControllerStatus.Stopped)
                {
                    allDisabledAndStopped = false;
                }
            }
            catch (InvalidOperationException)
            {
                // Service not found: treat as effectively disabled, but call it out explicitly.
                parts.Add($"{serviceName}: NotFound");
            }
            catch (Exception ex)
            {
                parts.Add($"{serviceName}: Error={ex.Message}");
                allDisabledAndStopped = false;
            }
        }

        return (allDisabledAndStopped, string.Join("; ", parts));
    }

    private Task<(bool IsApplied, string? CurrentValue)> AuditFirewallRuleDisplayNamePatternAsync(string displayNamePattern, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (currentProfiles, rules) = FirewallCom.GetRuleSnapshots();
            var matcher = FirewallCom.CreateDisplayNameMatcher(displayNamePattern);

            var matching = rules
                .Where(r =>
                    r.Enabled
                    && r.Action == FirewallCom.NetFwActionBlock
                    && r.Direction == FirewallCom.NetFwDirectionOut
                    && FirewallCom.IsRuleEffectiveForCurrentProfile(r.Profiles, currentProfiles)
                    && matcher(r.Name))
                .ToList();

            var count = matching.Count;
            var examples = matching
                .Select(r => r.Name)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToArray();

            var current = count > 0
                ? $"{count} enabled block firewall rules matched DisplayName='{displayNamePattern}'" +
                  (examples.Length > 0 ? $"; examples: {string.Join(", ", examples)}" : string.Empty)
                : $"0 enabled block firewall rules matched DisplayName='{displayNamePattern}'";

            return Task.FromResult<(bool IsApplied, string? CurrentValue)>((count > 0, current));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to audit firewall rules for display name pattern {Pattern}", displayNamePattern);
            return Task.FromResult<(bool IsApplied, string? CurrentValue)>((false, $"Error auditing firewall rules: {ex.Message}"));
        }
    }

    private static ServiceStartMode GetServiceStartupType(string serviceName)
    {
        var keyPath = $@"SYSTEM\CurrentControlSet\Services\{serviceName}";
        using var key = Registry.LocalMachine.OpenSubKey(keyPath, false);

        if (key == null)
        {
            throw new InvalidOperationException($"Service '{serviceName}' does not exist");
        }

        var startValue = (int?)key.GetValue("Start");
        if (startValue == null)
        {
            throw new InvalidOperationException($"Could not read Start value for service '{serviceName}'");
        }

        return startValue.Value switch
        {
            0 => ServiceStartMode.Boot,
            1 => ServiceStartMode.System,
            2 => ServiceStartMode.Automatic,
            3 => ServiceStartMode.Manual,
            4 => ServiceStartMode.Disabled,
            _ => ServiceStartMode.Manual
        };
    }

    private ChangeRecord CreateErrorRecord(PolicyDefinition policy, ChangeOperation operation, string error)
    {
        return new ChangeRecord
        {
            ChangeId = Guid.NewGuid().ToString(),
            Operation = operation,
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

internal sealed record ExternalProcessResult(bool Success, string? Output, string? ErrorMessage);
