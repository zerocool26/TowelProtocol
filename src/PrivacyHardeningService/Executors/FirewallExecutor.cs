using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningService.Executors;

/// <summary>
/// Executes Windows Firewall rule policies via Windows Firewall COM API (HNetCfg.FwPolicy2)
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FirewallExecutor : IExecutor
{
    private readonly ILogger<FirewallExecutor> _logger;
    private const string RuleGroupName = "PrivacyHardeningFramework";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly SemaphoreSlim _auditCacheLock = new(1, 1);
    private DateTime _auditCacheExpiresAtUtc = DateTime.MinValue;
    private FirewallAuditIndex? _auditIndex;

    public MechanismType MechanismType => MechanismType.Firewall;

    public FirewallExecutor(ILogger<FirewallExecutor> logger)
    {
        _logger = logger;
    }

    public async Task<bool> IsAppliedAsync(PolicyDefinition policy, CancellationToken cancellationToken)
    {
        var targets = BuildRuleTargets(policy);
        if (targets.Length == 0) return false;

        try
        {
            var index = await GetAuditIndexAsync(cancellationToken);

            // A firewall policy is considered "applied" only if all of its intended targets are currently blocked
            // (regardless of who created the blocking rule).
            foreach (var target in targets)
            {
                if (!await IsTargetBlockedAsync(target, index, cancellationToken))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to audit firewall rules");
            return false;
        }
    }

    public async Task<string?> GetCurrentValueAsync(PolicyDefinition policy, CancellationToken cancellationToken)
    {
        var targets = BuildRuleTargets(policy);
        if (targets.Length == 0) return null;

        try
        {
            var index = await GetAuditIndexAsync(cancellationToken);
            var parts = new List<string>();

            foreach (var target in targets)
            {
                var state = await GetTargetBlockStateAsync(target, index, cancellationToken);

                if (state.IsBlocked)
                {
                    parts.Add($"{target.RemoteAddress}: Blocked by {state.Evidence.Count} rule(s): {string.Join(", ", state.Evidence.Take(5))}");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(state.Error))
                {
                    parts.Add($"{target.RemoteAddress}: Unknown ({state.Error})");
                    continue;
                }

                parts.Add($"{target.RemoteAddress}: Not blocked");
            }

            return string.Join(" | ", parts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read firewall rule state");
            return null;
        }
    }

    public async Task<ChangeRecord> ApplyAsync(PolicyDefinition policy, CancellationToken cancellationToken)
    {
        try
        {
            var targets = BuildRuleTargets(policy);
            if (targets.Length == 0)
            {
                return CreateErrorRecord(policy, ChangeOperation.Apply, "Invalid firewall mechanism details");
            }

            var createdRules = new List<string>();
            var skippedExistingRules = new List<string>();
            var failures = new List<string>();

            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Check if a rule with this DisplayName already exists. If it does, we DO NOT modify it.
                if (FirewallCom.RuleExists(target.DisplayName))
                {
                    skippedExistingRules.Add(target.DisplayName);
                    continue;
                }

                var normalizedRemoteAddresses = await TryNormalizeRemoteAddressesForRuleAsync(target.RemoteAddress, cancellationToken);
                if (string.IsNullOrWhiteSpace(normalizedRemoteAddresses))
                {
                    failures.Add($"{target.DisplayName}: Invalid RemoteAddress '{target.RemoteAddress}' (must be IP/CIDR/range/keyword; hostnames require DNS resolution)");
                    continue;
                }

                var targetToCreate = new FirewallRuleTarget
                {
                    DisplayName = target.DisplayName,
                    Description = target.Description,
                    Direction = target.Direction,
                    Action = target.Action,
                    Protocol = target.Protocol,
                    RemoteAddress = normalizedRemoteAddresses,
                    Group = target.Group,
                    Enabled = target.Enabled
                };

                if (!FirewallCom.TryCreateRule(targetToCreate, out var error))
                {
                    failures.Add($"{target.DisplayName}: {error ?? "Failed to create rule"}");
                    continue;
                }

                createdRules.Add(target.DisplayName);
            }

            // Invalidate audit cache after modifications.
            InvalidateAuditCache();

            var applyState = new FirewallApplyState
            {
                CreatedRules = createdRules.ToArray(),
                SkippedExistingRules = skippedExistingRules.ToArray()
            };

            var stateJson = JsonSerializer.Serialize(applyState);
            var success = failures.Count == 0;

            return new ChangeRecord
            {
                ChangeId = Guid.NewGuid().ToString(),
                Operation = ChangeOperation.Apply,
                PolicyId = policy.PolicyId,
                AppliedAt = DateTime.UtcNow,
                Mechanism = MechanismType.Firewall,
                Description = $"Firewall rules: created {createdRules.Count}, skipped existing {skippedExistingRules.Count}",
                PreviousState = null,
                NewState = stateJson,
                Success = success,
                ErrorMessage = success ? null : string.Join("; ", failures)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply firewall policy: {PolicyId}", policy.PolicyId);
            return CreateErrorRecord(policy, ChangeOperation.Apply, ex.Message);
        }
    }

    public async Task<ChangeRecord> RevertAsync(PolicyDefinition policy, ChangeRecord originalChange, CancellationToken cancellationToken)
    {
        try
        {
            var targets = BuildRuleTargets(policy);
            if (targets.Length == 0)
            {
                return CreateErrorRecord(policy, ChangeOperation.Revert, "Invalid firewall mechanism details");
            }

            var applyState = TryParseApplyState(originalChange.NewState);
            var createdRules = applyState?.CreatedRules ?? Array.Empty<string>();

            // If we have an explicit list of tool-created rules, only remove those.
            // Otherwise fall back to removing the policy's expected rule(s) but ONLY within the expected Group.
            var toRemove = createdRules.Length > 0
                ? createdRules.Select(r => (DisplayName: r, Group: targets.FirstOrDefault(t => string.Equals(t.DisplayName, r, StringComparison.OrdinalIgnoreCase))?.Group ?? RuleGroupName)).ToArray()
                : targets.Select(t => (t.DisplayName, t.Group)).ToArray();

            if (toRemove.Length == 0)
            {
                return new ChangeRecord
                {
                    ChangeId = Guid.NewGuid().ToString(),
                    Operation = ChangeOperation.Revert,
                    PolicyId = policy.PolicyId,
                    AppliedAt = DateTime.UtcNow,
                    Mechanism = MechanismType.Firewall,
                    Description = "No firewall rules to revert",
                    PreviousState = originalChange.NewState,
                    NewState = originalChange.NewState ?? "[no-op]",
                    Success = true
                };
            }

            var removed = 0;
            var failures = new List<string>();

            foreach (var item in toRemove)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!FirewallCom.TryRemoveRule(item.DisplayName, item.Group, out var didRemove, out var error))
                {
                    failures.Add($"{item.DisplayName}: {error ?? "Failed to remove rule"}");
                    continue;
                }

                if (didRemove)
                {
                    removed++;
                }
            }

            InvalidateAuditCache();

            var success = failures.Count == 0;

            return new ChangeRecord
            {
                ChangeId = Guid.NewGuid().ToString(),
                Operation = ChangeOperation.Revert,
                PolicyId = policy.PolicyId,
                AppliedAt = DateTime.UtcNow,
                Mechanism = MechanismType.Firewall,
                Description = $"Removed {removed} firewall rule(s) (group-scoped)",
                PreviousState = originalChange.NewState,
                NewState = "[removed]",
                Success = success,
                ErrorMessage = success ? null : string.Join("; ", failures)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revert firewall policy: {PolicyId}", policy.PolicyId);
            return CreateErrorRecord(policy, ChangeOperation.Revert, ex.Message);
        }
    }

    private FirewallMechanismDetails? ParseFirewallDetails(object mechanismDetails)
    {
        try
        {
            var json = JsonSerializer.Serialize(mechanismDetails);
            return JsonSerializer.Deserialize<FirewallMechanismDetails>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private FirewallRuleTarget[] BuildRuleTargets(PolicyDefinition policy)
    {
        var details = ParseFirewallDetails(policy.MechanismDetails);
        if (details == null)
        {
            return Array.Empty<FirewallRuleTarget>();
        }

        // New schema: mechanismDetails.firewallRule.* (single rule)
        if (details.FirewallRule != null
            && !string.IsNullOrWhiteSpace(details.FirewallRule.DisplayName)
            && !string.IsNullOrWhiteSpace(details.FirewallRule.RemoteAddress))
        {
            return new[]
            {
                new FirewallRuleTarget
                {
                    DisplayName = details.FirewallRule.DisplayName.Trim(),
                    Description = details.FirewallRule.Description,
                    Direction = string.IsNullOrWhiteSpace(details.FirewallRule.Direction) ? "Outbound" : details.FirewallRule.Direction,
                    Action = string.IsNullOrWhiteSpace(details.FirewallRule.Action) ? "Block" : details.FirewallRule.Action,
                    Protocol = string.IsNullOrWhiteSpace(details.FirewallRule.Protocol) ? "Any" : details.FirewallRule.Protocol,
                    RemoteAddress = details.FirewallRule.RemoteAddress.Trim(),
                    Group = string.IsNullOrWhiteSpace(details.FirewallRule.Group) ? RuleGroupName : details.FirewallRule.Group,
                    Enabled = details.FirewallRule.Enabled ?? true
                }
            };
        }

        // Legacy schema: rulePrefix + endpoints list
        if (!string.IsNullOrWhiteSpace(details.RulePrefix)
            && !string.IsNullOrWhiteSpace(details.Direction)
            && !string.IsNullOrWhiteSpace(details.Action)
            && details.Endpoints != null
            && details.Endpoints.Length > 0)
        {
            return details.Endpoints
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(endpoint => new FirewallRuleTarget
                {
                    DisplayName = $"{details.RulePrefix}_{endpoint}",
                    Description = $"Privacy Hardening Framework policy {policy.PolicyId}",
                    Direction = details.Direction,
                    Action = details.Action,
                    Protocol = "Any",
                    RemoteAddress = endpoint,
                    Group = RuleGroupName,
                    Enabled = true
                })
                .ToArray();
        }

        return Array.Empty<FirewallRuleTarget>();
    }

    private void InvalidateAuditCache()
    {
        _auditIndex = null;
        _auditCacheExpiresAtUtc = DateTime.MinValue;
    }

    private static FirewallApplyState? TryParseApplyState(string? stateJson)
    {
        if (string.IsNullOrWhiteSpace(stateJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FirewallApplyState>(stateJson, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private async Task<FirewallAuditIndex> GetAuditIndexAsync(CancellationToken cancellationToken)
    {
        if (_auditIndex != null && DateTime.UtcNow < _auditCacheExpiresAtUtc)
        {
            return _auditIndex;
        }

        await _auditCacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_auditIndex != null && DateTime.UtcNow < _auditCacheExpiresAtUtc)
            {
                return _auditIndex;
            }

            _auditIndex = await BuildAuditIndexAsync(cancellationToken);
            _auditCacheExpiresAtUtc = DateTime.UtcNow.AddSeconds(5);
            return _auditIndex;
        }
        finally
        {
            _auditCacheLock.Release();
        }
    }

    private async Task<FirewallAuditIndex> BuildAuditIndexAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var byRemote = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var anyRemote = new List<string>();
        var cidrEntries = new List<FirewallCidrEntry>();
        var rangeEntries = new List<FirewallRangeEntry>();

        var (currentProfiles, rules) = FirewallCom.GetRuleSnapshots();

        foreach (var rule in rules)
        {
            if (!rule.Enabled) continue;
            if (rule.Action != FirewallCom.NetFwActionBlock) continue;
            if (rule.Direction != FirewallCom.NetFwDirectionOut) continue;
            if (!FirewallCom.IsRuleEffectiveForCurrentProfile(rule.Profiles, currentProfiles)) continue;

            var evidence = string.IsNullOrWhiteSpace(rule.Grouping) ? rule.Name : $"{rule.Name} (Group={rule.Grouping})";

            var tokens = SplitRemoteAddressTokens(rule.RemoteAddresses).ToArray();
            if (tokens.Length == 0)
            {
                anyRemote.Add(evidence);
                continue;
            }

            foreach (var token in tokens)
            {
                if (IsAnyRemoteToken(token))
                {
                    anyRemote.Add(evidence);
                    continue;
                }

                if (TryParseCidr(token, out var network, out var prefixLength))
                {
                    cidrEntries.Add(new FirewallCidrEntry(network, prefixLength, evidence));
                    continue;
                }

                if (TryParseRange(token, out var start, out var end))
                {
                    rangeEntries.Add(new FirewallRangeEntry(start, end, evidence));
                    continue;
                }

                if (IPAddress.TryParse(token, out var ip))
                {
                    AddEvidence(byRemote, ip.ToString(), evidence);
                    continue;
                }

                AddEvidence(byRemote, token, evidence);
            }
        }

        // De-dupe evidence lists
        foreach (var key in byRemote.Keys.ToArray())
        {
            byRemote[key] = byRemote[key].Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        anyRemote = anyRemote.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        return new FirewallAuditIndex(byRemote, anyRemote, cidrEntries, rangeEntries);
    }

    private ChangeRecord CreateErrorRecord(PolicyDefinition policy, ChangeOperation operation, string error)
    {
        return new ChangeRecord
        {
            ChangeId = Guid.NewGuid().ToString(),
            Operation = operation,
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

    private static void AddEvidence(Dictionary<string, List<string>> byRemote, string remote, string evidence)
    {
        if (!byRemote.TryGetValue(remote, out var list))
        {
            list = new List<string>();
            byRemote[remote] = list;
        }

        list.Add(evidence);
    }

    private static IEnumerable<string> SplitRemoteAddressTokens(string? remoteAddresses)
    {
        if (string.IsNullOrWhiteSpace(remoteAddresses))
        {
            yield break;
        }

        foreach (var token in remoteAddresses
                     .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(token))
            {
                yield return token.Trim();
            }
        }
    }

    private static bool IsAnyRemoteToken(string token)
    {
        return token.Equals("*", StringComparison.OrdinalIgnoreCase)
               || token.Equals("Any", StringComparison.OrdinalIgnoreCase)
               || token.Equals("0.0.0.0/0", StringComparison.OrdinalIgnoreCase)
               || token.Equals("::/0", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseCidr(string value, out IPAddress network, out int prefixLength)
    {
        network = IPAddress.None;
        prefixLength = 0;

        var trimmed = value.Trim();
        var slashIdx = trimmed.IndexOf('/');
        if (slashIdx <= 0 || slashIdx == trimmed.Length - 1)
        {
            return false;
        }

        var addrPart = trimmed.Substring(0, slashIdx);
        var prefixPart = trimmed.Substring(slashIdx + 1);

        if (!IPAddress.TryParse(addrPart, out network))
        {
            return false;
        }

        if (!int.TryParse(prefixPart, out prefixLength))
        {
            return false;
        }

        var max = network.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        return prefixLength >= 0 && prefixLength <= max;
    }

    private static bool TryParseRange(string value, out IPAddress start, out IPAddress end)
    {
        start = IPAddress.None;
        end = IPAddress.None;

        var trimmed = value.Trim();
        var dashIdx = trimmed.IndexOf('-');
        if (dashIdx <= 0 || dashIdx == trimmed.Length - 1)
        {
            return false;
        }

        var startPart = trimmed.Substring(0, dashIdx).Trim();
        var endPart = trimmed.Substring(dashIdx + 1).Trim();

        if (!IPAddress.TryParse(startPart, out start) || !IPAddress.TryParse(endPart, out end))
        {
            return false;
        }

        if (start.AddressFamily != end.AddressFamily)
        {
            return false;
        }

        return true;
    }

    private async Task<string?> TryNormalizeRemoteAddressesForRuleAsync(string remoteAddress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(remoteAddress))
        {
            return null;
        }

        var tokens = SplitRemoteAddressTokens(remoteAddress).ToArray();
        if (tokens.Length == 0)
        {
            return null;
        }

        if (tokens.Any(IsAnyRemoteToken))
        {
            return "*";
        }

        var normalized = new List<string>();

        foreach (var token in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryParseCidr(token, out _, out _) || TryParseRange(token, out _, out _))
            {
                normalized.Add(token);
                continue;
            }

            if (IPAddress.TryParse(token, out var ip))
            {
                normalized.Add(ip.ToString());
                continue;
            }

            // Token isn't an IP/CIDR/range/keyword; treat it as a hostname and resolve.
            try
            {
                var resolved = await Dns.GetHostAddressesAsync(token, cancellationToken);
                if (resolved.Length == 0)
                {
                    return null;
                }

                normalized.AddRange(resolved.Select(a => a.ToString()));
            }
            catch
            {
                return null;
            }
        }

        return string.Join(",", normalized.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private async Task<bool> IsTargetBlockedAsync(FirewallRuleTarget target, FirewallAuditIndex index, CancellationToken cancellationToken)
    {
        var state = await GetTargetBlockStateAsync(target, index, cancellationToken);
        return state.IsBlocked;
    }

    private async Task<FirewallTargetBlockState> GetTargetBlockStateAsync(FirewallRuleTarget target, FirewallAuditIndex index, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(target.RemoteAddress))
        {
            return FirewallTargetBlockState.NotBlocked("RemoteAddress is empty");
        }

        string[] addressesToCheck;
        try
        {
            var normalized = await TryNormalizeRemoteAddressesForRuleAsync(target.RemoteAddress, cancellationToken);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return FirewallTargetBlockState.NotBlocked("RemoteAddress is invalid or could not be resolved");
            }

            if (IsAnyRemoteToken(normalized))
            {
                addressesToCheck = new[] { "*" };
            }
            else
            {
                addressesToCheck = SplitRemoteAddressTokens(normalized).ToArray();
            }
        }
        catch (Exception ex)
        {
            return FirewallTargetBlockState.Fault(ex.Message);
        }

        if (addressesToCheck.Length == 0)
        {
            return FirewallTargetBlockState.NotBlocked("No resolvable addresses");
        }

        var evidence = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var addr in addressesToCheck)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var matches = index.GetBlockingRulesForRemoteAddress(addr);
            if (matches.Count == 0)
            {
                return FirewallTargetBlockState.NotBlocked(null);
            }

            foreach (var item in matches)
            {
                evidence.Add(item);
            }
        }

        return FirewallTargetBlockState.Blocked(evidence.ToArray());
    }
}

internal sealed class FirewallMechanismDetails
{
    public FirewallRuleDetails? FirewallRule { get; init; }

    // Legacy schema
    public string? RulePrefix { get; init; }
    public string? Direction { get; init; }
    public string? Action { get; init; }
    public string[]? Endpoints { get; init; }
}

internal sealed class FirewallRuleDetails
{
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string? Direction { get; init; }
    public string? Action { get; init; }
    public string? Protocol { get; init; }
    public string? RemoteAddress { get; init; }
    public string? Group { get; init; }
    public bool? Enabled { get; init; }
}

internal sealed class FirewallRuleTarget
{
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public required string Direction { get; init; }
    public required string Action { get; init; }
    public required string Protocol { get; init; }
    public required string RemoteAddress { get; init; }
    public required string Group { get; init; }
    public required bool Enabled { get; init; }
    public string? Program { get; init; }
    public string? Service { get; init; }
}

internal sealed class FirewallApplyState
{
    public string[] CreatedRules { get; init; } = Array.Empty<string>();
    public string[] SkippedExistingRules { get; init; } = Array.Empty<string>();
}

internal sealed class FirewallAuditIndex
{
    private readonly Dictionary<string, List<string>> _blockingRulesByRemoteAddress;
    private readonly List<string> _anyRemoteBlockingRules;
    private readonly List<FirewallCidrEntry> _cidrEntries;
    private readonly List<FirewallRangeEntry> _rangeEntries;

    public FirewallAuditIndex(
        Dictionary<string, List<string>> blockingRulesByRemoteAddress,
        List<string> anyRemoteBlockingRules,
        List<FirewallCidrEntry> cidrEntries,
        List<FirewallRangeEntry> rangeEntries)
    {
        _blockingRulesByRemoteAddress = blockingRulesByRemoteAddress;
        _anyRemoteBlockingRules = anyRemoteBlockingRules;
        _cidrEntries = cidrEntries;
        _rangeEntries = rangeEntries;
    }

    public bool HasBlockingRuleForRemoteAddress(string remoteAddress)
    {
        return GetBlockingRulesForRemoteAddress(remoteAddress).Count > 0;
    }

    public IReadOnlyList<string> GetBlockingRulesForRemoteAddress(string remoteAddress)
    {
        if (_blockingRulesByRemoteAddress.TryGetValue(remoteAddress, out var list) && list.Count > 0)
        {
            return list;
        }

        if (IPAddress.TryParse(remoteAddress, out var ip))
        {
            var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in _cidrEntries)
            {
                if (entry.Network.AddressFamily != ip.AddressFamily) continue;

                if (IsInCidr(ip, entry.Network, entry.PrefixLength))
                {
                    matches.Add(entry.Evidence);
                }
            }

            foreach (var entry in _rangeEntries)
            {
                if (entry.Start.AddressFamily != ip.AddressFamily) continue;

                if (IsInRange(ip, entry.Start, entry.End))
                {
                    matches.Add(entry.Evidence);
                }
            }

            if (matches.Count > 0)
            {
                return matches.ToArray();
            }
        }

        // A broad "Any" remote-address block rule effectively blocks all endpoints.
        if (_anyRemoteBlockingRules.Count > 0)
        {
            return _anyRemoteBlockingRules;
        }

        return Array.Empty<string>();
    }

    private static bool IsInCidr(IPAddress address, IPAddress network, int prefixLength)
    {
        var addressBytes = address.GetAddressBytes();
        var networkBytes = network.GetAddressBytes();

        var bytesToCompare = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var i = 0; i < bytesToCompare; i++)
        {
            if (addressBytes[i] != networkBytes[i])
            {
                return false;
            }
        }

        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)(~(0xFF >> remainingBits));
        return (addressBytes[bytesToCompare] & mask) == (networkBytes[bytesToCompare] & mask);
    }

    private static bool IsInRange(IPAddress address, IPAddress start, IPAddress end)
    {
        var addressBytes = address.GetAddressBytes();
        var startBytes = start.GetAddressBytes();
        var endBytes = end.GetAddressBytes();

        return CompareBytes(addressBytes, startBytes) >= 0 && CompareBytes(addressBytes, endBytes) <= 0;
    }

    private static int CompareBytes(byte[] a, byte[] b)
    {
        for (var i = 0; i < a.Length && i < b.Length; i++)
        {
            var diff = a[i].CompareTo(b[i]);
            if (diff != 0)
            {
                return diff;
            }
        }

        return a.Length.CompareTo(b.Length);
    }
}

internal readonly record struct FirewallCidrEntry(IPAddress Network, int PrefixLength, string Evidence);
internal readonly record struct FirewallRangeEntry(IPAddress Start, IPAddress End, string Evidence);

internal sealed class FirewallTargetBlockState
{
    public required bool IsBlocked { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    public static FirewallTargetBlockState Blocked(IReadOnlyList<string> evidence) => new()
    {
        IsBlocked = true,
        Evidence = evidence ?? Array.Empty<string>()
    };

    public static FirewallTargetBlockState NotBlocked(string? error) => new()
    {
        IsBlocked = false,
        Error = error
    };

    public static FirewallTargetBlockState Fault(string error) => new()
    {
        IsBlocked = false,
        Error = error
    };
}
