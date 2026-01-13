using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    public ReportsViewModel(AuditViewModel audit, HistoryViewModel history, PreviewViewModel preview, SettingsService settings)
    {
        _audit = audit;
        _history = history;
        _preview = preview;
        _settings = settings;
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
}
