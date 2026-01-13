using System.Runtime.Versioning;
using System.Text.Json;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningService.Executors;

/// <summary>
/// Executes registry-based policies
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryExecutor : IExecutor
{
    private readonly ILogger<RegistryExecutor> _logger;

    public MechanismType MechanismType => MechanismType.Registry;

    public RegistryExecutor(ILogger<RegistryExecutor> logger)
    {
        _logger = logger;
    }

    public async Task<bool> IsAppliedAsync(PolicyDefinition policy, CancellationToken cancellationToken)
    {
        var details = ParseRegistryDetails(policy.MechanismDetails);
        if (details == null) return false;

        var currentValue = await GetCurrentValueAsync(policy, cancellationToken);
        return currentValue == details.ExpectedValue;
    }

    public async Task<string?> GetCurrentValueAsync(PolicyDefinition policy, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Registry operations are synchronous

        var details = ParseRegistryDetails(policy.MechanismDetails);
        if (details == null) return null;

        try
        {
            var (hive, subKey) = ParseKeyPath(details.KeyPath);
            using var key = hive.OpenSubKey(subKey, writable: false);

            if (key == null) return null;

            var value = key.GetValue(details.ValueName);
            if (value == null) return null;

            // Convert to string for comparison
            return details.ValueType switch
            {
                RegistryValueKind.DWord => $"0x{value:X8}",
                RegistryValueKind.QWord => $"0x{value:X16}",
                RegistryValueKind.String or RegistryValueKind.ExpandString => value.ToString(),
                _ => value.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read registry value: {KeyPath}\\{ValueName}",
                details.KeyPath, details.ValueName);
            return null;
        }
    }

    public async Task<ChangeRecord> ApplyAsync(PolicyDefinition policy, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Registry operations are synchronous

        var details = ParseRegistryDetails(policy.MechanismDetails);
        if (details == null)
        {
            return CreateErrorRecord(policy, ChangeOperation.Apply, "Invalid registry mechanism details");
        }

        try
        {
            var (hive, subKey) = ParseKeyPath(details.KeyPath);

            // Capture previous state
            var previousValue = await GetCurrentValueAsync(policy, cancellationToken);

            // Create key if it doesn't exist
            using var key = hive.CreateSubKey(subKey, writable: true);
            if (key == null)
            {
                return CreateErrorRecord(policy, ChangeOperation.Apply, $"Failed to create/open registry key: {details.KeyPath}");
            }

            // Set value based on type
            object valueToSet = details.ValueType switch
            {
                RegistryValueKind.DWord => Convert.ToInt32(details.ValueData),
                RegistryValueKind.QWord => Convert.ToInt64(details.ValueData),
                RegistryValueKind.String => details.ValueData.ToString() ?? string.Empty,
                RegistryValueKind.ExpandString => details.ValueData.ToString() ?? string.Empty,
                _ => details.ValueData
            };

            key.SetValue(details.ValueName, valueToSet, details.ValueType);

            _logger.LogInformation("Set registry value: {KeyPath}\\{ValueName} = {Value}",
                details.KeyPath, details.ValueName, details.ExpectedValue);

            return new ChangeRecord
            {
                ChangeId = Guid.NewGuid().ToString(),
                Operation = ChangeOperation.Apply,
                PolicyId = policy.PolicyId,
                AppliedAt = DateTime.UtcNow,
                Mechanism = MechanismType.Registry,
                Description = $"Set {details.KeyPath}\\{details.ValueName}",
                PreviousState = previousValue,
                NewState = details.ExpectedValue,
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply registry policy: {PolicyId}", policy.PolicyId);
            return CreateErrorRecord(policy, ChangeOperation.Apply, ex.Message);
        }
    }

    public async Task<ChangeRecord> RevertAsync(PolicyDefinition policy, ChangeRecord originalChange, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var details = ParseRegistryDetails(policy.MechanismDetails);
        if (details == null)
        {
            return CreateErrorRecord(policy, ChangeOperation.Revert, "Invalid registry mechanism details");
        }

        try
        {
            var (hive, subKey) = ParseKeyPath(details.KeyPath);

            if (originalChange.PreviousState == null)
            {
                // Value didn't exist before - delete it
                using var key = hive.OpenSubKey(subKey, writable: true);
                if (key != null)
                {
                    key.DeleteValue(details.ValueName, throwOnMissingValue: false);
                    _logger.LogInformation("Deleted registry value: {KeyPath}\\{ValueName}",
                        details.KeyPath, details.ValueName);
                }
            }
            else
            {
                // Restore previous value
                using var key = hive.CreateSubKey(subKey, writable: true);
                if (key != null)
                {
                    // Parse previous value back to appropriate type
                    object? previousValue = null;

                    var prevStateStr = originalChange.PreviousState as string;
                    if (!string.IsNullOrEmpty(prevStateStr))
                    {
                        // Support hex string prefixed with "0x" or plain hex digits
                        var hex = prevStateStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                            ? prevStateStr.Substring(2)
                            : prevStateStr;

                        if (details.ValueType == RegistryValueKind.DWord)
                        {
                            if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var iv))
                                previousValue = iv;
                            else if (int.TryParse(prevStateStr, out iv))
                                previousValue = iv;
                        }
                        else if (details.ValueType == RegistryValueKind.QWord)
                        {
                            if (long.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var lv))
                                previousValue = lv;
                            else if (long.TryParse(prevStateStr, out lv))
                                previousValue = lv;
                        }
                        else
                        {
                            previousValue = prevStateStr;
                        }
                    }

                    // If we couldn't parse previous value, fall back to creating the key and setting string
                    if (previousValue == null)
                        previousValue = originalChange.PreviousState;

                    key.SetValue(details.ValueName, previousValue, details.ValueType);
                    _logger.LogInformation("Restored registry value: {KeyPath}\\{ValueName} = {Value}",
                        details.KeyPath, details.ValueName, originalChange.PreviousState);
                }
            }

            return new ChangeRecord
            {
                ChangeId = Guid.NewGuid().ToString(),
                Operation = ChangeOperation.Revert,
                PolicyId = policy.PolicyId,
                AppliedAt = DateTime.UtcNow,
                Mechanism = MechanismType.Registry,
                Description = $"Reverted {details.KeyPath}\\{details.ValueName}",
                PreviousState = originalChange.NewState,
                NewState = originalChange.PreviousState ?? "[deleted]",
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revert registry policy: {PolicyId}", policy.PolicyId);
            return CreateErrorRecord(policy, ChangeOperation.Revert, ex.Message);
        }
    }

    private RegistryDetails? ParseRegistryDetails(object mechanismDetails)
    {
        try
        {
            var json = JsonSerializer.Serialize(mechanismDetails);
            return JsonSerializer.Deserialize<RegistryDetails>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    private (RegistryKey Hive, string SubKey) ParseKeyPath(string keyPath)
    {
        var parts = keyPath.Split('\\', 2);
        var hiveName = parts[0];
        var subKey = parts.Length > 1 ? parts[1] : string.Empty;

        var hive = hiveName.ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
            "HKU" or "HKEY_USERS" => Registry.Users,
            "HKCC" or "HKEY_CURRENT_CONFIG" => Registry.CurrentConfig,
            _ => throw new ArgumentException($"Unknown registry hive: {hiveName}")
        };

        return (hive, subKey);
    }

    private ChangeRecord CreateErrorRecord(PolicyDefinition policy, ChangeOperation operation, string error)
    {
        return new ChangeRecord
        {
            ChangeId = Guid.NewGuid().ToString(),
            Operation = operation,
            PolicyId = policy.PolicyId,
            AppliedAt = DateTime.UtcNow,
            Mechanism = MechanismType.Registry,
            Description = "Failed to apply policy",
            PreviousState = null,
            NewState = "[error]",
            Success = false,
            ErrorMessage = error
        };
    }
}

internal sealed class RegistryDetails
{
    public required string KeyPath { get; init; }
    public required string ValueName { get; init; }
    public required RegistryValueKind ValueType { get; init; }
    public required object ValueData { get; init; }
    public required string ExpectedValue { get; init; }
}
