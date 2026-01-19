using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PrivacyHardeningService.Configuration;
using PrivacyHardeningService.PolicyEngine;
using PrivacyHardeningService.StateManager;

namespace PrivacyHardeningService.Monitoring;

/// <summary>
/// Background service that periodically checks for policy drift based on configuration.
/// </summary>
public sealed class DriftMonitorService : BackgroundService
{
    private readonly ILogger<DriftMonitorService> _logger;
    private readonly ServiceConfigManager _configManager;
    private readonly DriftDetector _driftDetector;
    private readonly PolicyEngineCore _policyEngine; // For auto-remediation if we implement it

    private PeriodicTimer? _timer;

    public DriftMonitorService(
        ILogger<DriftMonitorService> logger,
        ServiceConfigManager configManager,
        DriftDetector driftDetector,
        PolicyEngineCore policyEngine)
    {
        _logger = logger;
        _configManager = configManager;
        _driftDetector = driftDetector;
        _policyEngine = policyEngine;
        
        _configManager.ConfigChanged += OnConfigChanged;
    }

    private void OnConfigChanged(object? sender, PrivacyHardeningContracts.Models.ServiceConfiguration e)
    {
        UpdateTimer();
    }

    private void UpdateTimer()
    {
        var intervalMin = _configManager.CurrentConfig.DriftCheckIntervalMinutes;
        
        if (intervalMin <= 0)
        {
            _timer?.Dispose();
            _timer = null;
            _logger.LogInformation("Background drift monitoring disabled.");
        }
        else
        {
            // Reset timer
            _timer?.Dispose();
            _timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMin));
            _logger.LogInformation("Background drift monitoring scheduled every {Minutes} minutes.", intervalMin);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        UpdateTimer();

        while (stoppingToken.IsCancellationRequested == false)
        {
             if (_timer != null)
             {
                 try
                 {
                     if (await _timer.WaitForNextTickAsync(stoppingToken))
                     {
                         await RunDriftCheckAsync(stoppingToken);
                     }
                 }
                 catch (OperationCanceledException) { break; }
             }
             else
             {
                 // Wait a bit if disabled, or rely on ConfigChanged to wake (Logic gap: strictly ConfigChanged won't wake a while loop waiting on a null timer easily without a semaphore, 
                 // but typically ExecuteAsync just waits. If timer is null, we can do Task.Delay)
                 await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
             }
        }
    }

    private async Task RunDriftCheckAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting scheduled drift check...");
        try
        {
            var command = new PrivacyHardeningContracts.Commands.DetectDriftCommand { SnapshotId = null };
            var result = await _policyEngine.DetectDriftAsync(command, ct);
            
            if (result.Success && result.DriftedPolicies.Length > 0)
            {
                _logger.LogWarning("Drift detected: {Count} policies have drifted.", result.DriftedPolicies.Length);

                if (_configManager.CurrentConfig.AutoRemediateDrift)
                {
                    _logger.LogInformation("Auto-remediation is enabled. Attempting to fix drift...");
                    // Extract IDs
                    var ids = result.DriftedPolicies.Select(p => p.PolicyId).ToArray();
                    
                    // Apply
                    // Note: CreateRestorePoint=false to avoid spamming restore points on background checks
                    await _policyEngine.ApplyAsync(new PrivacyHardeningContracts.Commands.ApplyCommand 
                    {
                        PolicyIds = ids,
                        CreateRestorePoint = false, 
                        DryRun = false,
                        ContinueOnError = true
                    }, ct);
                }
            }
            else
            {
                _logger.LogInformation("Drift check complete. System is compliant.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during scheduled drift check");
        }
    }
}
