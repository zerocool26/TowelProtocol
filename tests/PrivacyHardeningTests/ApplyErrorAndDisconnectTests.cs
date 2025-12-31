using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using PrivacyHardeningUI.Services;
using PrivacyHardeningContracts.Commands;
using PrivacyHardeningContracts.Responses;

namespace PrivacyHardeningTests
{
    public class ApplyErrorAndDisconnectTests
    {
        [Fact]
        public async Task ServiceClient_ThrowsOnErrorResponse()
        {
            const string pipeName = "PrivacyHardeningService_v1";

            var serverTask = Task.Run(async () =>
            {
                await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync();

                using var sr = new StreamReader(server);
                using var sw = new StreamWriter(server) { AutoFlush = true };

                var requestLine = await sr.ReadLineAsync();
                var applyCmd = JsonSerializer.Deserialize<ApplyCommand>(requestLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (applyCmd == null) return;

                var err = new ErrorResponse
                {
                    CommandId = applyCmd.CommandId,
                    Success = false,
                    Errors = new[] { new ErrorInfo { Code = "ExecutionError", Message = "Simulated failure" } }
                };

                var json = JsonSerializer.Serialize(err, err.GetType(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                await sw.WriteLineAsync(json);
            });

            var client = new ServiceClient();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ApplyAsync(new[] { "tel-001" }, createRestorePoint: false, dryRun: true));
            Assert.Contains("ExecutionError", ex.Message);

            await serverTask;
        }

        [Fact]
        public async Task ServiceClient_ThrowsOnUnexpectedDisconnect()
        {
            const string pipeName = "PrivacyHardeningService_v1";

            var serverTask = Task.Run(async () =>
            {
                await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync();

                using var sr = new StreamReader(server);
                using var sw = new StreamWriter(server) { AutoFlush = true };

                var requestLine = await sr.ReadLineAsync();
                var applyCmd = JsonSerializer.Deserialize<ApplyCommand>(requestLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (applyCmd == null) return;

                // Send a progress update and then abruptly close
                var prog = new ProgressResponse { CommandId = applyCmd.CommandId, Success = true, Percent = 10, Message = "Starting" };
                await sw.WriteLineAsync(JsonSerializer.Serialize(prog, prog.GetType()));

                // Close without sending final result
                await server.DisposeAsync();
            });

            var client = new ServiceClient();

            await Assert.ThrowsAsync<InvalidOperationException>(() => client.ApplyAsync(new[] { "tel-001" }, createRestorePoint: false, dryRun: true));

            await serverTask;
        }
    }
}
