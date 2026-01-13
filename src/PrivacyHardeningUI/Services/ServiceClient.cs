using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PrivacyHardeningContracts.Commands;
using PrivacyHardeningContracts.Models;
using PrivacyHardeningContracts.Responses;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PrivacyHardeningUI.Services;

/// <summary>
/// IPC client for communicating with the Windows Service
/// Falls back to standalone mode if service is not available
/// </summary>
public sealed class ServiceClient : IDisposable
{
    private const string PipeName = "PrivacyHardeningService_v1";
    private const int TimeoutMs = 1500; // Fast fallback for local named pipe

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    /// <summary>
    /// Raised when the service emits progress updates during long-running operations (Apply)
    /// </summary>
    public event Action<int, string?>? ProgressReceived;

    /// <summary>
    /// Raised when the client transitions into or out of standalone/read-only mode.
    /// </summary>
    public event EventHandler<bool>? StandaloneModeChanged;

    private readonly IDeserializer _yamlDeserializer;
    private bool _standaloneMode = false;
    private PolicyDefinition[]? _cachedPolicies;
    private FileSystemWatcher? _policyWatcher;
    private string? _resolvedPolicyDirectory;
    private readonly object _cacheLock = new();

    /// <summary>
    /// True when the UI has fallen back to offline/standalone mode (no service IPC).
    /// </summary>
    public bool IsStandaloneMode => _standaloneMode;

    /// <summary>
    /// Resets the standalone flag to allow the client to attempt a service reconnection.
    /// </summary>
    public void Reconnect()
    {
        SetStandaloneMode(false);
    }

    private void SetStandaloneMode(bool value)
    {
        if (_standaloneMode == value)
        {
            return;
        }

        _standaloneMode = value;
        StandaloneModeChanged?.Invoke(this, value);
    }

