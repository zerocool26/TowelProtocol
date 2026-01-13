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

    private static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();

        // Always allow the creator/owner full control (ensures the service can create additional instances).
        security.AddAccessRule(
            new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.CreatorOwnerSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

        // Defense-in-depth: deny remote network/anonymous access to this local-only IPC pipe.
        security.AddAccessRule(
            new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.AnonymousSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Deny));

        security.AddAccessRule(
            new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Deny));

        // Allow LocalSystem and Administrators full control (service + elevated helper scenarios)
        security.AddAccessRule(
            new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

        security.AddAccessRule(
            new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

        // Allow interactive users to connect and exchange data (read-only operations are still enforced by CallerValidator).
        security.AddAccessRule(
            new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
                PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize,
                AccessControlType.Allow));

        // Ensure the account running the service can always create additional instances (dev console-run scenario).
        var currentUserSid = WindowsIdentity.GetCurrent()?.User;
        if (currentUserSid != null)
        {
            security.AddAccessRule(new PipeAccessRule(currentUserSid, PipeAccessRights.FullControl, AccessControlType.Allow));
        }

        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        return security;
    }

    private async Task ServerLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? serverStream = null;
            try
            {
                // Create pipe server instance with explicit security (some environments have restrictive default DACLs).
                // Authorization is enforced after connection by CallerValidator.
                var pipeSecurity = CreatePipeSecurity();
                serverStream = NamedPipeServerStreamAcl.Create(
                    pipeName: PipeName,
                    direction: PipeDirection.InOut,
                    maxNumberOfServerInstances: MaxConcurrentConnections,
                    transmissionMode: PipeTransmissionMode.Byte,
                    options: PipeOptions.Asynchronous,
                    inBufferSize: 4096,
                    outBufferSize: 4096,
                    pipeSecurity: pipeSecurity,
                    inheritability: HandleInheritability.None,
                    additionalAccessRights: (PipeAccessRights)0);

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
                // Avoid tight spin if pipe creation repeatedly fails.
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
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
            // 4. Execute command
            if (command is ApplyCommand applyCmd)
            {
                // Special-case apply: stream progress updates then final result
                await ExecuteApplyWithProgressAsync(stream, applyCmd, cancellationToken);
                return;
            }

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
        var json = JsonSerializer.Serialize(response, response.GetType(), _jsonOptions);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json + "\n");
        await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
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

        try
        {
            var json = JsonSerializer.Serialize(errorResponse, _jsonOptions);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json + "\n");
            await stream.WriteAsync(bytes, 0, bytes.Length);
            await stream.FlushAsync();
        }
        catch (IOException)
        {
            // Client disconnected; nothing to do.
        }
        catch (ObjectDisposedException)
        {
            // Stream already closed; nothing to do.
        }
    }

    private async Task ExecuteApplyWithProgressAsync(Stream stream, ApplyCommand applyCmd, CancellationToken cancellationToken)
    {
        // progress callback writes ProgressResponse messages to the stream
        void ProgressCallback(int percent, string? message)
        {
            try
            {
                var progress = new PrivacyHardeningContracts.Responses.ProgressResponse
                {
                    CommandId = applyCmd.CommandId,
                    Success = true,
                    Percent = percent,
                    Message = message
                };

                var json = JsonSerializer.Serialize(progress, progress.GetType(), _jsonOptions) + "\n";
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                // Fire-and-forget write; use synchronous write to avoid async-over-sync complications in callback
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to send progress update");
            }
        }

        // Execute apply with progress callback
        var result = await _policyEngine.ApplyAsync(applyCmd, ProgressCallback, cancellationToken);

        // Send final apply result
        await SendResponseAsync(stream, result, cancellationToken);
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

}

// Simple error response for protocol errors
internal sealed class ErrorResponse : ResponseBase
{
}
