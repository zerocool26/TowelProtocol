using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace PrivacyHardeningUI.Services;

/// <summary>
/// Represents a network connection.
/// </summary>
public record NetworkConnection(
    string Protocol,
    string LocalAddress,
    string LocalPort,
    string RemoteAddress,
    string RemotePort,
    string State,
    string ProcessName,
    int ProcessId,
    DateTime DetectedAt
);

/// <summary>
/// Represents analysis of a connection to determine if it's telemetry-related.
/// </summary>
public record TelemetryConnection(
    NetworkConnection Connection,
    bool IsTelemetry,
    string Category,
    string Description,
    TelemetryRiskLevel RiskLevel
);

/// <summary>
/// Risk level for telemetry connections.
/// </summary>
public enum TelemetryRiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Service for monitoring network traffic and identifying telemetry connections.
/// </summary>
public interface INetworkTrafficMonitorService
{
    /// <summary>
    /// Get all active network connections.
    /// </summary>
    Task<IReadOnlyList<NetworkConnection>> GetActiveConnectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyze connections to identify telemetry traffic.
    /// </summary>
    Task<IReadOnlyList<TelemetryConnection>> AnalyzeTelemetryConnectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get list of known Microsoft telemetry domains.
    /// </summary>
    IReadOnlyList<string> GetKnownTelemetryDomains();

    /// <summary>
    /// Event raised when a new telemetry connection is detected.
    /// </summary>
    event EventHandler<TelemetryConnection>? TelemetryConnectionDetected;
}

/// <summary>
/// Implementation of network traffic monitoring service.
/// </summary>
public class NetworkTrafficMonitorService : INetworkTrafficMonitorService
{
    public event EventHandler<TelemetryConnection>? TelemetryConnectionDetected;

    // Known Microsoft telemetry domains
    private static readonly HashSet<string> TelemetryDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        // Microsoft Telemetry
        "vortex.data.microsoft.com",
        "vortex-win.data.microsoft.com",
        "telecommand.telemetry.microsoft.com",
        "oca.telemetry.microsoft.com",
        "sqm.telemetry.microsoft.com",
        "watson.telemetry.microsoft.com",
        "redir.metaservices.microsoft.com",
        "choice.microsoft.com",
        "df.telemetry.microsoft.com",
        "reports.wes.df.telemetry.microsoft.com",
        "services.wes.df.telemetry.microsoft.com",
        "sqm.df.telemetry.microsoft.com",
        "telemetry.microsoft.com",
        "watson.ppe.telemetry.microsoft.com",
        "telemetry.appex.bing.net",
        "telemetry.urs.microsoft.com",
        "telemetry.appex.bing.net:443",
        "settings-sandbox.data.microsoft.com",
        "vortex-sandbox.data.microsoft.com",

        // Windows Update Telemetry
        "statsfe2.ws.microsoft.com",
        "corpext.msitadfs.glbdns2.microsoft.com",
        "compatexchange.cloudapp.net",
        "cs1.wpc.v0cdn.net",
        "a-0001.a-msedge.net",
        "statsfe2.update.microsoft.com.akadns.net",
        "sls.update.microsoft.com.akadns.net",

        // Diagnostics
        "diagnostics.support.microsoft.com",
        "corp.sts.microsoft.com",
        "statsfe1.ws.microsoft.com",
        "pre.footprintpredict.com",
        "i1.services.social.microsoft.com",
        "i1.services.social.microsoft.com.nsatc.net",

        // Advertising
        "ads1.msn.com",
        "ads.msn.com",
        "ads1.msads.net",
        "ads.msads.net",
        "adnexus.net",
        "adnxs.com",
        "ris.api.iris.microsoft.com",

