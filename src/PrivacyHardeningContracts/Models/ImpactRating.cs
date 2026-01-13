namespace PrivacyHardeningContracts.Models;

/// <summary>
/// Detailed impact ratings for a policy across different dimensions.
/// Higher value typically means more impact (e.g., 3 = High Privacy, but also potentially 3 = High Compatibility risk).
/// </summary>
public sealed class ImpactRating
{
    /// <summary>
    /// Privacy benefit (0-3: None, Low, Medium, High)
    /// </summary>
    public int Privacy { get; init; }

    /// <summary>
    /// Performance impact (0-3: None, Negligible, Noticeable, High)
    /// </summary>
    public int Performance { get; init; }

    /// <summary>
    /// Compatibility risk / Potential for user friction (0-3: None, Low, Medium, High)
    /// </summary>
    public int Compatibility { get; init; }

    /// <summary>
    /// Security hardening value (0-3: None, Low, Medium, High)
    /// </summary>
    public int Security { get; init; }
}
