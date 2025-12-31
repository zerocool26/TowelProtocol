# Windows 11 Privacy Hardening Framework

**Enterprise-grade privacy, telemetry control, and system hardening for Windows 11 Enterprise**

## Overview

This framework provides secure, auditable, and reversible control over Windows 11 telemetry, cloud data exfiltration, AI integration, and invasive UX features. Designed for enterprise environments with strict security and privacy requirements.

### Key Features

- **Individual Policy Selection**: Granular control - select exactly which policies to apply
- **Privilege Separation**: UI runs as standard user, service runs as LocalSystem
- **Audit Mode**: Inspect current system state before making changes
- **Full Reversibility**: All changes can be rolled back
- **Drift Detection**: Monitors for changes after Windows updates
- **Profiles**: Balanced, Hardened, and Maximum Privacy presets
- **Restore Points**: Automatic system restore point creation
- **Transparent**: Every change is logged and explainable

### Architecture

```
┌─────────────────────┐
│   WinUI 3 UI App    │ ← Standard user context
│  (Policy Selection) │
└──────────┬──────────┘
           │ Named Pipe IPC
           ▼
┌─────────────────────┐
│  Windows Service    │ ← LocalSystem context
│  (Policy Engine)    │
└──────────┬──────────┘
           │
           ├─→ Registry Executor
           ├─→ Service Executor
           ├─→ Firewall Executor
           ├─→ PowerShell Executor
           └─→ Task Executor
```

## Project Structure

```
PrivacyHardeningFramework/
├── src/
│   ├── PrivacyHardeningUI/          # WinUI 3 user interface
│   ├── PrivacyHardeningService/     # Windows service (LocalSystem)
│   ├── PrivacyHardeningContracts/   # Shared models/contracts
│   └── PrivacyHardeningCLI/         # CLI tool for safe mode
├── policies/                         # YAML policy definitions
│   ├── telemetry/
│   ├── ai/
│   ├── ux/
│   ├── network/
│   └── services/
├── scripts/                          # Signed PowerShell scripts
└── docs/                             # Documentation
```

## Getting Started

### Prerequisites

- Windows 11 Enterprise (22H2 or later)
- Visual Studio 2022 (with .NET 8.0 and Windows App SDK)
- Administrator rights for service installation

### Building

You can build this solution using Visual Studio or the .NET CLI. The repository targets .NET 8 and Windows-specific frameworks for the UI/service components.

Using Visual Studio

1. Open `PrivacyHardeningFramework.sln` in Visual Studio (2022 or later with .NET 8 workloads installed)
2. Restore NuGet packages
3. Build solution (Release configuration, x64 platform)

Using .NET CLI (recommended for CI and quick local builds)

```powershell
# Restore packages
dotnet restore "PrivacyHardeningFramework.sln"

# Build (Release)
dotnet build "PrivacyHardeningFramework.sln" -c Release

# Run tests (if any)
dotnet test "PrivacyHardeningFramework.sln"
```

### Installation

1. **Install the Windows Service**:
   ```powershell
   sc.exe create PrivacyHardeningService binPath="C:\Path\To\PrivacyHardeningService.exe"
   sc.exe start PrivacyHardeningService
   ```

2. **Deploy Policy Files**:
   ```powershell
   Copy-Item -Path policies\* -Destination "C:\ProgramData\PrivacyHardeningFramework\policies\" -Recurse
   ```

3. **Launch UI**:
   ```powershell
   Start-Process "C:\Path\To\PrivacyHardeningUI.exe"
   ```

## Usage

### Individual Policy Selection Panel

The main UI feature is the **Policy Selection** tab, which allows you to:

1. **Browse all available policies** organized by category (Telemetry, AI, UX, Network, Services, etc.)
2. **Filter policies** by:
   - Search text (name, description, policy ID)
   - Category
   - Applicability to your system
   - Risk level
3. **Select individual policies** using checkboxes
4. **Bulk actions**:
   - Select All / Select None
   - Select only Low Risk policies
   - Select Low + Medium Risk
5. **Expand each policy** to see:
   - Detailed description
   - Risk level and support status
   - Known breakage scenarios
   - Dependencies
   - Reversibility information
   - Mechanism (Registry, Service, Firewall, etc.)

### Workflow

1. **Load Policies**: Click "Load Policies" to fetch available policies from the service
2. **Filter & Browse**: Use filters and search to find policies of interest
3. **Review Details**: Expand individual policies to understand risks and impacts
4. **Select**: Check the policies you want to apply
5. **Run Audit** (optional): See current system state
6. **Apply**: Click "Apply Selected" to execute changes
7. **Revert** (if needed): Use CLI or UI to roll back changes

### CLI Tool

For troubleshooting or safe mode recovery:

```powershell
# Test service connection
PrivacyHardeningCLI.exe test-connection

# Run audit
PrivacyHardeningCLI.exe audit

# Emergency rollback
PrivacyHardeningCLI.exe revert-all

# List all policies
PrivacyHardeningCLI.exe list-policies
```

