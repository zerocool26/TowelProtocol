using System.IO.Pipes;
using System.Text.Json;
using PrivacyHardeningContracts.Commands;
using PrivacyHardeningContracts.Responses;

namespace PrivacyHardeningElevated;

class Program
{
    private const string PipeName = "PrivacyHardeningService_v1";

    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("PrivacyHardening Elevated Helper - running with elevated privileges");

        if (args.Length == 0)
        {
            ShowHelp();
            return 0;
        }

        var cmd = args[0].ToLowerInvariant();

        try
        {
            return cmd switch
            {
                "apply" => await RunApplyAsync(args.Skip(1).ToArray()),
                "revert-all" => await RunRevertAllAsync(),
                _ => UnknownCommand(cmd)
            };
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    static void ShowHelp()
    {
        Console.WriteLine("Usage: PrivacyHardeningElevated.exe <command> [options]\n");
        Console.WriteLine("Commands:");
        Console.WriteLine("  apply <policyId> [policyId ...]   - Apply listed policies (requires elevation)");
        Console.WriteLine("  revert-all                         - Revert all applied policies (requires elevation)");
    }

    static int UnknownCommand(string cmd)
    {
        Console.WriteLine($"Unknown command: {cmd}");
        ShowHelp();
        return 1;
    }

    static async Task<int> RunApplyAsync(string[] policyIds)
    {
        if (policyIds == null || policyIds.Length == 0)
        {
            Console.WriteLine("No policy IDs provided.");
            return 1;
        }

        var command = new ApplyCommand { PolicyIds = policyIds, CreateRestorePoint = true, DryRun = false };
        var result = await SendCommandAsync<ApplyResult>(command);

        if (result.Success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Applied {result.AppliedPolicies?.Length ?? 0} policies.");
            Console.ResetColor();
            return 0;
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Apply failed: {string.Join(';', result.Errors.Select(e => e.Message))}");
        Console.ResetColor();
        return 1;
    }

    static async Task<int> RunRevertAllAsync()
    {
        var command = new RevertCommand { PolicyIds = null, CreateRestorePoint = true };
        var result = await SendCommandAsync<RevertResult>(command);

        if (result.Success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Reverted {result.RevertedPolicies?.Length ?? 0} policies.");
            Console.ResetColor();
            return 0;
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Revert failed: {string.Join(';', result.Errors.Select(e => e.Message))}");
        Console.ResetColor();
        return 1;
    }

    static async Task<TResponse> SendCommandAsync<TResponse>(CommandBase command)
        where TResponse : ResponseBase
    {
        await using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(10000);

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        await JsonSerializer.SerializeAsync(client, command, command.GetType(), jsonOptions);
        await client.FlushAsync();

        var response = await JsonSerializer.DeserializeAsync<TResponse>(client, jsonOptions);
        return response ?? throw new InvalidOperationException("Null response from service");
    }
}