        // Cortana & Search
        "www.bing.com",
        "bing.com",
        "api.bing.com",
    };

    // Known telemetry processes
    private static readonly HashSet<string> TelemetryProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "compattelrunner.exe",
        "devicecensus.exe",
        "diagtrack.exe",
        "dmclient.exe",
        "invagent.exe",
        "musnotification.exe",
        "musnotificationux.exe",
        "omadmclient.exe",
        "utc.exe",
        "compattelrunner",
        "devicecensus",
        "diagtrack",
    };

    public async Task<IReadOnlyList<NetworkConnection>> GetActiveConnectionsAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var connections = new List<NetworkConnection>();

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netstat",
                    Arguments = "-ano",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return connections;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var now = DateTime.Now;

                foreach (var line in lines.Skip(4)) // Skip header lines
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 5) continue;

                    var protocol = parts[0];
                    var localAddress = parts[1];
                    var remoteAddress = parts[2];
                    var state = parts.Length > 3 && parts[0].Equals("TCP", StringComparison.OrdinalIgnoreCase)
                        ? parts[3]
                        : "N/A";
                    var pidStr = parts[^1]; // Last element is PID

                    if (!int.TryParse(pidStr, out var pid)) continue;

                    // Parse local address
                    var localParts = localAddress.Split(':');
                    var localAddr = localParts.Length > 1 ? string.Join(":", localParts[..^1]) : localAddress;
                    var localPort = localParts.Length > 1 ? localParts[^1] : "0";

                    // Parse remote address
                    var remoteParts = remoteAddress.Split(':');
                    var remoteAddr = remoteParts.Length > 1 ? string.Join(":", remoteParts[..^1]) : remoteAddress;
                    var remotePort = remoteParts.Length > 1 ? remoteParts[^1] : "0";

                    // Get process name
                    var processName = GetProcessName(pid);

                    connections.Add(new NetworkConnection(
                        protocol,
                        localAddr,
                        localPort,
                        remoteAddr,
                        remotePort,
                        state,
                        processName,
                        pid,
                        now
                    ));
                }
            }
            catch
            {
                // Return empty list on error
            }

            return connections;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<TelemetryConnection>> AnalyzeTelemetryConnectionsAsync(CancellationToken cancellationToken = default)
    {
        var connections = await GetActiveConnectionsAsync(cancellationToken);
        var telemetryConnections = new List<TelemetryConnection>();

        foreach (var connection in connections)
        {
            var analysis = AnalyzeConnection(connection);
            if (analysis.IsTelemetry)
            {
                telemetryConnections.Add(analysis);
            }
        }

        return telemetryConnections;
    }

    public IReadOnlyList<string> GetKnownTelemetryDomains()
    {
        return TelemetryDomains.ToList();
    }

    /// <summary>
    /// Analyze a connection to determine if it's telemetry-related.
    /// </summary>
    private TelemetryConnection AnalyzeConnection(NetworkConnection connection)
    {
        var isTelemetry = false;
        var category = "Unknown";
        var description = "Normal network connection";
        var riskLevel = TelemetryRiskLevel.Low;

        // Check if process is known telemetry process
        if (TelemetryProcesses.Contains(connection.ProcessName))
        {
            isTelemetry = true;
            category = "Telemetry Process";
            description = $"Known telemetry process: {connection.ProcessName}";
            riskLevel = TelemetryRiskLevel.High;
        }

        // Check if remote address is known telemetry domain
        if (IsTelemetryDomain(connection.RemoteAddress))
        {
            isTelemetry = true;
            category = "Telemetry Domain";
            description = $"Connection to known telemetry domain: {connection.RemoteAddress}";
            riskLevel = TelemetryRiskLevel.High;
        }

        // Check for Microsoft IP ranges (heuristic)
        if (connection.RemoteAddress.StartsWith("13.") ||
            connection.RemoteAddress.StartsWith("20.") ||
            connection.RemoteAddress.StartsWith("40.") ||
            connection.RemoteAddress.StartsWith("104."))
        {
            // These are common Microsoft Azure IP ranges
            if (connection.ProcessName.Contains("svchost", StringComparison.OrdinalIgnoreCase) ||
                connection.ProcessName.Contains("MoUsoCoreWorker", StringComparison.OrdinalIgnoreCase))
            {
                isTelemetry = true;
                category = "Suspected Telemetry";
                description = $"Suspected telemetry to Microsoft IP: {connection.RemoteAddress}";
                riskLevel = TelemetryRiskLevel.Medium;
            }
        }

        // Check specific processes
        if (connection.ProcessName.Equals("svchost.exe", StringComparison.OrdinalIgnoreCase) &&
            (connection.RemotePort == "443" || connection.RemotePort == "80"))
        {
            category = "System Service";
            description = "System service connection (may include telemetry)";
            riskLevel = TelemetryRiskLevel.Low;
        }

        return new TelemetryConnection(
            connection,
            isTelemetry,
            category,
            description,
            riskLevel
        );
    }

    /// <summary>
    /// Check if an address is a known telemetry domain.
    /// </summary>
    private bool IsTelemetryDomain(string address)
    {
        // Direct match
        if (TelemetryDomains.Contains(address))
            return true;

        // Try reverse DNS lookup for IP addresses
        if (IPAddress.TryParse(address, out var ipAddress))
        {
            try
            {
                var hostEntry = Dns.GetHostEntry(ipAddress);
                return TelemetryDomains.Any(domain =>
                    hostEntry.HostName.Contains(domain, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                // DNS lookup failed
            }
        }

        return false;
    }

    /// <summary>
    /// Get process name from PID.
    /// </summary>
    private static string GetProcessName(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch
        {
            return "Unknown";
        }
    }
}
