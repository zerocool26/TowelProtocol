using Microsoft.Extensions.Logging;
using PrivacyHardeningContracts.Models;
using PrivacyHardeningContracts.Responses;
using PrivacyHardeningService.PolicyEngine;
using PrivacyHardeningService.StateManager;

namespace PrivacyHardeningService.Advisor;

public sealed class RecommendationEngine
{
    private readonly ILogger<RecommendationEngine> _logger;
    private readonly SystemStateCapture _stateCapture;
    private readonly IPolicyLoader _loader;

    public RecommendationEngine(
        ILogger<RecommendationEngine> logger,
        SystemStateCapture stateCapture,
        IPolicyLoader loader)
    {
        _logger = logger;
        _stateCapture = stateCapture;
        _loader = loader;
    }

    public async Task<RecommendationResult> AnalyzeAsync(CancellationToken cancellationToken)
    {
        var recommendations = new List<RecommendationItem>();
        var systemInfo = await _stateCapture.GetSystemInfoAsync(cancellationToken);
        
        // --- 1. Security Baseline Analysis ---
        
        // Defender Tamper Protection
        if (!systemInfo.DefenderTamperProtectionEnabled)
        {
            recommendations.Add(new RecommendationItem
            {
                Id = "SEC-001",
                Title = "Enable Tamper Protection",
                Description = "Windows Defender Tamper Protection is disabled. This is a critical security feature that prevents malicious apps from changing your antivirus settings.",
                Severity = "Critical",
                RelatedPolicyIds = new[] { "def-006-realtime-monitoring" } 
            });
        }

        // Domain Join Status
        if (!systemInfo.IsDomainJoined && !systemInfo.IsMDMManaged)
        {
            // Consumer Device Recommendations
            recommendations.Add(new RecommendationItem
            {
                Id = "PRIV-001",
                Title = "Apply Consumer Privacy Baseline",
                Description = "Your device is not managed by an organization. You should apply the 'Standard Privacy' profile to reduce data collection.",
                Severity = "Info",
                RelatedPolicyIds = null // Suggest profile instead
            });
        }
        else
        {
             recommendations.Add(new RecommendationItem
            {
                Id = "ENT-001",
                Title = "Enterprise Environment Detected",
                Description = "This device is domain-joined or MDM managed. Some policies may be overridden by your administrator. Use caution when applying GPO policies.",
                Severity = "Warning",
                RelatedPolicyIds = null
            });
        }

        // --- 2. OS Specifics ---
        if (systemInfo.WindowsSku.Contains("Home", StringComparison.OrdinalIgnoreCase))
        {
            recommendations.Add(new RecommendationItem
            {
                Id = "OS-001",
                Title = "Windows Home Detected",
                Description = "Group Policy (GPO) features are limited on Windows Home edition. Some advanced hardening policies may not apply correctly.",
                Severity = "Info",
                RelatedPolicyIds = null
            });
        }

        // --- 3. Privacy Score Calculation ---
        // Simple heuristic: Start at 100, deduct for issues.
        int score = 100;
        foreach(var rec in recommendations)
        {
            switch(rec.Severity)
            {
                case "Critical": score -= 25; break;
                case "Warning": score -= 10; break;
                case "Info": score -= 0; break;
            }
        }
        score = Math.Max(0, score);

        string grade = score switch
        {
            >= 90 => "A",
            >= 80 => "B",
            >= 70 => "C",
            >= 60 => "D",
            _     => "F"
        };

        return new RecommendationResult 
        { 
            CommandId = Guid.NewGuid().ToString(),
            Success = true,
            PrivacyScore = score,
            Grade = grade,
            Recommendations = recommendations.ToArray()
        };
    }
}
