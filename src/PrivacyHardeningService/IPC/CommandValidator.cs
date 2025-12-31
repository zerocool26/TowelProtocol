using Microsoft.Extensions.Logging;
using PrivacyHardeningContracts.Commands;

namespace PrivacyHardeningService.IPC;

/// <summary>
/// Validates IPC command schema and protocol version
/// </summary>
public sealed class CommandValidator
{
    private readonly ILogger<CommandValidator> _logger;
    private const int CurrentProtocolVersion = 1;

    public CommandValidator(ILogger<CommandValidator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Validates command structure and version compatibility
    /// </summary>
    public bool ValidateCommand(CommandBase command)
    {
        if (command == null)
        {
            _logger.LogWarning("Received null command");
            return false;
        }

        // Check protocol version
        if (command.ProtocolVersion != CurrentProtocolVersion)
        {
            _logger.LogWarning(
                "Protocol version mismatch: expected {Expected}, got {Actual}",
                CurrentProtocolVersion,
                command.ProtocolVersion);
            return false;
        }

        // Validate command-specific requirements
        return command switch
        {
            ApplyCommand apply => ValidateApplyCommand(apply),
            RevertCommand revert => ValidateRevertCommand(revert),
            AuditCommand audit => ValidateAuditCommand(audit),
            GetPoliciesCommand => true,
            GetStateCommand => true,
            DetectDriftCommand => true,
            CreateSnapshotCommand => true,
            _ => false
        };
    }

    private bool ValidateApplyCommand(ApplyCommand cmd)
    {
        if (cmd.PolicyIds == null || cmd.PolicyIds.Length == 0)
        {
            _logger.LogWarning("ApplyCommand missing PolicyIds");
            return false;
        }

        // Validate policy ID format
        foreach (var policyId in cmd.PolicyIds)
        {
            if (string.IsNullOrWhiteSpace(policyId))
            {
                _logger.LogWarning("ApplyCommand contains invalid policy ID");
                return false;
            }
        }

        return true;
    }

    private bool ValidateRevertCommand(RevertCommand cmd)
    {
        // RevertCommand can have null PolicyIds (means revert all)
        // but must have either PolicyIds, SnapshotId, or RestorePointId
        if (cmd.PolicyIds == null &&
            cmd.SnapshotId == null &&
            cmd.RestorePointId == null)
        {
            _logger.LogWarning("RevertCommand must specify what to revert");
            return false;
        }

        return true;
    }

    private bool ValidateAuditCommand(AuditCommand cmd)
    {
        // Audit is always valid (PolicyIds are optional)
        return true;
    }
}
