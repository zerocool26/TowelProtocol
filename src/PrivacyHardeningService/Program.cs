using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PrivacyHardeningService.Executors;
using PrivacyHardeningService.IPC;
using PrivacyHardeningService.PolicyEngine;
using PrivacyHardeningService.Security;
using PrivacyHardeningService.StateManager;

namespace PrivacyHardeningService;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Configure as Windows Service
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "PrivacyHardeningService";
        });

        // Register IPC server
        builder.Services.AddSingleton<IPCServer>();
        builder.Services.AddSingleton<CommandValidator>();
        builder.Services.AddSingleton<CallerValidator>();

        // Register policy engine components
        builder.Services.AddSingleton<PolicyLoader>();
        builder.Services.AddSingleton<PolicyValidator>();
        builder.Services.AddSingleton<CompatibilityChecker>();
        builder.Services.AddSingleton<DependencyResolver>();
        builder.Services.AddSingleton<PolicyEngineCore>();

        // Register executors
        builder.Services.AddSingleton<IExecutor, RegistryExecutor>();
        builder.Services.AddSingleton<IExecutor, ServiceExecutor>();
        builder.Services.AddSingleton<IExecutor, TaskExecutor>();
        builder.Services.AddSingleton<IExecutor, FirewallExecutor>();
        builder.Services.AddSingleton<IExecutor, PowerShellExecutor>();
        builder.Services.AddSingleton<ExecutorFactory>();

        // Register state management
        builder.Services.AddSingleton<SystemStateCapture>();
        builder.Services.AddSingleton<ChangeLog>();
        builder.Services.AddSingleton<RestorePointManager>();
        builder.Services.AddSingleton<DriftDetector>();

        // Register background service
        builder.Services.AddHostedService<ServiceMain>();

        var host = builder.Build();
        host.Run();
    }
}
