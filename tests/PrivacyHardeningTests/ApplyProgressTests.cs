using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using PrivacyHardeningUI.Services;
using PrivacyHardeningContracts.Commands;
using PrivacyHardeningContracts.Responses;
using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningTests
{
    public class ApplyProgressTests
    {
        [Fact]
        public async Task ServiceClient_ParsesProgressAndFinalApplyResult()
        {
            const string pipeName = "PrivacyHardeningService_v1";

            // Start a simple in-process pipe server that will read the command then emit progress messages and a final ApplyResult
            var serverTask = Task.Run(async () =>
            {
                await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync();

                using var sr = new StreamReader(server);
                using var sw = new StreamWriter(server) { AutoFlush = true };

                var requestLine = await sr.ReadLineAsync();
                var applyCmd = JsonSerializer.Deserialize<ApplyCommand>(requestLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (applyCmd == null) return;

                // Send a few progress updates
                for (int i = 1; i <= 3; i++)
                {
                    var prog = new ProgressResponse
                    {
                        CommandId = applyCmd.CommandId,
                        Success = true,
                        Percent = i * 30,
                        Message = $"Step {i}"
                    };

                    var json = JsonSerializer.Serialize(prog, prog.GetType(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    await sw.WriteLineAsync(json);
                    await Task.Delay(50);
                }

                // Final result
                var final = new ApplyResult
                {
                    CommandId = applyCmd.CommandId,
                    Success = true,
                    AppliedPolicies = applyCmd.PolicyIds ?? Array.Empty<string>(),
                    FailedPolicies = Array.Empty<string>(),
                    Changes = Array.Empty<ChangeRecord>(),
                    SnapshotId = Guid.NewGuid().ToString(),
                    CompletedAt = DateTime.UtcNow,
                    RestartRecommended = false
                };

                var finalJson = JsonSerializer.Serialize(final, final.GetType(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                await sw.WriteLineAsync(finalJson);

                // Keep connection briefly open
                await Task.Delay(50);
            });

            // Client side
            var client = new ServiceClient();
            var progress = new List<(int, string?)>();
            client.ProgressReceived += (p, m) => progress.Add((p, m));

            var result = await client.ApplyAsync(new[] { "tel-001" }, createRestorePoint: false, dryRun: true);

            Assert.True(result.Success);
            Assert.NotEmpty(progress);
            Assert.Contains(progress, p => p.Item1 > 0);

            await serverTask;
        }
    }
}
