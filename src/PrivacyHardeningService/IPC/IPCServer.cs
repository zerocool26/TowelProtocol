using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PrivacyHardeningContracts.Commands;
using PrivacyHardeningContracts.Responses;
using PrivacyHardeningService.PolicyEngine;
using PrivacyHardeningService.Security;

namespace PrivacyHardeningService.IPC;

/// <summary>
/// Named pipe IPC server for UI-to-Service communication
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class IPCServer
{
    private const string PipeName = "PrivacyHardeningService_v1";
    private const int MaxConcurrentConnections = 4;
    private const int MaxMessageSizeBytes = 1024 * 1024; // 1MB

    private readonly ILogger<IPCServer> _logger;
    private readonly CommandValidator _commandValidator;
    private readonly CallerValidator _callerValidator;
    private readonly PolicyEngineCore _policyEngine;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public IPCServer(
        ILogger<IPCServer> logger,
        CommandValidator commandValidator,
        CallerValidator callerValidator,
        PolicyEngineCore policyEngine)
    {
        _logger = logger;
        _commandValidator = commandValidator;
        _callerValidator = callerValidator;
        _policyEngine = policyEngine;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting IPC server on pipe: {PipeName}", PipeName);

        var tasks = new List<Task>();

        // Create multiple concurrent server instances
        for (int i = 0; i < MaxConcurrentConnections; i++)
        {
            tasks.Add(Task.Run(() => ServerLoopAsync(cancellationToken), cancellationToken));
        }

        await Task.WhenAll(tasks);
    }

    private async Task ServerLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? serverStream = null;
            try
            {
                // Create pipe with restrictive security
                var pipeSecurity = CreateRestrictivePipeSecurity();
                serverStream = NamedPipeServerStreamAcl.Create(
                    PipeName,
                    PipeDirection.InOut,
                    MaxConcurrentConnections,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 4096,
                    outBufferSize: 4096,
                    pipeSecurity);

                _logger.LogDebug("Waiting for client connection...");
                await serverStream.WaitForConnectionAsync(cancellationToken);
                _logger.LogInformation("Client connected");

                await HandleClientAsync(serverStream, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("IPC server shutting down");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in IPC server loop");
            }
            finally
            {
                serverStream?.Dispose();
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream stream, CancellationToken cancellationToken)
    {
        try
        {
            // 1. Read command first (we'll validate caller permissions against the command)
            var command = await ReadCommandAsync(stream, cancellationToken);
            if (command == null)
            {
                await SendErrorResponseAsync(stream, "InvalidCommand", "Failed to deserialize command");
                return;
            }

            // 2. Validate caller identity and permissions for this command
            if (!_callerValidator.ValidateCallerForCommand(stream, command))
            {
                _logger.LogWarning("Rejected unauthorized caller for command {CommandType}", command.CommandType);
                await SendErrorResponseAsync(stream, "Unauthorized", "Caller validation failed");
                return;
            }

            _logger.LogInformation("Received command: {CommandType} ({CommandId})",
                command.CommandType, command.CommandId);

            // 3. Validate command schema
            if (!_commandValidator.ValidateCommand(command))
            {
                await SendErrorResponseAsync(stream, "ValidationFailed", "Command validation failed");
                return;
            }

            // 4. Execute command
            var response = await ExecuteCommandAsync(command, cancellationToken);

            // 5. Send response
            await SendResponseAsync(stream, response, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling client request");
            try
            {
                await SendErrorResponseAsync(stream, "InternalError", ex.Message);
            }
            catch
            {
                // Ignore errors when sending error response
            }
        }
    }

    private async Task<CommandBase?> ReadCommandAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            // Read with size limit to prevent DoS
            using var memoryStream = new MemoryStream();
            var buffer = new byte[4096];
            int totalRead = 0;

            while (totalRead < MaxMessageSizeBytes)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;

                totalRead += read;
                memoryStream.Write(buffer, 0, read);

                // Check if we have a complete message (simple heuristic: stream became empty)
                if (read < buffer.Length) break;
            }

            if (totalRead == 0)
            {
                return null;
            }

            memoryStream.Position = 0;
            return await JsonSerializer.DeserializeAsync<CommandBase>(memoryStream, _jsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize command");
            return null;
        }

    }

    private async Task<ResponseBase> ExecuteCommandAsync(CommandBase command, CancellationToken cancellationToken)
    {
        try
        {
            return command switch
            {
                AuditCommand audit => await _policyEngine.AuditAsync(audit, cancellationToken),
                ApplyCommand apply => await _policyEngine.ApplyAsync(apply, cancellationToken),
                RevertCommand revert => await _policyEngine.RevertAsync(revert, cancellationToken),
                GetPoliciesCommand getPolicies => await _policyEngine.GetPoliciesAsync(getPolicies, cancellationToken),
                GetStateCommand getState => await _policyEngine.GetStateAsync(getState, cancellationToken),
                DetectDriftCommand detectDrift => await _policyEngine.DetectDriftAsync(detectDrift, cancellationToken),
                CreateSnapshotCommand createSnapshot => await _policyEngine.CreateSnapshotAsync(createSnapshot, cancellationToken),
                _ => throw new NotSupportedException($"Command type {command.CommandType} not supported")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command {CommandType}", command.CommandType);
            return CreateErrorResponse(command.CommandId, "ExecutionError", ex.Message);
        }
    }

    private async Task SendResponseAsync(Stream stream, ResponseBase response, CancellationToken cancellationToken)
    {
        await JsonSerializer.SerializeAsync(stream, response, response.GetType(), _jsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private async Task SendErrorResponseAsync(Stream stream, string code, string message)
    {
        var errorResponse = new ErrorResponse
        {
            CommandId = "unknown",
            Success = false,
            Errors = new[]
            {
                new ErrorInfo { Code = code, Message = message }
            }
        };

        await JsonSerializer.SerializeAsync(stream, errorResponse, _jsonOptions);
        await stream.FlushAsync();
    }

    private ResponseBase CreateErrorResponse(string commandId, string code, string message)
    {
        return new ErrorResponse
        {
            CommandId = commandId,
            Success = false,
            Errors = new[]
            {
                new ErrorInfo { Code = code, Message = message }
            }
        };
    }

    private static PipeSecurity CreateRestrictivePipeSecurity()
    {
        var pipeSecurity = new PipeSecurity();

        // Allow Administrators full control
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        // Allow SYSTEM full control
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return pipeSecurity;
    }
}

// Simple error response for protocol errors
internal sealed class ErrorResponse : ResponseBase
{
}