    public ServiceClient()
    {
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public void Dispose()
    {
        try
        {
            _policyWatcher?.Dispose();
        }
        catch
        {
            // Best-effort cleanup. Nothing in here should crash shutdown.
        }
        finally
        {
            _policyWatcher = null;
        }
    }

    private static ErrorInfo ServiceUnavailableError(string? details = null) => new()
    {
        Code = "ServiceUnavailable",
        Message = "Service is not running. Running in standalone (read-only) mode.",
        Details = details
    };

    private static SystemInfo CreateBasicSystemInfo() => new()
    {
        WindowsBuild = Environment.OSVersion.Version.Build,
        WindowsVersion = Environment.OSVersion.VersionString,
        WindowsSku = "Unknown (Standalone)",
        IsDomainJoined = false,
        IsMDMManaged = false,
        IsSCCMManaged = false,
        IsEntraIDJoined = false,
        DefenderTamperProtectionEnabled = false
    };

    private static GetStateResult CreateStandaloneState(bool includeHistory)
    {
        var now = DateTime.UtcNow;
        return new GetStateResult
        {
            CommandId = Guid.NewGuid().ToString(),
            Success = false,
            Errors = new[] { ServiceUnavailableError("State/history requires the background service.") },
            Warnings = new[] { "Read-only mode: install/start the service to enable audit/apply/history." },
            CurrentState = new SystemSnapshot
            {
                SnapshotId = "standalone",
                CreatedAt = now,
                WindowsBuild = Environment.OSVersion.Version.Build,
                WindowsSku = "Unknown",
                AppliedPolicies = Array.Empty<string>(),
                ChangeHistory = Array.Empty<ChangeRecord>(),
                Description = "Standalone (service unavailable)"
            },
            AppliedPolicies = Array.Empty<string>(),
            SystemInfo = CreateBasicSystemInfo()
        };
    }

    private static AuditResult CreateStandaloneAudit()
    {
        return new AuditResult
        {
            CommandId = Guid.NewGuid().ToString(),
            Success = false,
            Errors = new[] { ServiceUnavailableError("Audit requires privileged access via the background service.") },
            Items = Array.Empty<PolicyAuditItem>(),
            SystemInfo = CreateBasicSystemInfo()
        };
    }

    private static DriftDetectionResult CreateStandaloneDrift()
    {
        return new DriftDetectionResult
        {
            CommandId = Guid.NewGuid().ToString(),
            Success = false,
            Errors = new[] { ServiceUnavailableError("Drift detection requires the background service.") },
            DriftDetected = false,
            DriftedPolicies = Array.Empty<DriftItem>(),
            LastAppliedAt = null,
            BaselineSnapshotId = null
        };
    }

    public async Task<TResponse> SendCommandAsync<TResponse>(CommandBase command)
        where TResponse : ResponseBase
    {
        try
        {
            await using var pipeClient = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await pipeClient.ConnectAsync(TimeoutMs);

            // Send command
            var cmdJson = JsonSerializer.Serialize(command, typeof(CommandBase), _jsonOptions) + "\n";
            var cmdBytes = System.Text.Encoding.UTF8.GetBytes(cmdJson);
            await pipeClient.WriteAsync(cmdBytes, 0, cmdBytes.Length);
            await pipeClient.FlushAsync();

            using var sr = new StreamReader(pipeClient, System.Text.Encoding.UTF8);

            // If an ApplyResult is expected we may receive multiple newline-delimited JSON messages (progress + final)
            if (typeof(TResponse) == typeof(PrivacyHardeningContracts.Responses.ApplyResult))
            {
                while (true)
                {
                    var line = await sr.ReadLineAsync();
                    if (line == null) throw new InvalidOperationException("Service closed connection unexpectedly");

                    // Inspect message
                    using var doc = System.Text.Json.JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    // Detect progress message by presence of Percent property
                    if (root.TryGetProperty("Percent", out var percentElem))
                    {
                        var percent = percentElem.GetInt32();
                        string? msg = null;
                        if (root.TryGetProperty("Message", out var msgElem)) msg = msgElem.GetString();
                        ProgressReceived?.Invoke(percent, msg);
                        continue;
                    }

                    // Otherwise assume final response of expected type
                    var response = System.Text.Json.JsonSerializer.Deserialize<TResponse>(line, _jsonOptions);
                    if (response == null) throw new InvalidOperationException("Received null response from service");
                    
                    // Connection successful, ensure we are out of standalone mode
                    SetStandaloneMode(false);

                    if (!response.Success)
                    {
                        var first = response.Errors?.FirstOrDefault();
                        var code = first?.Code ?? "Error";
                        var message = first?.Message ?? "Service reported failure";

                        if (string.Equals(code, "Unauthorized", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new UnauthorizedAccessException(message);
                        }

                        throw new InvalidOperationException($"Service error: {code} - {message}");
                    }

                    return response;
                }
            }

            // Default: read a single response line
            var respLine = await sr.ReadLineAsync();
            if (respLine == null) throw new InvalidOperationException("Service closed connection unexpectedly");

            var singleResponse = System.Text.Json.JsonSerializer.Deserialize<TResponse>(respLine, _jsonOptions);
            if (singleResponse == null)
                throw new InvalidOperationException("Received null response from service");

            // Connection successful, ensure we are out of standalone mode
            SetStandaloneMode(false);

            if (!singleResponse.Success)
            {
                var first = singleResponse.Errors?.FirstOrDefault();
                var code = first?.Code ?? "Error";
                var message = first?.Message ?? "Service reported failure";

                if (string.Equals(code, "Unauthorized", StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException(message);
                }

                throw new InvalidOperationException($"Service error: {code} - {message}");
            }

            return singleResponse;
        }
        catch (TimeoutException)
        {
            SetStandaloneMode(true);
            throw new ServiceUnavailableException("Service not running (standalone/read-only mode)");
        }
        catch (IOException)
        {
            SetStandaloneMode(true);
            throw new ServiceUnavailableException("Service not running (standalone/read-only mode)");
        }
    }

    public async Task<GetPoliciesResult> GetPoliciesAsync(bool onlyApplicable = true)
    {
        // Try standalone mode if previously detected service is down
        if (_standaloneMode)
        {
            return await GetPoliciesStandaloneAsync(onlyApplicable);
        }

        try
        {
            var command = new GetPoliciesCommand
            {
                OnlyApplicable = onlyApplicable
            };

            return await SendCommandAsync<GetPoliciesResult>(command);
        }
        catch
        {
            // Fallback to standalone mode
            SetStandaloneMode(true);
            return await GetPoliciesStandaloneAsync(onlyApplicable);
        }
    }

    private async Task<GetPoliciesResult> GetPoliciesStandaloneAsync(bool onlyApplicable)
    {
        if (_cachedPolicies == null)
        {
            _cachedPolicies = await LoadPoliciesFromDiskAsync();
        }

        return new GetPoliciesResult
        {
            CommandId = Guid.NewGuid().ToString(),
            Success = true,
            Policies = _cachedPolicies,
            ManifestVersion = "1.0.0",
            LastUpdated = DateTime.UtcNow
        };
    }

    private async Task<PolicyDefinition[]> LoadPoliciesFromDiskAsync()
    {
        var policies = new List<PolicyDefinition>();

        var policyDirectory = ResolvePolicyDirectory();
        if (string.IsNullOrEmpty(policyDirectory) || !Directory.Exists(policyDirectory))
        {
            Console.WriteLine("Policy directory not found. Standalone mode will have 0 policies.");
            return policies.ToArray();
        }

        EnsurePolicyWatcherInitialized(policyDirectory);

        Console.WriteLine($"Loading policies from: {policyDirectory}");

        // Recursively find all YAML files
        var yamlFiles = Directory.EnumerateFiles(policyDirectory, "*.yaml", SearchOption.AllDirectories);

        foreach (var file in yamlFiles)
        {
            try
            {
                var yaml = await File.ReadAllTextAsync(file);
                var policy = _yamlDeserializer.Deserialize<PolicyDefinition>(yaml);

                if (policy != null)
                {
                    policies.Add(policy);
                    Console.WriteLine($"Loaded policy: {policy.PolicyId} - {policy.Name}");
                }
            }
            catch (Exception ex)
            {
                // Skip invalid policy files
                Console.WriteLine($"Failed to load policy from {file}: {ex.Message}");
            }
        }

        Console.WriteLine($"Total policies loaded: {policies.Count}");
        return policies.ToArray();
    }

    private string? ResolvePolicyDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_resolvedPolicyDirectory) && Directory.Exists(_resolvedPolicyDirectory))
        {
            return _resolvedPolicyDirectory;
        }

        // Resiliently find policies directory by searching upwards from executable.
        // This matches the dev layout and supports running from build outputs.
        var baseDir = AppContext.BaseDirectory;
        var current = new DirectoryInfo(baseDir);

        while (current != null)
        {
            var testPath = Path.Combine(current.FullName, "policies");
            if (Directory.Exists(testPath) && Directory.EnumerateFiles(testPath, "*.yaml", SearchOption.AllDirectories).Any())
            {
                _resolvedPolicyDirectory = testPath;
                return testPath;
            }

            current = current.Parent;
        }

        // Try relative to working directory as fallback.
        var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), "policies");
        if (Directory.Exists(cwdPath))
        {
            _resolvedPolicyDirectory = cwdPath;
            return cwdPath;
        }

        return null;
    }

    private void EnsurePolicyWatcherInitialized(string policyDirectory)
    {
        if (_policyWatcher != null)
        {
            return;
        }

        try
        {
            _policyWatcher = new FileSystemWatcher(policyDirectory, "*.yaml")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
            };

            void Invalidate(object? _, FileSystemEventArgs __)
            {
                lock (_cacheLock)
                {
                    _cachedPolicies = null;
                }
            }

            void InvalidateRenamed(object? _, RenamedEventArgs __)
            {
                lock (_cacheLock)
                {
                    _cachedPolicies = null;
                }
            }

            _policyWatcher.Changed += Invalidate;
            _policyWatcher.Created += Invalidate;
            _policyWatcher.Deleted += Invalidate;
            _policyWatcher.Renamed += InvalidateRenamed;
            _policyWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            // Non-fatal: standalone mode will still work, just without hot reload.
            Console.WriteLine($"Failed to initialize policy file watcher: {ex.Message}");
        }
    }

    public async Task<AuditResult> AuditAsync(string[]? policyIds = null)
    {
        if (_standaloneMode)
        {
            return CreateStandaloneAudit();
        }

        var command = new AuditCommand
        {
            PolicyIds = policyIds,
            IncludeDetails = true
        };

        try { return await SendCommandAsync<AuditResult>(command); }
        catch (ServiceUnavailableException)
        {
            // Service unreachable; keep UI in a stable read-only state.
            SetStandaloneMode(true);
            return CreateStandaloneAudit();
        }
    }

    public async Task<ApplyResult> ApplyAsync(string[] policyIds, bool createRestorePoint = true, bool dryRun = false)
    {
        if (_standaloneMode)
        {
            throw new InvalidOperationException("Cannot apply policies in standalone mode. The background service must be running.");
        }

        var command = new ApplyCommand
        {
            PolicyIds = policyIds,
            CreateRestorePoint = createRestorePoint,
            DryRun = dryRun,
            ContinueOnError = false
        };

        return await SendCommandAsync<ApplyResult>(command);
    }

    public async Task<RevertResult> RevertAsync(string[]? policyIds = null, string? snapshotId = null)
    {
        if (_standaloneMode)
        {
            throw new InvalidOperationException("Cannot revert policies in standalone mode. The background service must be running.");
        }

        var command = new RevertCommand
        {
            PolicyIds = policyIds,
            SnapshotId = snapshotId,
            CreateRestorePoint = true
        };

        return await SendCommandAsync<RevertResult>(command);
    }

    public async Task<GetStateResult> GetStateAsync(bool includeHistory = false)
    {
        if (_standaloneMode)
        {
            return CreateStandaloneState(includeHistory);
        }

        var command = new GetStateCommand
        {
            IncludeHistory = includeHistory
        };

        try { return await SendCommandAsync<GetStateResult>(command); }
        catch (ServiceUnavailableException)
        {
            SetStandaloneMode(true);
            return CreateStandaloneState(includeHistory);
        }
    }

    public async Task<DriftDetectionResult> DetectDriftAsync(string? snapshotId = null)
    {
        if (_standaloneMode)
        {
            return CreateStandaloneDrift();
        }

        var command = new DetectDriftCommand
        {
            SnapshotId = snapshotId
        };

        try { return await SendCommandAsync<DriftDetectionResult>(command); }
        catch (ServiceUnavailableException)
        {
            SetStandaloneMode(true);
            return CreateStandaloneDrift();
        }
    }
}
