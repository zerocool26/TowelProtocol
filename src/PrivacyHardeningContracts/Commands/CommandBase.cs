using System.Text.Json.Serialization;

namespace PrivacyHardeningContracts.Commands;

/// <summary>
/// Base class for all IPC commands sent from UI to service
/// </summary>
[JsonDerivedType(typeof(AuditCommand), typeDiscriminator: "audit")]
[JsonDerivedType(typeof(ApplyCommand), typeDiscriminator: "apply")]
[JsonDerivedType(typeof(RevertCommand), typeDiscriminator: "revert")]
[JsonDerivedType(typeof(GetStateCommand), typeDiscriminator: "getState")]
[JsonDerivedType(typeof(GetPoliciesCommand), typeDiscriminator: "getPolicies")]
[JsonDerivedType(typeof(DetectDriftCommand), typeDiscriminator: "detectDrift")]
[JsonDerivedType(typeof(CreateSnapshotCommand), typeDiscriminator: "createSnapshot")]
public abstract class CommandBase
{
    /// <summary>
    /// Unique command ID for tracking
    /// </summary>
    public string CommandId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// IPC protocol version (for compatibility)
    /// </summary>
    public int ProtocolVersion { get; init; } = 1;

    /// <summary>
    /// Command timestamp
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Command type discriminator
    /// </summary>
    [JsonIgnore]
    public abstract string CommandType { get; }
}
