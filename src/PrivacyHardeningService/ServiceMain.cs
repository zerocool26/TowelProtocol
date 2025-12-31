using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PrivacyHardeningService.IPC;

namespace PrivacyHardeningService;

/// <summary>
/// Main Windows Service entry point
/// </summary>
public sealed class ServiceMain : BackgroundService
{
    private readonly ILogger<ServiceMain> _logger;
    private readonly IPCServer _ipcServer;

    public ServiceMain(ILogger<ServiceMain> logger, IPCServer ipcServer)
    {
        _logger = logger;
        _ipcServer = ipcServer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Privacy Hardening Service starting");

        try
        {
            // Start IPC server
            await _ipcServer.StartAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in service execution");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Privacy Hardening Service stopping");
        await base.StopAsync(cancellationToken);
    }
}
