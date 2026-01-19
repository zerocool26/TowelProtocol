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
                "apply" => await RunApplyAsync(args.Skip(1).ToArray()),
                "audit" => await RunAuditAsync(),
                "history" => await RunHistoryAsync(args.Skip(1).ToArray()),
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
        Console.WriteLine("  apply [policies]   - Apply specific policies (space separated ids) or all if none specified");
        Console.WriteLine("    --overrides <file.json> : Load configuration overrides from JSON file");
        Console.WriteLine("    --dry-run               : Simulate application without making changes");
        Console.WriteLine("  audit              - Run audit and show current state");
        Console.WriteLine("  revert-all         - Revert all applied policies (emergency rollback)");
        Console.WriteLine("  list-policies      - List all available policies");
        Console.WriteLine("  test-connection    - Test connection to service");
        Console.WriteLine("\nExamples:");
        Console.WriteLine("  PrivacyHardeningCLI.exe apply tel-001 cp-001 --dry-run");
        Console.WriteLine("  PrivacyHardeningCLI.exe apply --overrides my-config.json");
        Console.WriteLine("  PrivacyHardeningCLI.exe history --limit 10");
    }

    static async Task<int> RunHistoryAsync(string[] args)
    {
        int limit = 50; // Default limit
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--limit" && i + 1 < args.Length)
            {
                 if (int.TryParse(args[++i], out var parsed)) limit = parsed;
            }
        }

        Console.WriteLine("Loading history...");

        var command = new GetStateCommand { IncludeHistory = true };
        var result = await SendCommandAsync<GetStateResult>(command);

        if (!result.Success || result.CurrentState == null)
        {
             Console.ForegroundColor = ConsoleColor.Red;
             Console.WriteLine("Failed to verify system state or no history available.");
             Console.ResetColor();
             return 1;
        }

        var history = result.CurrentState.ChangeHistory;
        Console.WriteLine($"Total records: {history.Length}");
        Console.WriteLine($"Showing last {Math.Min(limit, history.Length)} records:\n");

        if (history.Length == 0)
        {
            Console.WriteLine("(No history)");
            return 0;
        }

        // Display reverse chronological (newest first)
        foreach (var change in history.Take(limit))
        {
             var status = change.Success ? "SUCCESS" : "FAILED";
             var color = change.Success ? ConsoleColor.Green : ConsoleColor.Red;
             
             Console.Write($"[{change.AppliedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}] ");
             Console.ForegroundColor = color;
             Console.Write($"{status}");
             Console.ResetColor();
             Console.WriteLine($" {change.Operation} {change.PolicyId}");
             Console.WriteLine($"    Action: {change.Description}");
             if (!change.Success)
             {
                 Console.ForegroundColor = ConsoleColor.Red;
                 Console.WriteLine($"    Error: {change.ErrorMessage}");
                 Console.ResetColor();
             }
             Console.WriteLine();
        }

        return 0;
    }

    static async Task<int> RunApplyAsync(string[] args)
    {
        var policies = new List<string>();
        string? overridesPath = null;
        bool dryRun = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--overrides" && i + 1 < args.Length)
            {
                overridesPath = args[++i];
            }
            else if (args[i] == "--dry-run")
            {
                dryRun = true;
            }
            else if (!args[i].StartsWith("-"))
            {
                policies.Add(args[i]);
            }
        }

        Dictionary<string, string>? overrides = null;
        if (overridesPath != null)
        {
            if (File.Exists(overridesPath))
            {
                 var json = File.ReadAllText(overridesPath);
                 overrides = JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                 Console.WriteLine($"Loaded {overrides?.Count ?? 0} configuration overrides from {overridesPath}");
            }
            else
            {
                Console.WriteLine($"Warning: Overrides file not found: {overridesPath}");
            }
        }

        Console.WriteLine($"Applying {(policies.Count > 0 ? $"{policies.Count} policies" : "all applicable policies")}...");
        if (dryRun) Console.WriteLine("(DRY RUN MODE)");

        var command = new ApplyCommand
        {
            PolicyIds = policies.Count > 0 ? policies.ToArray() : null,
            ConfigurationOverrides = overrides,
            DryRun = dryRun,
            CreateRestorePoint = !dryRun // Default to creating RP
        };

        var result = await SendApplyCommandAsync(command);

        Console.WriteLine();
        if (result.Success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Apply completed successfully.");
            Console.WriteLine($"Applied: {result.AppliedPolicies.Length}");
            Console.WriteLine($"Failed:  {result.FailedPolicies.Length}");
            Console.ResetColor();

            if (result.Changes.Length > 0)
            {
                Console.WriteLine($"Total Changes: {result.Changes.Length}");
            }
        }
        else
        {
             Console.ForegroundColor = ConsoleColor.Red;
             Console.WriteLine("Apply failed.");
             foreach (var err in result.Errors)
             {
                 Console.WriteLine($"[{err.PolicyId}] {err.Message}");
             }
             Console.ResetColor();
        }

        return result.Success ? 0 : 1;
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

    static async Task<ApplyResult> SendApplyCommandAsync(ApplyCommand command)
    {
        await using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
        await client.ConnectAsync(30000);

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        await JsonSerializer.SerializeAsync(client, (CommandBase)command, typeof(CommandBase), jsonOptions);
        await client.FlushAsync();

        // Read streaming responses
        using var reader = new StreamReader(client);
        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break;

            try
            {
                // Try to parse as ProgressResponse first
                var progress = JsonSerializer.Deserialize<ProgressResponse>(line, jsonOptions);
                if (progress != null && progress.Percent >= 0)
                {
                    // Update progress bar
                    DrawProgressBar(progress.Percent, progress.Message);
                    continue;
                }
            }
            catch {}

            try
            {
                // Try as ApplyResult
                var result = JsonSerializer.Deserialize<ApplyResult>(line, jsonOptions);
                if (result != null) return result;
            }
            catch {}
        }

        throw new InvalidOperationException("Stream ended without result");
    }

    static void DrawProgressBar(int percent, string? message)
    {
        Console.CursorLeft = 0;
        Console.Write("[");
        int width = 50;
        int filled = (int)(width * (percent / 100.0));
        Console.Write(new string('#', filled));
        Console.Write(new string('-', width - filled));
        Console.Write($"] {percent}% {message}".PadRight(40));
    }

    static async Task<TResponse> SendCommandAsync<TResponse>(CommandBase command)
        where TResponse : ResponseBase
    {
        await using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
        await client.ConnectAsync(30000);

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        await JsonSerializer.SerializeAsync(client, command, typeof(CommandBase), jsonOptions);
        await client.FlushAsync();

        var response = await JsonSerializer.DeserializeAsync<TResponse>(client, jsonOptions);
        return response ?? throw new InvalidOperationException("Null response from service");
    }
}
