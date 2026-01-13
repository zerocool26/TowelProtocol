using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PrivacyHardeningService.Executors;

internal static class FirewallCom
{
    // NET_FW_* constants from Windows Firewall COM API
    internal const int NetFwActionBlock = 0;
    internal const int NetFwActionAllow = 1;
    internal const int NetFwDirectionIn = 1;
    internal const int NetFwDirectionOut = 2;
    internal const int NetFwProfile2All = int.MaxValue; // 0x7FFFFFFF
    internal const int NetFwIpProtocolAny = 256;

    internal sealed record FirewallRuleSnapshot(
        string Name,
        bool Enabled,
        int Action,
        int Direction,
        int Profiles,
        string? Grouping,
        string? RemoteAddresses);

    internal static (int CurrentProfileTypes, IReadOnlyList<FirewallRuleSnapshot> Rules) GetRuleSnapshots()
    {
        object? policy2Obj = null;
        object? rulesObj = null;
        try
        {
            var policy2Type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (policy2Type == null)
            {
                throw new InvalidOperationException("Windows Firewall COM API (HNetCfg.FwPolicy2) is not available.");
            }

            dynamic policy2 = Activator.CreateInstance(policy2Type) ?? throw new InvalidOperationException("Failed to create HNetCfg.FwPolicy2 COM object.");
            policy2Obj = policy2;

            int currentProfiles = (int)policy2.CurrentProfileTypes;
            dynamic rules = policy2.Rules;
            rulesObj = rules;

            var snapshots = new List<FirewallRuleSnapshot>();

            foreach (var ruleObj in rules)
            {
                if (ruleObj == null) continue;
                try
                {
                    dynamic rule = ruleObj;
                    var name = (string?)rule.Name ?? string.Empty;
                    var enabled = (bool)rule.Enabled;
                    var action = (int)rule.Action;
                    var direction = (int)rule.Direction;
                    var profiles = (int)rule.Profiles;
                    var grouping = (string?)rule.Grouping;
                    var remoteAddresses = (string?)rule.RemoteAddresses;

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        snapshots.Add(new FirewallRuleSnapshot(
                            name.Trim(),
                            enabled,
                            action,
                            direction,
                            profiles,
                            string.IsNullOrWhiteSpace(grouping) ? null : grouping.Trim(),
                            string.IsNullOrWhiteSpace(remoteAddresses) ? null : remoteAddresses.Trim()));
                    }
                }
                finally
                {
                    if (Marshal.IsComObject(ruleObj))
                    {
                        Marshal.FinalReleaseComObject(ruleObj);
                    }
                }
            }

            return (currentProfiles, snapshots);
        }
        finally
        {
            if (rulesObj != null && Marshal.IsComObject(rulesObj))
            {
                Marshal.FinalReleaseComObject(rulesObj);
            }

            if (policy2Obj != null && Marshal.IsComObject(policy2Obj))
            {
                Marshal.FinalReleaseComObject(policy2Obj);
            }
        }
    }

    internal static bool RuleExists(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return false;

        object? policy2Obj = null;
        object? rulesObj = null;
        object? ruleObj = null;
        try
        {
            var policy2Type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (policy2Type == null) return false;

            dynamic policy2 = Activator.CreateInstance(policy2Type);
            policy2Obj = policy2;
            dynamic rules = policy2.Rules;
            rulesObj = rules;

            try
            {
                ruleObj = rules.Item(displayName);
                return ruleObj != null;
            }
            catch
            {
                return false;
            }
        }
        finally
        {
            if (ruleObj != null && Marshal.IsComObject(ruleObj))
            {
                Marshal.FinalReleaseComObject(ruleObj);
            }

            if (rulesObj != null && Marshal.IsComObject(rulesObj))
            {
                Marshal.FinalReleaseComObject(rulesObj);
            }

            if (policy2Obj != null && Marshal.IsComObject(policy2Obj))
            {
                Marshal.FinalReleaseComObject(policy2Obj);
            }
        }
    }

    internal static bool TryCreateRule(FirewallRuleTarget target, out string? error)
    {
        error = null;

        object? policy2Obj = null;
        object? rulesObj = null;
        object? ruleObj = null;
        try
        {
            var policy2Type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            var ruleType = Type.GetTypeFromProgID("HNetCfg.FWRule");
            if (policy2Type == null || ruleType == null)
            {
                error = "Windows Firewall COM API is not available.";
                return false;
            }

            dynamic policy2 = Activator.CreateInstance(policy2Type);
            policy2Obj = policy2;

            dynamic rules = policy2.Rules;
            rulesObj = rules;

            dynamic rule = Activator.CreateInstance(ruleType);
            ruleObj = rule;

            rule.Name = target.DisplayName;
            rule.Description = target.Description ?? $"Privacy Hardening Framework policy {target.DisplayName}";
            rule.Grouping = target.Group;
            rule.Enabled = target.Enabled;
            rule.Profiles = NetFwProfile2All;

            rule.Direction = target.Direction.Equals("Inbound", StringComparison.OrdinalIgnoreCase)
                ? NetFwDirectionIn
                : NetFwDirectionOut;

            rule.Action = target.Action.Equals("Allow", StringComparison.OrdinalIgnoreCase)
                ? NetFwActionAllow
                : NetFwActionBlock;

            rule.Protocol = ParseProtocol(target.Protocol);
            rule.RemoteAddresses = target.RemoteAddress;

            rules.Add(rule);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            if (ruleObj != null && Marshal.IsComObject(ruleObj))
            {
                Marshal.FinalReleaseComObject(ruleObj);
            }

            if (rulesObj != null && Marshal.IsComObject(rulesObj))
            {
                Marshal.FinalReleaseComObject(rulesObj);
            }

            if (policy2Obj != null && Marshal.IsComObject(policy2Obj))
            {
                Marshal.FinalReleaseComObject(policy2Obj);
            }
        }
    }

    internal static bool TryRemoveRule(string displayName, string? requiredGrouping, out bool removed, out string? error)
    {
        removed = false;
        error = null;

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return true;
        }

        object? policy2Obj = null;
        object? rulesObj = null;
        object? ruleObj = null;
        try
        {
            var policy2Type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (policy2Type == null)
            {
                error = "Windows Firewall COM API (HNetCfg.FwPolicy2) is not available.";
                return false;
            }

            dynamic policy2 = Activator.CreateInstance(policy2Type);
            policy2Obj = policy2;
            dynamic rules = policy2.Rules;
            rulesObj = rules;

            try
            {
                ruleObj = rules.Item(displayName);
            }
            catch
            {
                return true;
            }

            if (ruleObj == null)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(requiredGrouping))
            {
                try
                {
                    dynamic rule = ruleObj;
                    var grouping = (string?)rule.Grouping;
                    if (!string.Equals(grouping, requiredGrouping, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                    return true;
                }
            }

            rules.Remove(displayName);
            removed = true;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            if (ruleObj != null && Marshal.IsComObject(ruleObj))
            {
                Marshal.FinalReleaseComObject(ruleObj);
            }

            if (rulesObj != null && Marshal.IsComObject(rulesObj))
            {
                Marshal.FinalReleaseComObject(rulesObj);
            }

            if (policy2Obj != null && Marshal.IsComObject(policy2Obj))
            {
                Marshal.FinalReleaseComObject(policy2Obj);
            }
        }
    }

    internal static bool IsRuleEffectiveForCurrentProfile(int ruleProfiles, int currentProfiles)
    {
        if (ruleProfiles == 0 || ruleProfiles == NetFwProfile2All)
        {
            return true;
        }

        return (ruleProfiles & currentProfiles) != 0;
    }

    internal static Func<string, bool> CreateDisplayNameMatcher(string wildcardPattern)
    {
        if (string.IsNullOrWhiteSpace(wildcardPattern))
        {
            return _ => false;
        }

        // Convert simple wildcards (*, ?) into regex.
        var regexPattern = "^" + Regex.Escape(wildcardPattern.Trim())
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        var regex = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return input => input != null && regex.IsMatch(input);
    }

    private static int ParseProtocol(string protocol)
    {
        if (string.IsNullOrWhiteSpace(protocol)) return NetFwIpProtocolAny;

        var normalized = protocol.Trim();
        if (normalized.Equals("Any", StringComparison.OrdinalIgnoreCase))
        {
            return NetFwIpProtocolAny;
        }

        if (normalized.Equals("TCP", StringComparison.OrdinalIgnoreCase))
        {
            return 6;
        }

        if (normalized.Equals("UDP", StringComparison.OrdinalIgnoreCase))
        {
            return 17;
        }

        if (normalized.Equals("ICMPv4", StringComparison.OrdinalIgnoreCase) || normalized.Equals("ICMP", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (normalized.Equals("ICMPv6", StringComparison.OrdinalIgnoreCase))
        {
            return 58;
        }

        return NetFwIpProtocolAny;
    }
}

