# Architecture Overview

## Design Principles

1. **Privilege Separation**: UI and service run in different security contexts
2. **Defense in Depth**: Multiple enforcement layers (registry, service, firewall)
3. **Least Privilege**: Minimum necessary permissions for each component
4. **Audit First**: Always inspect before applying
5. **Reversibility**: Every change must be undoable
6. **Transparency**: All actions logged and explainable

## Component Architecture

### 1. WinUI 3 UI Application

**Context**: Standard User
**Purpose**: User interface for policy selection and system inspection
**Key Features**:
- Individual policy selection with checkboxes
- Category filtering and search
- Risk level indicators
- Detailed policy information on expand
- Bulk selection actions
- Audit results visualization
- Diff view (current vs recommended state)

**Security Constraints**:
- Cannot directly modify system settings
- All operations go through IPC to service
- Input validation before sending commands

### 2. Windows Service

**Context**: LocalSystem
**Purpose**: Privileged policy execution engine
**Key Components**:

#### IPC Server
- Named pipe communication
- SDDL-restricted access (Administrators + SYSTEM only)
- Protocol versioning
- Command schema validation
- Caller identity verification

#### Policy Engine
- **Policy Loader**: Reads YAML definitions
- **Compatibility Checker**: Filters by Windows build/SKU
- **Dependency Resolver**: Topological sort for application order
- **Executor Factory**: Routes to appropriate mechanism executor

#### Executors
- **Registry Executor**: Direct registry manipulation
- **Service Executor**: Service configuration (startup type, state)
- **Task Executor**: Scheduled task disable/enable
- **Firewall Executor**: Windows Firewall rule creation
- **PowerShell Executor**: Signed script execution (constrained mode)

#### State Manager
- **Change Log**: Persistent record of all changes (SQLite/JSON)
- **Snapshot Manager**: Point-in-time system state capture
- **Drift Detector**: Compares current state to last applied
- **Restore Point Manager**: Windows System Restore integration

### 3. CLI Tool

**Context**: Administrator (UAC-elevated)
**Purpose**: Safe mode recovery and troubleshooting
**Features**:
- Works in Safe Mode (service may not be running)
- Emergency revert-all function
- Connection testing
- Audit reporting

### 4. Policy Definitions (YAML)

**Location**: `%ProgramData%\PrivacyHardeningFramework\policies\`
**Format**: YAML with strict schema
**Security**: Checksummed in manifest, optionally signed
**Versioning**: Semantic versioning per policy

## Data Flow

### Policy Application Flow

```
User selects policies in UI
  ↓
UI sends ApplyCommand via named pipe
  ↓
Service validates caller + command
  ↓
Policy Engine loads definitions
  ↓
Compatibility Checker filters applicable
  ↓
Dependency Resolver determines order
  ↓
For each policy:
  ├→ Get executor for mechanism type
  ├→ Capture current state
  ├→ Execute change (Registry/Service/Firewall/etc.)
  ├→ Verify change applied
  └→ Log to change log
  ↓
Create snapshot
  ↓
Return ApplyResult to UI
  ↓
UI displays results to user
```

### Audit Flow

```
User clicks "Run Audit"
  ↓
UI sends AuditCommand
  ↓
Service loads all policies
  ↓
For each policy:
  ├→ Check if applicable
  ├→ Get current system value
  ├→ Compare to expected value
  └→ Generate PolicyAuditItem
  ↓
Return AuditResult with all items
  ↓
UI displays current vs expected state
```

### Revert Flow

```
User requests revert (UI or CLI)
  ↓
Service retrieves change log for policies
  ↓
For each change (reverse order):
  ├→ Get executor
  ├→ Call RevertAsync with original change record
  ├→ Restore previous state
  └→ Log revert action
  ↓
Return RevertResult
```

## Security Boundaries

### Trust Boundaries

| Boundary | Trust Level | Validation |
|----------|-------------|------------|
| UI → Service IPC | Semi-trusted | Caller SID check, Administrator group membership |
| Policy Files → Service | Untrusted | Checksum verification, schema validation |
| PowerShell Scripts | Untrusted | Authenticode signature, constrained language mode |

### Attack Surface Mitigation

1. **IPC Hijacking**: Named pipe SDDL restricts to Admin+SYSTEM
2. **Policy Tampering**: Manifest checksums detect modification
3. **Privilege Escalation**: UI cannot directly invoke executors
4. **Command Injection**: Strict schema validation, no dynamic code
5. **Script Injection**: No user input in PowerShell execution

## Extensibility Points

### Adding New Policy Mechanisms

1. Create new `MechanismType` enum value
2. Implement `IExecutor` interface
3. Register in DI container
4. Create policy YAML with new mechanism

### Adding New Policy Categories

1. Add to `PolicyCategory` enum
2. Create subdirectory in `policies/`
3. Add filter option in UI

### Custom Executors

```csharp
public class CustomExecutor : IExecutor
{
    public MechanismType MechanismType => MechanismType.Custom;

    public async Task<bool> IsAppliedAsync(PolicyDefinition policy, CancellationToken ct)
    {
        // Check if policy is currently in effect
    }

    public async Task<ChangeRecord> ApplyAsync(PolicyDefinition policy, CancellationToken ct)
    {
        // Apply the policy, return change record
    }

    public async Task<ChangeRecord> RevertAsync(PolicyDefinition policy, ChangeRecord original, CancellationToken ct)
    {
        // Restore previous state
    }
}
```

## Performance Considerations

- **Policy Loading**: Cached after first load, invalidated on manifest change
- **IPC**: Concurrent connections supported (max 4 simultaneous)
- **Executors**: Async/await throughout for responsiveness
- **UI**: Virtualized lists for large policy sets

## Future Architecture Enhancements

### Planned for v2.0

1. **WFP Callout Driver**: Kernel-mode network filtering
2. **Policy Signing**: Authenticode signatures on YAML files
3. **Automatic Updates**: Secure policy definition updates
4. **Telemetry Capture**: Optional anonymous usage stats
5. **ETW Monitoring**: Real-time detection of policy reverts
