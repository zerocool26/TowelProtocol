using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningService.Executors;

/// <summary>
/// Executes Windows Service configuration policies
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ServiceExecutor : IExecutor
{
    private readonly ILogger<ServiceExecutor> _logger;

    public MechanismType MechanismType => MechanismType.Service;

    public ServiceExecutor(ILogger<ServiceExecutor> logger)
    {
        _logger = logger;
    }

    public async Task<bool> IsAppliedAsync(PolicyDefinition policy, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Service operations are synchronous

        var details = ParseServiceDetails(policy.MechanismDetails);
        if (details == null) return false;

        try
        {
            // Check startup type
            if (!string.IsNullOrEmpty(details.StartupType))
            {
                var currentStartupType = GetServiceStartupType(details.ServiceName);
                var expectedStartupType = ParseStartupType(details.StartupType);

                if (currentStartupType != expectedStartupType)
                {
                    return false;
                }
            }
            
            // If we also need to stop the service, check status
            if (details.StopService)
            {
                using var sc = new ServiceController(details.ServiceName);
                sc.Refresh();
                return sc.Status == ServiceControllerStatus.Stopped;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check service status: {ServiceName}", details.ServiceName);
            return false;
        }
    }

    public async Task<string?> GetCurrentValueAsync(PolicyDefinition policy, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var details = ParseServiceDetails(policy.MechanismDetails);
        if (details == null) return null;

        try
        {
            var startupType = GetServiceStartupType(details.ServiceName);

            using var sc = new ServiceController(details.ServiceName);
            sc.Refresh();
            var status = sc.Status;

            return $"StartupType={startupType}, Status={status}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read service state: {ServiceName}", details.ServiceName);
            return null;
        }
    }

    public async Task<ChangeRecord> ApplyAsync(PolicyDefinition policy, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var details = ParseServiceDetails(policy.MechanismDetails);
        if (details == null)
        {
            return CreateErrorRecord(policy, ChangeOperation.Apply, "Invalid service mechanism details");
        }

        try
        {
            // Capture previous state
            var previousStartupType = GetServiceStartupType(details.ServiceName);
            ServiceControllerStatus? previousStatus = null;

            using (var sc = new ServiceController(details.ServiceName))
            {
                sc.Refresh();
                previousStatus = sc.Status;
            }

            var previousState = $"StartupType={previousStartupType}, Status={previousStatus}";
            var targetStartupType = previousStartupType;

            // Change startup type
            if (!string.IsNullOrEmpty(details.StartupType))
            {
                targetStartupType = ParseStartupType(details.StartupType);
                SetServiceStartupType(details.ServiceName, targetStartupType);

                _logger.LogInformation("Changed service {ServiceName} startup type: {Previous} -> {New}",
                    details.ServiceName, previousStartupType, targetStartupType);
            }
            
            // Stop service if requested
            if (details.StopService)
            {
                using var sc = new ServiceController(details.ServiceName);
                sc.Refresh();

                if (sc.Status != ServiceControllerStatus.Stopped && sc.Status != ServiceControllerStatus.StopPending)
                {
                    // Check if service can be stopped
                    if (!sc.CanStop)
                    {
                        _logger.LogWarning("Service {ServiceName} cannot be stopped (CanStop=false)", details.ServiceName);
                    }
                    else
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                        _logger.LogInformation("Stopped service: {ServiceName}", details.ServiceName);
                    }
                }
            }

            var newState = $"StartupType={targetStartupType}, Status=Stopped";

            return new ChangeRecord
            {
                ChangeId = Guid.NewGuid().ToString(),
                Operation = ChangeOperation.Apply,
                PolicyId = policy.PolicyId,
                AppliedAt = DateTime.UtcNow,
                Mechanism = MechanismType.Service,
                Description = $"Configured service: {details.ServiceName}",
                PreviousState = previousState,
                NewState = newState,
                Success = true
            };
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("does not exist"))
        {
            _logger.LogWarning("Service not found: {ServiceName}", details.ServiceName);
            return CreateErrorRecord(policy, ChangeOperation.Apply, $"Service '{details.ServiceName}' does not exist on this system");
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            _logger.LogError("Timeout stopping service: {ServiceName}", details.ServiceName);
            return CreateErrorRecord(policy, ChangeOperation.Apply, $"Timeout stopping service '{details.ServiceName}'");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure service: {ServiceName}", details.ServiceName);
            return CreateErrorRecord(policy, ChangeOperation.Apply, ex.Message);
        }
    }

    public async Task<ChangeRecord> RevertAsync(PolicyDefinition policy, ChangeRecord originalChange, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var details = ParseServiceDetails(policy.MechanismDetails);
        if (details == null)
        {
            return CreateErrorRecord(policy, ChangeOperation.Revert, "Invalid service mechanism details");
        }

        try
        {
            // Parse previous state
            if (string.IsNullOrEmpty(originalChange.PreviousState))
            {
                return CreateErrorRecord(policy, ChangeOperation.Revert, "No previous state recorded, cannot revert");
            }

            // Extract previous startup type from state string
            var previousStartupType = ExtractStartupTypeFromState(originalChange.PreviousState);
            if (previousStartupType == null)
            {
                return CreateErrorRecord(policy, ChangeOperation.Revert, "Could not parse previous startup type");
            }

            // Restore previous startup type
            SetServiceStartupType(details.ServiceName, previousStartupType.Value);

            _logger.LogInformation("Reverted service {ServiceName} startup type to: {StartupType}",
                details.ServiceName, previousStartupType.Value);

            // Optionally start service if it was running before
            var wasRunning = originalChange.PreviousState?.Contains("Status=Running") == true;
            if (wasRunning)
            {
                using var sc = new ServiceController(details.ServiceName);
                sc.Refresh();

                if (sc.Status == ServiceControllerStatus.Stopped && previousStartupType.Value != ServiceStartMode.Disabled)
                {
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                    _logger.LogInformation("Started service: {ServiceName}", details.ServiceName);
                }
            }

            return new ChangeRecord
            {
                ChangeId = Guid.NewGuid().ToString(),
                Operation = ChangeOperation.Revert,
                PolicyId = policy.PolicyId,
                AppliedAt = DateTime.UtcNow,
                Mechanism = MechanismType.Service,
                Description = $"Reverted service: {details.ServiceName}",
                PreviousState = originalChange.NewState ?? string.Empty,
                NewState = originalChange.PreviousState ?? string.Empty,
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revert service: {ServiceName}", details.ServiceName);
            return CreateErrorRecord(policy, ChangeOperation.Revert, ex.Message);
        }
    }

    private ServiceDetails? ParseServiceDetails(object mechanismDetails)
    {
        try
        {
            var json = JsonSerializer.Serialize(mechanismDetails);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var serviceName = GetStringProperty(root, "serviceName");
            if (string.IsNullOrEmpty(serviceName)) return null;

            var displayName = GetStringProperty(root, "displayName");

            // Check if Legacy or Granular
            var isGranular = root.TryGetProperty("type", out var typeProp) &&
                             typeProp.GetString()?.Equals("ServiceConfiguration", StringComparison.OrdinalIgnoreCase) == true;

            string? startupType = null;
            bool stopService = false;

            if (isGranular)
            {
                // If wrapped in configurableOptions
                JsonElement optionsRoot = root;
                if (root.TryGetProperty("configurableOptions", out var configOptions) || 
                    root.TryGetProperty("ConfigurableOptions", out configOptions))
                {
                    optionsRoot = configOptions;
                }

                // Deserialize options
                var optionsJson = optionsRoot.GetRawText();
                var options = JsonSerializer.Deserialize<ServiceConfigOptions>(optionsJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (options != null)
                {
                    // Resolve Startup Type
                    startupType = options.StartupType?.SelectedValue ?? options.StartupType?.RecommendedValue;
                    
                    // Resolve Action (Stop)
                    var action = options.ServiceAction?.SelectedValue ?? options.ServiceAction?.RecommendedValue;
                    stopService = action?.Contains("Stop", StringComparison.OrdinalIgnoreCase) == true;
                    // Note: Action might be "Stop" or "Stop and Disable". Both imply stopping.
                    // If "Stop and Disable", startup type might also be impacted if not set explicitly?
                    // Usually StartupType controls disable state. "Stop" just stops current instance.
                }
            }
            else
            {
                // Legacy
                startupType = GetStringProperty(root, "startupType");
                stopService = root.TryGetProperty("stopService", out var stopProp) && stopProp.GetBoolean();
            }

            // Fallback validation
            if (string.IsNullOrWhiteSpace(startupType) && !stopService)
            {
                // If neither configured, maybe invalid? or just info only?
                // Allow valid parse if we have at least service name, but ApplyAsync might fail if no action needed.
            }

            return new ServiceDetails
            {
                ServiceName = serviceName,
                DisplayName = displayName,
                StartupType = startupType,
                StopService = stopService
            };
        }
        catch
        {
            return null;
        }
    }

    private string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) ||
            element.TryGetProperty(propertyName.ToLowerInvariant(), out prop))
        {
            return prop.GetString();
        }
        return null;
    }

    private ServiceStartMode GetServiceStartupType(string serviceName)
    {
        var keyPath = $@"SYSTEM\CurrentControlSet\Services\{serviceName}";
        using var key = Registry.LocalMachine.OpenSubKey(keyPath, false);

        if (key == null)
        {
            throw new InvalidOperationException($"Service '{serviceName}' does not exist");
        }

        var startValue = (int?)key.GetValue("Start");
        if (startValue == null)
        {
            throw new InvalidOperationException($"Could not read Start value for service '{serviceName}'");
        }

        return startValue.Value switch
        {
            0 => ServiceStartMode.Boot,
            1 => ServiceStartMode.System,
            2 => ServiceStartMode.Automatic,
            3 => ServiceStartMode.Manual,
            4 => ServiceStartMode.Disabled,
            _ => ServiceStartMode.Manual
        };
    }

    private void SetServiceStartupType(string serviceName, ServiceStartMode startMode)
    {
        var keyPath = $@"SYSTEM\CurrentControlSet\Services\{serviceName}";
        using var key = Registry.LocalMachine.OpenSubKey(keyPath, true);

        if (key == null)
        {
            throw new InvalidOperationException($"Service '{serviceName}' does not exist");
        }

        int startValue = startMode switch
        {
            ServiceStartMode.Boot => 0,
            ServiceStartMode.System => 1,
            ServiceStartMode.Automatic => 2,
            ServiceStartMode.Manual => 3,
            ServiceStartMode.Disabled => 4,
            _ => 3
        };

        key.SetValue("Start", startValue, RegistryValueKind.DWord);
    }

    private ServiceStartMode ParseStartupType(string startupType)
    {
        return startupType.ToLowerInvariant() switch
        {
            "automatic" => ServiceStartMode.Automatic,
            "manual" => ServiceStartMode.Manual,
            "disabled" => ServiceStartMode.Disabled,
            "boot" => ServiceStartMode.Boot,
            "system" => ServiceStartMode.System,
            _ => ServiceStartMode.Manual
        };
    }

    private ServiceStartMode? ExtractStartupTypeFromState(string state)
    {
        // Parse "StartupType=Automatic, Status=Running" format
        var parts = state.Split(',');
        var startupPart = parts.FirstOrDefault(p => p.Trim().StartsWith("StartupType="));
        if (startupPart == null) return null;

        var value = startupPart.Split('=')[1].Trim();
        return Enum.TryParse<ServiceStartMode>(value, out var mode) ? mode : null;
    }

    private ChangeRecord CreateErrorRecord(PolicyDefinition policy, ChangeOperation operation, string error)
    {
        return new ChangeRecord
        {
            ChangeId = Guid.NewGuid().ToString(),
            Operation = operation,
            PolicyId = policy.PolicyId,
            AppliedAt = DateTime.UtcNow,
            Mechanism = MechanismType.Service,
            Description = "Failed to apply service policy",
            PreviousState = null,
            NewState = "[error]",
            Success = false,
            ErrorMessage = error
        };
    }
}

internal sealed class ServiceDetails
{
    public required string ServiceName { get; init; }
    public string? DisplayName { get; init; }
    public string? StartupType { get; init; }
    public bool StopService { get; init; }
}
