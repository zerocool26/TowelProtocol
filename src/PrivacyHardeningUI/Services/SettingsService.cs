using System;
using System.IO;
using System.Text.Json;

namespace PrivacyHardeningUI.Services;

public class SettingsService
{
    public class SettingsModel
    {
        public bool IsDarkMode { get; set; }
        public double FontSize { get; set; } = 14.0;
        public bool AutoAuditOnStart { get; set; } = true;
        public bool RedactReports { get; set; } = true;
        public bool EnableEvidenceLogging { get; set; } = false;
        public string CustomPolicyPath { get; set; } = string.Empty;
    }

    private readonly string _settingsPath;

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "PrivacyHardeningUI");
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, "settings.json");
    }

    public SettingsModel Load()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return new SettingsModel();
            var txt = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<SettingsModel>(txt) ?? new SettingsModel();
        }
        catch
        {
            return new SettingsModel();
        }
    }

    public void Save(SettingsModel model)
    {
        var txt = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsPath, txt);
    }

    public void Import(string path)
    {
        if (!File.Exists(path)) return;
        File.Copy(path, _settingsPath, true);
    }

    public void Export(string destinationPath)
    {
        if (!File.Exists(_settingsPath)) return;
        File.Copy(_settingsPath, destinationPath, true);
    }
}
