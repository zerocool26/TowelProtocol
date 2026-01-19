using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningContracts.Responses;

/// <summary>
/// Result of the recommendation analysis
/// </summary>
public sealed class RecommendationResult : ResponseBase
{
    /// <summary>
    /// Overall privacy score (0-100)
    /// </summary>
    public required int PrivacyScore { get; init; }

    /// <summary>
    /// Calculated letter grade (A, B, C, D, F)
    /// </summary>
    public required string Grade { get; init; }

    /// <summary>
    /// List of specific actionable recommendations
    /// </summary>
    public required RecommendationItem[] Recommendations { get; init; }
}

public sealed class RecommendationItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    
    /// <summary>
    /// "Critical", "Warning", "Info"
    /// </summary>
    public required string Severity { get; init; }

    /// <summary>
    /// Associated Policy IDs (if any) to fix this
    /// </summary>
    public string[]? RelatedPolicyIds { get; init; }
}
