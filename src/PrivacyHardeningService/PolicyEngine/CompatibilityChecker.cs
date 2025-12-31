using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningService.PolicyEngine;

/// <summary>
/// Checks if policies are applicable to current Windows build/SKU
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CompatibilityChecker
{
    private readonly ILogger<CompatibilityChecker> _logger;
    private readonly int _currentBuild;
    private readonly string _currentSku;

    public CompatibilityChecker(ILogger<CompatibilityChecker> logger)
    {
        _logger = logger;
        _currentBuild = Environment.OSVersion.Version.Build;
        _currentSku = GetWindowsSku();
    }

    public bool IsApplicable(PolicyDefinition policy)
    {
        var app = policy.Applicability;

        // Check build range
        if (app.MinBuild.HasValue && _currentBuild < app.MinBuild.Value)
        {
            _logger.LogDebug("Policy {PolicyId} requires min build {MinBuild}, current: {CurrentBuild}",
                policy.PolicyId, app.MinBuild, _currentBuild);
            return false;
        }

        if (app.MaxBuild.HasValue && _currentBuild > app.MaxBuild.Value)
        {
            _logger.LogDebug("Policy {PolicyId} max build {MaxBuild}, current: {CurrentBuild}",
                policy.PolicyId, app.MaxBuild, _currentBuild);
            return false;
        }

        // Check SKU
        if (app.ExcludedSkus.Contains(_currentSku, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Policy {PolicyId} excluded for SKU: {Sku}", policy.PolicyId, _currentSku);
            return false;
        }

        if (app.SupportedSkus.Length > 0 &&
            !app.SupportedSkus.Contains(_currentSku, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Policy {PolicyId} not supported for SKU: {Sku}", policy.PolicyId, _currentSku);
            return false;
        }

        return true;
    }

    private string GetWindowsSku()
    {
        // TODO: Implement proper SKU detection via WMI or registry
        // For now, return placeholder
        return "Enterprise";
    }
}
