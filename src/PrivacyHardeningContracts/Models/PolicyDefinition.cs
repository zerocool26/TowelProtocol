namespace PrivacyHardeningContracts.Models;

/// <summary>
/// Complete definition of a single privacy/hardening policy
/// Deserialized from YAML policy files
/// </summary>
public sealed class PolicyDefinition
{
    /// <summary>
    /// Unique policy identifier (e.g., "tel-001")
    /// </summary>
    public required string PolicyId { get; init; }

    /// <summary>
    /// Policy version (semantic versioning)
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Human-readable policy name
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Policy category
    /// </summary>
    public required PolicyCategory Category { get; init; }

    /// <summary>
    /// Detailed description (plain English, can be multi-line)
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Primary mechanism used to enforce this policy
    /// </summary>
    public required MechanismType Mechanism { get; init; }

    /// <summary>
    /// Mechanism-specific details (JSON object, type varies by mechanism)
    /// </summary>
    public required object MechanismDetails { get; init; }

    /// <summary>
    /// Whether this policy uses a supported mechanism
    /// </summary>
    public required SupportStatus SupportStatus { get; init; }

    /// <summary>
    /// Risk level for applying this policy
    /// </summary>
    public required RiskLevel RiskLevel { get; init; }

    /// <summary>
    /// Whether this policy can be reverted
    /// </summary>
    public required bool Reversible { get; init; }

    /// <summary>
    /// Description of how to revert (human-readable)
    /// </summary>
    public string? RevertMechanism { get; init; }

    /// <summary>
    /// Build/SKU applicability
    /// </summary>
    public required PolicyApplicability Applicability { get; init; }

    /// <summary>
    /// Policy dependencies (enhanced with user override capability)
    /// </summary>
    public PolicyDependency[] Dependencies { get; init; } = Array.Empty<PolicyDependency>();

    /// <summary>
    /// Known breakage scenarios
    /// </summary>
    public BreakageScenario[] KnownBreakage { get; init; } = Array.Empty<BreakageScenario>();

    /// <summary>
    /// Command to verify policy is applied (e.g., reg query)
    /// </summary>
    public string? VerificationCommand { get; init; }

    /// <summary>
    /// The exact Registry key or File path used as evidence for this policy's state.
    /// Used for "Evidence-Based Auditing" in the UI.
    /// </summary>
    public string? EvidencePath { get; init; }

    /// <summary>
    /// Expected output from verification command
    /// </summary>
    public string? ExpectedOutput { get; init; }

    /// <summary>
    /// Detailed impact ratings for this policy.
    /// </summary>
    public ImpactRating? Impact { get; init; }

    /// <summary>
    /// Available sub-setting options for granular configuration.
    /// </summary>
    public PolicyValueOption[]? ValueOptions { get; init; }

    /// <summary>
    /// Reference URLs (Microsoft docs, research)
    /// </summary>
    public string[] References { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Tags for filtering/searching
    /// </summary>
    public string[] Tags { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Additional notes (warnings, caveats)
    /// </summary>
    public string? Notes { get; init; }

    /// <summary>
    /// Detailed technical evidence metadata (e.g. "HKLM\...\DisableTel") used for fine-tuning.
    /// </summary>
    public string? TechnicalEvidence { get; init; }

    /// <summary>
    /// Whether this policy is enabled by default in profiles
    /// </summary>
    public bool EnabledByDefault { get; init; }

    /// <summary>
    /// Profiles that include this policy (Balanced, Hardened, MaximumPrivacy)
    /// </summary>
    public string[] IncludedInProfiles { get; init; } = Array.Empty<string>();

    // ========================================================================
    // GRANULAR CONTROL EXTENSIONS
    // ========================================================================
    // The following fields support maximum user control over policy configuration
    // Following the "User is the Ultimate Authority" principle

    /// <summary>
    /// Whether this policy should be applied automatically (MUST be false for user control)
    /// </summary>
    public bool AutoApply { get; init; } = false;

    /// <summary>
    /// Whether this policy requires explicit user confirmation before applying
    /// </summary>
    public bool RequiresConfirmation { get; init; } = true;

    /// <summary>
    /// Whether this policy should be shown in the UI for user selection
    /// </summary>
    public bool ShowInUI { get; init; } = true;

    /// <summary>
    /// For parameterized policies: allowed value options with descriptions
    /// Enables policies with multiple values beyond simple on/off
    /// Example: Diagnostic Data Level with Security/Basic/Enhanced/Full options
    /// </summary>
    public PolicyValueOption[]? AllowedValues { get; init; }

    /// <summary>
    /// For multi-parameter service policies: configurable service options
    /// Enables independent control of startup type, action, and recovery
    /// </summary>
    public ServiceConfigOptions? ServiceConfigOptions { get; init; }

    /// <summary>
    /// For multi-action task policies: configurable task options
    /// Enables choosing from multiple task actions (Disable/Delete/ModifyTriggers/Export)
    /// </summary>
    public TaskConfigOptions? TaskConfigOptions { get; init; }

    /// <summary>
    /// For per-endpoint firewall policies: endpoint details
    /// Enables individual selection of which endpoints to block
    /// </summary>
    public FirewallEndpoint? FirewallEndpoint { get; init; }

    /// <summary>
    /// Advanced options that users can toggle for policy application
    /// Examples: dry run, create restore point, force apply, logging verbosity
    /// </summary>
    public AdvancedOptions? AdvancedOptions { get; init; }

    /// <summary>
    /// Help text to assist users in understanding configuration choices
    /// </summary>
    public string? HelpText { get; init; }

    /// <summary>
    /// Current value of this policy on the system (populated at runtime)
    /// Allows user to see current state vs proposed state
    /// </summary>
    public object? CurrentValue { get; init; }

    /// <summary>
    /// Recommended value for this policy (from privacy perspective)
    /// User can choose to follow or override recommendation
    /// </summary>
    public object? RecommendedValue { get; init; }

    /// <summary>
    /// Whether user must explicitly choose a value (cannot use defaults)
    /// Enforces conscious decision-making for important settings
    /// </summary>
    public bool UserMustChoose { get; init; } = false;
}
