using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PrivacyHardeningService.PolicyEngine;

/// <summary>
/// Manages persistent configuration overrides for policies.
/// Allows user choices (e.g. "Delete" vs "Disable") to stick across restarts and audits.
/// </summary>
public sealed class PolicyOverrideManager
{
    private readonly ILogger<PolicyOverrideManager> _logger;
    private readonly string _overridesFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public PolicyOverrideManager(ILogger<PolicyOverrideManager> logger)
    {
        _logger = logger;
        
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var configDirectory = Path.Combine(appDataPath, "PrivacyHardeningFramework");
        Directory.CreateDirectory(configDirectory);
        
        _overridesFilePath = Path.Combine(configDirectory, "policy_overrides.json");
    }

    /// <summary>
    /// Loads all persisted overrides.
    /// Key: PolicyId, Value: JSON configuration
    /// </summary>
    public async Task<Dictionary<string, string>> LoadOverridesAsync(CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_overridesFilePath))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            using var stream = File.OpenRead(_overridesFilePath);
            var result = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, cancellationToken: cancellationToken);
            return result ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load policy overrides");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// Saves or updates overrides for specific policies.
    /// Merges with existing overrides.
    /// </summary>
    public async Task UpdateOverridesAsync(Dictionary<string, string> newOverrides, CancellationToken cancellationToken)
    {
        if (newOverrides == null || newOverrides.Count == 0) return;

        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            Dictionary<string, string> currentOverrides;

            if (File.Exists(_overridesFilePath))
            {
                try 
                {
                    using var readStream = File.OpenRead(_overridesFilePath);
                    currentOverrides = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(readStream, cancellationToken: cancellationToken) 
                        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                catch
                {
                    currentOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
            }
            else
            {
                currentOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            bool changed = false;
            foreach (var kvp in newOverrides)
            {
                if (!currentOverrides.TryGetValue(kvp.Key, out var existing) || existing != kvp.Value)
                {
                    currentOverrides[kvp.Key] = kvp.Value;
                    changed = true;
                }
            }

            if (changed)
            {
                using var writeStream = File.Create(_overridesFilePath);
                await JsonSerializer.SerializeAsync(writeStream, currentOverrides, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
                _logger.LogInformation("Updated policy overrides for {Count} policies", newOverrides.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save policy overrides");
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }
    
    /// <summary>
    /// Removes overrides for specific policies (restoring them to default behavior).
    /// </summary>
    public async Task RemoveOverridesAsync(IEnumerable<string> policyIds, CancellationToken cancellationToken)
    {
         await _fileLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_overridesFilePath)) return;

            Dictionary<string, string> currentOverrides;
             try 
            {
                using var readStream = File.OpenRead(_overridesFilePath);
                currentOverrides = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(readStream, cancellationToken: cancellationToken) 
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return;
            }

            bool changed = false;
            foreach (var id in policyIds)
            {
                if (currentOverrides.Remove(id))
                {
                    changed = true;
                }
            }

            if (changed)
            {
                 using var writeStream = File.Create(_overridesFilePath);
                 await JsonSerializer.SerializeAsync(writeStream, currentOverrides, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
                 _logger.LogInformation("Removed overrides for policies");
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
