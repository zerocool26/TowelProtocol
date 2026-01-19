using System.Text.Json;
using Microsoft.Extensions.Logging;
using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningService.Configuration;

public sealed class ServiceConfigManager
{
    private readonly ILogger<ServiceConfigManager> _logger;
    private readonly string _configPath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    
    private ServiceConfiguration _currentConfig = new();

    public event EventHandler<ServiceConfiguration>? ConfigChanged;

    public ServiceConfiguration CurrentConfig => _currentConfig;

    public ServiceConfigManager(ILogger<ServiceConfigManager> logger)
    {
        _logger = logger;
        var commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var dir = Path.Combine(commonAppData, "PrivacyHardeningFramework");
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, "service_config.json");
        
        // Load sync to ensure available
        LoadAsync().Wait();
    }

    public async Task LoadAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (File.Exists(_configPath))
            {
                using var stream = File.OpenRead(_configPath);
                var loaded = await JsonSerializer.DeserializeAsync<ServiceConfiguration>(stream);
                if (loaded != null)
                {
                    _currentConfig = loaded;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load service configuration");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(ServiceConfiguration newConfig)
    {
        await _lock.WaitAsync();
        try
        {
            using var stream = File.Create(_configPath);
            await JsonSerializer.SerializeAsync(stream, newConfig, new JsonSerializerOptions { WriteIndented = true });
            _currentConfig = newConfig;
            
            ConfigChanged?.Invoke(this, newConfig);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save service configuration");
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }
}
