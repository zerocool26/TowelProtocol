namespace PrivacyHardeningContracts.Models;

/// <summary>
/// Indicates whether a change record was produced by an apply or revert operation.
/// </summary>
public enum ChangeOperation
{
    Unknown = 0,
    Apply = 1,
    Revert = 2
}

