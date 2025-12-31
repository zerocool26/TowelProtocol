using System.Management.Automation;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningService.Executors;

/// <summary>
/// Executes Windows Firewall rule policies via PowerShell NetSecurity module
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FirewallExecutor : IExecutor
{
    private readonly ILogger<FirewallExecutor> _logger;
    private const string RuleGroupName = "PrivacyHardeningFramework";

    public MechanismType MechanismType => MechanismType.Firewall;

    public FirewallExecutor(ILogger<FirewallExecutor> logger)
    {
        _logger = logger;
    }

    public async Task<bool> IsAppliedAsync(PolicyDefinition policy, CancellationToken cancellationToken)
    {
        var details = ParseFirewallDetails(policy.MechanismDetails);
        if (details == null) return false;

        try
        {
            using var ps = PowerShell.Create();
            ps.AddCommand("Get-NetFirewallRule")
              .AddParameter("Group", RuleGroupName)
              .AddParameter("ErrorAction", "SilentlyContinue");

            var results = await Task.Run(() => ps.Invoke(), cancellationToken);

            // Check if we have rules matching our prefix
            var ruleCount = results.Count(r =>
            {
                var displayName = r.Properties["DisplayName"]?.Value?.ToString();
                return displayName?.StartsWith(details.RulePrefix) == true;
            });

            return ruleCount > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check firewall rules");
            return false;
        }
    }

    public async Task<string?> GetCurrentValueAsync(PolicyDefinition policy, CancellationToken cancellationToken)
    {
        var details = ParseFirewallDetails(policy.MechanismDetails);
        if (details == null) return null;

        try
        {
            using var ps = PowerShell.Create();
            ps.AddCommand("Get-NetFirewallRule")
              .AddParameter("Group", RuleGroupName)
              .AddParameter("ErrorAction", "SilentlyContinue");

            var results = await Task.Run(() => ps.Invoke(), cancellationToken);

            var matchingRules = results.Where(r =>
            {
                var displayName = r.Properties["DisplayName"]?.Value?.ToString();
                return displayName?.StartsWith(details.RulePrefix) == true;
            }).ToList();

            return $"{matchingRules.Count} firewall rules with prefix {details.RulePrefix}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read firewall rules");
            return null;
        }
    }

    public async Task<ChangeRecord> ApplyAsync(PolicyDefinition policy, CancellationToken cancellationToken)
    {
        var details = ParseFirewallDetails(policy.MechanismDetails);
        if (details == null)
        {
            return CreateErrorRecord(policy, "Invalid firewall mechanism details");
        }

        try
        {
            int createdCount = 0;
            int skippedCount = 0;

            foreach (var endpoint in details.Endpoints)
            {
                var ruleName = $"{details.RulePrefix}_{endpoint}";

                // Check if rule already exists
                using var checkPs = PowerShell.Create();
                checkPs.AddCommand("Get-NetFirewallRule")
                       .AddParameter("DisplayName", ruleName)
                       .AddParameter("ErrorAction", "SilentlyContinue");

                var existing = await Task.Run(() => checkPs.Invoke(), cancellationToken);

                if (existing.Count > 0)
                {
                    _logger.LogDebug("Firewall rule already exists: {RuleName}", ruleName);
                    skippedCount++;
                    continue;
                }

                // Create new rule
                using var createPs = PowerShell.Create();
                createPs.AddCommand("New-NetFirewallRule")
                        .AddParameter("DisplayName", ruleName)
                        .AddParameter("Description", $"Blocks endpoint: {endpoint} (Privacy Hardening Framework)")
                        .AddParameter("Direction", details.Direction)
                        .AddParameter("Action", details.Action)
                        .AddParameter("RemoteAddress", endpoint)
                        .AddParameter("Protocol", "Any")
                        .AddParameter("Enabled", true)
                        .AddParameter("Profile", "Any")
                        .AddParameter("Group", RuleGroupName);

                await Task.Run(() => createPs.Invoke(), cancellationToken);

                if (createPs.HadErrors)
                {
                    var errors = string.Join(", ", createPs.Streams.Error.Select(e => e.ToString()));
                    _logger.LogError("Error creating firewall rule {RuleName}: {Errors}", ruleName, errors);
                }
                else
                {
                    _logger.LogInformation("Created firewall rule: {RuleName}", ruleName);
                    createdCount++;
                }
            }

            return new ChangeRecord
            {
                ChangeId = Guid.NewGuid().ToString(),
                PolicyId = policy.PolicyId,
                AppliedAt = DateTime.UtcNow,
                Mechanism = MechanismType.Firewall,
                Description = $"Created {createdCount} firewall rules, skipped {skippedCount} existing",
                PreviousState = "0 rules",
                NewState = $"{createdCount + skippedCount} rules",
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply firewall policy: {PolicyId}", policy.PolicyId);
            return CreateErrorRecord(policy, ex.Message);
        }
    }

    public async Task<ChangeRecord> RevertAsync(PolicyDefinition policy, ChangeRecord originalChange, CancellationToken cancellationToken)
    {
        var details = ParseFirewallDetails(policy.MechanismDetails);
        if (details == null)
        {
            return CreateErrorRecord(policy, "Invalid firewall mechanism details");
        }

        try
        {
            // Remove all rules with our prefix
            using var ps = PowerShell.Create();
            ps.AddCommand("Get-NetFirewallRule")
              .AddParameter("Group", RuleGroupName)
              .AddParameter("ErrorAction", "SilentlyContinue");

            ps.AddCommand("Where-Object")
              .AddParameter("FilterScript", ScriptBlock.Create($"$_.DisplayName -like '{details.RulePrefix}*'"));

            ps.AddCommand("Remove-NetFirewallRule")
              .AddParameter("ErrorAction", "SilentlyContinue");

            await Task.Run(() => ps.Invoke(), cancellationToken);

            _logger.LogInformation("Removed firewall rules with prefix: {Prefix}", details.RulePrefix);

            return new ChangeRecord
            {
                ChangeId = Guid.NewGuid().ToString(),
                PolicyId = policy.PolicyId,
                AppliedAt = DateTime.UtcNow,
                Mechanism = MechanismType.Firewall,
                Description = $"Removed firewall rules with prefix {details.RulePrefix}",
                PreviousState = originalChange.NewState,
                NewState = "0 rules",
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revert firewall policy: {PolicyId}", policy.PolicyId);
            return CreateErrorRecord(policy, ex.Message);
        }
    }

    private FirewallDetails? ParseFirewallDetails(object mechanismDetails)
    {
        try
        {
            var json = JsonSerializer.Serialize(mechanismDetails);
            return JsonSerializer.Deserialize<FirewallDetails>(json);
        }
        catch
        {
            return null;
        }
    }

    private ChangeRecord CreateErrorRecord(PolicyDefinition policy, string error)
    {
        return new ChangeRecord
        {
            ChangeId = Guid.NewGuid().ToString(),
            PolicyId = policy.PolicyId,
            AppliedAt = DateTime.UtcNow,
            Mechanism = MechanismType.Firewall,
            Description = "Failed to apply firewall policy",
            PreviousState = null,
            NewState = "[error]",
            Success = false,
            ErrorMessage = error
        };
    }
}

internal sealed class FirewallDetails
{
    public required string RulePrefix { get; init; }
    public required string Direction { get; init; }
    public required string Action { get; init; }
    public required string[] Endpoints { get; init; }
}
