using System.IO.Pipes;
using System.Text.Json;
using PrivacyHardeningContracts.Commands;
using PrivacyHardeningContracts.Responses;

namespace PrivacyHardeningCLI;

/// <summary>
/// Command-line tool for troubleshooting and safe mode recovery
/// </summary>
class Program
{
    private const string PipeName = "PrivacyHardeningService_v1";

    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("Privacy Hardening Framework - CLI Tool");
        Console.WriteLine("======================================\n");

        if (args.Length == 0)
        {
            ShowHelp();
            return 0;
        }

        var command = args[0].ToLowerInvariant();

        try
        {
            return command switch
            {
                "audit" => await RunAuditAsync(),
                "revert-all" => await RevertAllAsync(),
                "list-policies" => await ListPoliciesAsync(),
                "test-connection" => await TestConnectionAsync(),
                _ => throw new ArgumentException($"Unknown command: {command}")
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
        Console.WriteLine("Usage: PrivacyHardeningCLI.exe <command> [options]\n");
        Console.WriteLine("Commands:");
        Console.WriteLine("  audit              - Run audit and show current state");
        Console.WriteLine("  revert-all         - Revert all applied policies (emergency rollback)");
        Console.WriteLine("  list-policies      - List all available policies");
        Console.WriteLine("  test-connection    - Test connection to service");
        Console.WriteLine("\nExamples:");
        Console.WriteLine("  PrivacyHardeningCLI.exe audit");
        Console.WriteLine("  PrivacyHardeningCLI.exe revert-all");
    }

    static async Task<int> RunAuditAsync()
    {
        Console.WriteLine("Running audit...\n");

        var command = new AuditCommand { IncludeDetails = true };
        var result = await SendCommandAsync<AuditResult>(command);

        Console.WriteLine($"Total policies checked: {result.Items.Length}");
        Console.WriteLine($"Applied: {result.Items.Count(i => i.IsApplied)}");
        Console.WriteLine($"Not applied: {result.Items.Count(i => !i.IsApplied)}\n");

        foreach (var item in result.Items.Where(i => i.IsApplied))
        {
            Console.WriteLine($"[APPLIED] {item.PolicyName} ({item.PolicyId})");
        }

        return 0;
    }

    static async Task<int> RevertAllAsync()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("WARNING: This will revert ALL applied policies.");
        Console.ResetColor();
        Console.Write("Continue? (yes/no): ");

        var confirm = Console.ReadLine();
        if (confirm?.ToLowerInvariant() != "yes")
        {
            Console.WriteLine("Cancelled.");
            return 0;
        }

        Console.WriteLine("Reverting all policies...\n");

        var command = new RevertCommand
        {
            PolicyIds = null, // null = revert all
            CreateRestorePoint = true
        };

        var result = await SendCommandAsync<RevertResult>(command);

        if (result.Success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Successfully reverted {result.RevertedPolicies.Length} policies.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Revert completed with errors. Reverted: {result.RevertedPolicies.Length}, Failed: {result.FailedPolicies.Length}");
            Console.ResetColor();
        }

        return result.Success ? 0 : 1;
    }

    static async Task<int> ListPoliciesAsync()
    {
        Console.WriteLine("Loading policies...\n");

        var command = new GetPoliciesCommand { OnlyApplicable = false };
        var result = await SendCommandAsync<GetPoliciesResult>(command);

        Console.WriteLine($"Total policies: {result.Policies.Length}\n");

        foreach (var category in result.Policies.GroupBy(p => p.Category))
        {
            Console.WriteLine($"\n{category.Key}:");
            foreach (var policy in category)
            {
                Console.WriteLine($"  - {policy.PolicyId}: {policy.Name}");
                Console.WriteLine($"    Risk: {policy.RiskLevel}, Support: {policy.SupportStatus}");
            }
        }

        return 0;
    }

    static async Task<int> TestConnectionAsync()
    {
        Console.WriteLine("Testing connection to service...");

        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            await client.ConnectAsync(5000);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Connection successful.");
            Console.ResetColor();
            return 0;
        }
        catch
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Connection failed. Is the service running?");
            Console.ResetColor();
            return 1;
        }
    }

    static async Task<TResponse> SendCommandAsync<TResponse>(CommandBase command)
        where TResponse : ResponseBase
    {
        await using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
        await client.ConnectAsync(30000);

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        await JsonSerializer.SerializeAsync(client, command, command.GetType(), jsonOptions);
        await client.FlushAsync();

        var response = await JsonSerializer.DeserializeAsync<TResponse>(client, jsonOptions);
        return response ?? throw new InvalidOperationException("Null response from service");
    }
}