## Policy Definitions

Policies are defined in YAML format with complete metadata:

```yaml
policyId: "tel-001"
name: "Set Diagnostic Data to Security Level"
category: Telemetry
description: "Reduces telemetry to minimum (Enterprise only)"
mechanism: Registry
supportStatus: Supported
riskLevel: Low
reversible: true
knownBreakage:
  - scenario: "Windows Update troubleshooting may require Basic telemetry"
    severity: Low
```

### Available Categories

- **Telemetry**: Diagnostic data, activity history, connected experiences
- **AI**: Recall, Copilot, Studio Effects
- **UX**: Widgets, ads, tips, search
- **Network**: Firewall rules, DNS policies
- **Services**: Background services, scheduled tasks
- **Updates**: Driver updates, feature rollout control

### Risk Levels

- **Low**: No known breakage, fully supported
- **Medium**: May break specific features, supported mechanism
- **High**: Likely breakage or unsupported mechanism
- **Critical**: Experimental, may cause instability

## Security Model

### Privilege Separation

- **UI Application**: Runs as standard user, no direct system modification rights
- **Windows Service**: Runs as LocalSystem, performs all system changes
- **IPC**: Named pipe with SDDL restricting access to Administrators + SYSTEM

### Command Validation

The service validates:
- Caller identity (Administrator group membership)
- Command schema and protocol version
- Policy applicability to current system

### Execution Constraints

- PowerShell scripts are signed and executed in constrained mode
- No arbitrary code execution
- All changes logged to persistent change log
- Restore points created before risky operations

## Supported Policies (Sample)

### Telemetry
- `tel-001`: Set diagnostic data to Security level (Enterprise)
- `tel-002`: Disable Connected User Experiences service

### AI
- `ai-001`: Disable Windows Recall (Copilot+ PCs)
- `ai-002`: Disable Windows Copilot

### UX
- `ux-001`: Disable Widgets
- `ux-002`: Disable ads and app suggestions

### Network
- `net-001`: Block telemetry endpoints via firewall

### Services
- `svc-001`: Disable DiagTrack service

## Limitations & Warnings

### Known Limitations

1. **Cannot guarantee 100% telemetry elimination** - Undocumented channels exist
2. **Fragile against Windows updates** - Some changes may be reverted
3. **No kernel-mode enforcement (v1)** - System processes can bypass firewall rules
4. **MDM/Domain conflicts** - Domain GPO overrides local policies
5. **Defender Tamper Protection** - Some settings cannot be changed when enabled

### Unsupported Features

- Hosts file modification (avoided where possible)
- Kernel driver installation (planned for v2)
- Automatic policy updates (manual update process)

## Breakage Scenarios

### High-Impact Policies

**DiagTrack Service Disable**:
- ⚠ Breaks: Microsoft Store, Windows Defender cloud protection
- Recommendation: Use diagnostic data registry setting instead

**Telemetry Firewall Blocks**:
- ⚠ May affect: Windows Update, troubleshooters
- Recommendation: Test in non-production environment first

## Reversibility & Rollback

### Automatic Restore Points

Created before:
- First policy application
- Maximum Privacy profile application
- User request

### Manual Rollback Methods

1. **Via UI**: Revert tab → Select policies → Revert
2. **Via CLI**: `PrivacyHardeningCLI.exe revert-all`
3. **System Restore**: Control Panel → Recovery → Open System Restore
4. **Manual**: Each policy definition includes revert instructions

## Development

### Adding New Policies

1. Create YAML file in appropriate `policies/` subdirectory
2. Define all required fields (see existing policies as templates)
3. Update `policies/manifest.json`
4. Test applicability and reversibility

### Adding New Executors

1. Implement `IExecutor` interface
2. Register in `Program.cs` dependency injection
3. Add mechanism type to `MechanismType` enum
4. Implement Apply/Revert/IsApplied logic

### Testing

- Test on clean Windows 11 Enterprise VM
- Verify policies apply correctly
- Verify full reversibility
- Test after cumulative updates

## Contributing

This is an enterprise-focused security tool. Contributions must:
- Follow privilege separation model
- Include complete policy metadata
- Provide reversibility mechanisms
- Document all known breakage
- Avoid unsupported mechanisms where possible

## License

[Your License Here]

## Disclaimer

This tool modifies critical system settings. Use at your own risk. Always test in non-production environments first. The authors are not responsible for system breakage, data loss, or compliance violations resulting from misuse.

## Support

- GitHub Issues: [link]
- Documentation: `docs/` folder
- Enterprise Support: [contact]

## Roadmap

### v1.1
- Complete all executor implementations (Service, Task, Firewall)
- GPO executor (lgpo.exe wrapper)
- Full drift detection with auto-reapply
- Enhanced audit reporting

### v2.0
- WFP kernel driver for network enforcement
- Automatic policy updates with signature verification
- TPM-based attestation
- Enhanced MDM conflict detection

## Acknowledgments

- Microsoft privacy documentation
- Windows internals community
- Security research community
