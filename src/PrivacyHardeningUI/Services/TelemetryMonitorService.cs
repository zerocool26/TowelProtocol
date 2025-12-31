using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PrivacyHardeningUI.Services;

/// <summary>
/// Represents the status of a telemetry component.
/// </summary>
public enum TelemetryStatus
{
    Active,
    Inactive,
    Disabled,
    Unknown
}

/// <summary>
/// Represents a monitored telemetry component.
/// </summary>
public record TelemetryComponent(
    string Id,
    string Name,
    string Category,
    TelemetryStatus Status,
    string Description,
    DateTime LastChecked
);

/// <summary>
/// Service for monitoring Windows telemetry and data collection in real-time.
/// </summary>
public interface ITelemetryMonitorService
{
    /// <summary>
    /// Get all monitored telemetry components with their current status.
    /// </summary>
    Task<IReadOnlyList<TelemetryComponent>> GetTelemetryStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a specific service is running.
    /// </summary>
    Task<bool> IsServiceRunningAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the diagnostic data level from registry.
    /// </summary>
    Task<int> GetDiagnosticDataLevelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when telemetry status changes.
    /// </summary>
    event EventHandler<TelemetryComponent>? TelemetryStatusChanged;
}

/// <summary>
/// Implementation of telemetry monitoring service.
/// </summary>
public class TelemetryMonitorService : ITelemetryMonitorService
{
    public event EventHandler<TelemetryComponent>? TelemetryStatusChanged;

    // Known Windows telemetry services
    private static readonly Dictionary<string, (string Name, string Description)> TelemetryServices = new()
    {
        ["DiagTrack"] = ("Connected User Experiences and Telemetry", "Primary telemetry collection service"),
        ["dmwappushservice"] = ("Device Management Wireless Application Protocol", "WAP Push message routing"),
        ["diagnosticshub.standardcollector.service"] = ("Diagnostics Hub Standard Collector", "Collects diagnostic information"),
        ["DPS"] = ("Diagnostic Policy Service", "Enables problem detection and troubleshooting"),
        ["WdiServiceHost"] = ("Diagnostic Service Host", "Hosts diagnostic services"),
        ["WdiSystemHost"] = ("Diagnostic System Host", "System diagnostic services"),
        ["PcaSvc"] = ("Program Compatibility Assistant", "Monitors program compatibility"),
        ["DcpSvc"] = ("Data Collection and Publishing", "Collects and publishes data"),
    };

    // Known telemetry scheduled tasks
    private static readonly Dictionary<string, (string Name, string Description)> TelemetryTasks = new()
    {
        [@"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser"] =
            ("Compatibility Appraiser", "Collects program compatibility data"),
        [@"\Microsoft\Windows\Application Experience\ProgramDataUpdater"] =
            ("Program Data Updater", "Collects program usage data"),
        [@"\Microsoft\Windows\Autochk\Proxy"] =
            ("Autochk Proxy", "Collects disk error data"),
        [@"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator"] =
            ("CEIP Consolidator", "Consolidates customer experience data"),
        [@"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip"] =
            ("CEIP USB", "Collects USB usage data"),
        [@"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector"] =
            ("Disk Diagnostic Collector", "Collects disk diagnostic data"),
    };

    public async Task<IReadOnlyList<TelemetryComponent>> GetTelemetryStatusAsync(CancellationToken cancellationToken = default)
    {
        var components = new List<TelemetryComponent>();
        var now = DateTime.Now;

        // Check diagnostic data level
        var dataLevel = await GetDiagnosticDataLevelAsync(cancellationToken);
        components.Add(new TelemetryComponent(
            "DiagnosticDataLevel",
            "Diagnostic Data Level",
            "System",
            dataLevel switch
            {
                0 => TelemetryStatus.Disabled,
                1 => TelemetryStatus.Inactive,
                2 or 3 => TelemetryStatus.Active,
                _ => TelemetryStatus.Unknown
            },
            $"Current level: {GetDataLevelName(dataLevel)}",
            now
        ));

        // Check telemetry services
        foreach (var (serviceId, (name, description)) in TelemetryServices)
        {
            var isRunning = await IsServiceRunningAsync(serviceId, cancellationToken);
            components.Add(new TelemetryComponent(
                serviceId,
                name,
                "Service",
                isRunning ? TelemetryStatus.Active : TelemetryStatus.Inactive,
                description,
                now
            ));
        }

        // Check scheduled tasks
        foreach (var (taskPath, (name, description)) in TelemetryTasks)
        {
            var isEnabled = await IsScheduledTaskEnabledAsync(taskPath, cancellationToken);
            components.Add(new TelemetryComponent(
                taskPath,
                name,
                "Task",
                isEnabled switch
                {
                    true => TelemetryStatus.Active,
                    false => TelemetryStatus.Disabled,
                    null => TelemetryStatus.Unknown
                },
                description,
                now
            ));
        }

        // Check advertising ID
        var advertisingIdEnabled = await IsAdvertisingIdEnabledAsync(cancellationToken);
        components.Add(new TelemetryComponent(
            "AdvertisingId",
            "Advertising ID",
            "Privacy",
            advertisingIdEnabled ? TelemetryStatus.Active : TelemetryStatus.Disabled,
            "Unique identifier for ad tracking",
            now
        ));

        return components;
    }

    public async Task<bool> IsServiceRunningAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "sc",
                    Arguments = $"query \"{serviceName}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                return output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }, cancellationToken);
    }

    public async Task<int> GetDiagnosticDataLevelAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!OperatingSystem.IsWindows()) return -1;

                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Policies\Microsoft\Windows\DataCollection");

                var value = key?.GetValue("AllowTelemetry");
                return value is int level ? level : 3; // Default is Full
            }
            catch
            {
                return -1;
            }
        }, cancellationToken);
    }

    private async Task<bool?> IsScheduledTaskEnabledAsync(string taskPath, CancellationToken cancellationToken)
    {
        return await Task.Run<bool?>(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = $"/Query /TN \"{taskPath}\" /FO LIST",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return null;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0) return null;

                // Check if task is enabled
                return !output.Contains("Disabled", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return null;
            }
        }, cancellationToken);
    }

    private async Task<bool> IsAdvertisingIdEnabledAsync(CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!OperatingSystem.IsWindows()) return false;

                using var key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo");

                var value = key?.GetValue("Enabled");
                return value is int enabled && enabled != 0;
            }
            catch
            {
                return true; // Assume enabled if can't check
            }
        }, cancellationToken);
    }

    private static string GetDataLevelName(int level)
    {
        return level switch
        {
            0 => "Security (Enterprise only)",
            1 => "Basic",
            2 => "Enhanced",
            3 => "Full",
            _ => "Unknown"
        };
    }
}
