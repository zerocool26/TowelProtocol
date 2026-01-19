using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PrivacyHardeningContracts.Models;
using PrivacyHardeningContracts.Responses;
using PrivacyHardeningUI.Services;

namespace PrivacyHardeningUI.ViewModels;

public sealed partial class ReportsViewModel : ObservableObject
{
    private readonly AuditViewModel _audit;
    private readonly HistoryViewModel _history;
    private readonly PreviewViewModel _preview;
    private readonly SettingsService _settings;
    private readonly ITelemetryMonitorService _telemetryMonitor;
    private readonly ServiceClient _serviceClient;

    public ReportsViewModel(
        AuditViewModel audit, 
        HistoryViewModel history, 
        PreviewViewModel preview, 
        SettingsService settings,
        ITelemetryMonitorService telemetryMonitor,
        ServiceClient serviceClient)
    {
        _audit = audit;
        _history = history;
        _preview = preview;
        _settings = settings;
        _telemetryMonitor = telemetryMonitor;
        _serviceClient = serviceClient;
    }

    public bool HasAudit => _audit.LastAuditResult != null;
    public bool HasPreview => _preview.LastPreviewResult != null;
    public bool HasHistory => _history.Snapshot != null;

    private string Redact(string text)
    {
        if (!_settings.Load().RedactReports) return text;

        // Redact user profile paths (C:\Users\Username\...)
        // Finds "Users\" followed by any non-backslash characters
        var redacted = Regex.Replace(text, @"Users\\[^\\]+", "Users\\REDACTED", RegexOptions.IgnoreCase);
        
        // Redact computer names in UNC paths or strings (\\Computer\...)
        redacted = Regex.Replace(redacted, @"\\{2}[^\\]+", "\\\\REDACTED", RegexOptions.IgnoreCase);

        return redacted;
    }

    public string ExportAuditJson()
    {
        var data = _audit.LastAuditResult;
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        return Redact(json);
    }

    public string ExportPreviewJson()
    {
        var data = _preview.LastPreviewResult;
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        return Redact(json);
    }

    public string ExportHistoryJson()
    {
        var data = _history.Snapshot;
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        return Redact(json);
    }

    public async Task<string> GenerateFullReportAsync()
    {
        var sb = new StringBuilder();
        var now = DateTime.Now;
        var state = await _serviceClient.GetStateAsync();

        sb.AppendLine("# Privacy Hardening Report");
        sb.AppendLine($"**Generated:** {now:F}");
        sb.AppendLine($"**Machine:** {Environment.MachineName} (Redacted if enabled)");
        sb.AppendLine($"**OS:** {state.SystemInfo?.WindowsVersion ?? "Unknown"}");
        sb.AppendLine();

        sb.AppendLine("## 1. Compliance Status");
        if (_audit.LastAuditResult != null)
        {
            var passed = _audit.LastAuditResult.Items.Count(i => i.Matches);
            var total = _audit.LastAuditResult.Items.Length;
             sb.AppendLine($"- **Compliance Score:** {passed}/{total} ({(total > 0 ? (passed * 100 / total) : 0)}%)");
        }
        else
        {
            sb.AppendLine("_No recent audit performed._");
        }
        sb.AppendLine();

        sb.AppendLine("## 2. Telemetry Monitor Snapshot");
        try 
        {
            var telemetry = await _telemetryMonitor.GetTelemetryStatusAsync();
            var services = telemetry.Where(t => t.Category == "Service");
            var tasks = telemetry.Where(t => t.Category == "Task");
            
            sb.AppendLine("### Telemetry Services");
            foreach(var s in services)
            {
               sb.AppendLine($"- {s.Name}: **{s.Status}**");
            }

            sb.AppendLine();
            sb.AppendLine("### Telemetry Tasks");
            foreach(var t in tasks)
            {
                sb.AppendLine($"- {t.Name}: **{t.Status}**");
            }
        }
        catch
        {
            sb.AppendLine("_Monitor data unavailable_");
        }
        sb.AppendLine();

        sb.AppendLine("## 3. Policy History");
        if (_history.Snapshot?.ChangeHistory != null)
        {
             var last5 = _history.Snapshot.ChangeHistory.OrderByDescending(c => c.AppliedAt).Take(10);
             foreach(var c in last5)
             {
                 sb.AppendLine($"- {c.AppliedAt}: {c.Operation} - {c.PolicyId} ({(c.Success ? "Success" : "Failed")})");
             }
        }
        
        return Redact(sb.ToString());
    }
}
