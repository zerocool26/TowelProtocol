using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PrivacyHardeningUI.Services.Probes;

/// <summary>
/// Simple registry probe that checks whether a registry value exists and optionally
/// compares it to an expected string value.
/// </summary>
public class RegistryProbe : IPolicyProbe
{
    public string PolicyId { get; }
    private readonly string _hiveName;
    private readonly string _subKeyPath;
    private readonly string? _valueName;
    private readonly string? _expectedValue;

    public RegistryProbe(string policyId, string hiveName, string subKeyPath, string? valueName = null, string? expectedValue = null)
    {
        PolicyId = policyId;
        _hiveName = hiveName;
        _subKeyPath = subKeyPath;
        _valueName = valueName;
        _expectedValue = expectedValue;
    }

    public Task<ProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var result = new ProbeResult
        {
            PolicyId = PolicyId,
            Type = ProbeType.Registry,
            Timestamp = DateTime.UtcNow,
            Confidence = 1.0
        };

        try
        {
            RegistryKey? baseKey = null;
            if (string.Equals(_hiveName, "HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase) || string.Equals(_hiveName, "HKCU", StringComparison.OrdinalIgnoreCase))
            {
                baseKey = Registry.CurrentUser;
            }
            else if (string.Equals(_hiveName, "HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase) || string.Equals(_hiveName, "HKLM", StringComparison.OrdinalIgnoreCase))
            {
                baseKey = Registry.LocalMachine;
                result.RequiresElevation = false; // reading HKLM usually doesn't require elevation; actions might
            }
            else
            {
                // unknown hive - attempt CurrentUser as safe fallback
                baseKey = Registry.CurrentUser;
            }

            using var key = baseKey.OpenSubKey(_subKeyPath, writable: false);
            if (key == null)
            {
                result.IsEnabled = null;
                result.Evidence = $"Registry key not found: {_hiveName}\\{_subKeyPath}";
                result.Confidence = 0.6;
                return Task.FromResult(result);
            }

            if (_valueName == null)
            {
                // Key presence is considered evidence
                result.IsEnabled = true;
                result.Evidence = $"Registry key exists: {_hiveName}\\{_subKeyPath}";
                return Task.FromResult(result);
            }

            var value = key.GetValue(_valueName);
            if (value == null)
            {
                result.IsEnabled = false;
                result.Evidence = $"Value '{_valueName}' not present under {_hiveName}\\{_subKeyPath}";
                return Task.FromResult(result);
            }

            var s = value.ToString() ?? string.Empty;
            if (_expectedValue != null)
            {
                result.IsEnabled = string.Equals(s, _expectedValue, StringComparison.OrdinalIgnoreCase);
                result.Evidence = $"Value='{s}' (expected='{_expectedValue}')";
            }
            else
            {
                // Presence of a value implies enabled
                result.IsEnabled = true;
                result.Evidence = $"Value present: {_valueName}='{s}'";
            }

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            result.IsEnabled = null;
            result.Evidence = "Probe failed: " + ex.Message;
            result.Confidence = 0.0;
            return Task.FromResult(result);
        }
    }
}
