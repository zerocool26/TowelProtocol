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
public sealed class ServiceClient
{
    private const string PipeName = "PrivacyHardeningService_v1";
    private const int TimeoutMs = 5000; // Reduced timeout for faster fallback

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    /// <summary>
    /// Raised when the service emits progress updates during long-running operations (Apply)
    /// </summary>
    public event Action<int, string?>? ProgressReceived;

    private readonly IDeserializer _yamlDeserializer;
    private bool _standaloneMode = false;
    private PolicyDefinition[]? _cachedPolicies;

    public ServiceClient()
    {
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
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
            var cmdJson = JsonSerializer.Serialize(command, command.GetType(), _jsonOptions) + "\n";
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
            _standaloneMode = true;
            throw new InvalidOperationException("Service not running - switching to standalone mode");
        }
        catch (IOException)
        {
            _standaloneMode = true;
            throw new InvalidOperationException("Service not running - switching to standalone mode");
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
            _standaloneMode = true;
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

        // Find policies directory
        var baseDir = AppContext.BaseDirectory;
        var projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        var policyDirectory = Path.Combine(projectRoot, "policies");

        if (!Directory.Exists(policyDirectory))
        {
            // Try relative to working directory
            policyDirectory = Path.Combine(Directory.GetCurrentDirectory(), "policies");
        }

        if (!Directory.Exists(policyDirectory))
        {
            Console.WriteLine($"Policy directory not found. Searched: {policyDirectory}");
            return policies.ToArray();
        }

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

    public async Task<AuditResult> AuditAsync(string[]? policyIds = null)
    {
        if (_standaloneMode)
        {
            // Standalone audit not implemented yet
            return new AuditResult
            {
                CommandId = Guid.NewGuid().ToString(),
                Success = false,
                Items = Array.Empty<PolicyAuditItem>(),
                SystemInfo = new SystemInfo
                {
                    WindowsBuild = Environment.OSVersion.Version.Build,
                    WindowsVersion = Environment.OSVersion.VersionString,
                    WindowsSku = "Unknown",
                    IsDomainJoined = false,
                    IsMDMManaged = false,
                    DefenderTamperProtectionEnabled = false
                }
            };
        }

        var command = new AuditCommand
        {
            PolicyIds = policyIds,
            IncludeDetails = true
        };

        return await SendCommandAsync<AuditResult>(command);
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

    public async Task<RevertResult> RevertAsync(string[]? policyIds = null)
    {
        if (_standaloneMode)
        {
            throw new InvalidOperationException("Cannot revert policies in standalone mode. The background service must be running.");
        }

        var command = new RevertCommand
        {
            PolicyIds = policyIds,
            CreateRestorePoint = true
        };

        return await SendCommandAsync<RevertResult>(command);
    }
}
