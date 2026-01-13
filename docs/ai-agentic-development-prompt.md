# Advanced AI Agentic Development Prompt - Privacy Hardening Framework

## Executive Summary

This document provides comprehensive architectural guidance for AI-assisted development of the Privacy Hardening Framework - a production-grade, enterprise Windows system configuration management platform. This is NOT a surface-level application. This is a security-critical, privilege-separated, multi-process distributed system requiring professional software engineering rigor.

---

## 1. ARCHITECTURAL FOUNDATION

### System Classification
- **Type**: Distributed multi-process Windows desktop application
- **Architecture Pattern**: Layered architecture with process boundary isolation
- **Security Model**: Least privilege with elevation boundaries
- **IPC Mechanism**: Named pipes with RBAC and Authenticode verification
- **UI Framework**: Avalonia 11.3 (cross-platform XAML-based MVVM)
- **Service Runtime**: .NET 8 Worker Service (Windows Service)
- **State Persistence**: SQLite with transactional integrity
- **Domain Model**: Policy-driven declarative configuration with dependency graphs

### Process Topology

```
┌─────────────────────────────────────────────────┐
│  PrivacyHardeningUI.exe (User Process)          │
│  - Avalonia desktop application                 │
│  - MVVM with CommunityToolkit.Mvvm              │
│  - Named pipe client (IPC)                      │
│  - Standalone fallback capability              │
│  - Runs with user privileges (non-elevated)    │
└───────────────┬─────────────────────────────────┘
                │ Named Pipe: "PrivacyHardeningService_v1"
                │ JSON-serialized commands/responses
                │ Process boundary security enforcement
                ▼
┌─────────────────────────────────────────────────┐
│  PrivacyHardeningService.exe (System Service)   │
│  - Windows Service (LocalSystem account)        │
│  - Named pipe server (4 concurrent connections) │
│  - Policy engine orchestrator                   │
│  - Executor factory (strategy pattern)          │
│  - SQLite state persistence                     │
│  - Caller validation with impersonation         │
└───────────────┬─────────────────────────────────┘
                │ Strategy pattern dispatch
                ▼
┌─────────────────────────────────────────────────┐
│  IExecutor Implementations                      │
│  - RegistryExecutor (direct Win32 registry)     │
│  - ServiceExecutor (ServiceController API)      │
│  - TaskExecutor (Task Scheduler COM)            │
│  - FirewallExecutor (INetFwPolicy2 COM)         │
│  - PowerShellExecutor (signed script execution) │
└─────────────────────────────────────────────────┘

Auxiliary Processes:
┌─────────────────────────────────────────────────┐
│  PrivacyHardeningElevated.exe                   │
│  - Elevation helper (spawned with runas verb)   │
│  - Relays commands when UI lacks admin rights   │
│  - UAC prompt required                          │
└─────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────┐
│  PrivacyHardeningCLI.exe                        │
│  - Console troubleshooting/recovery tool        │
│  - Named pipe client                            │
│  - Safe-mode operations                         │
└─────────────────────────────────────────────────┘
```

---

## 2. CORE DOMAIN MODEL & BUSINESS LOGIC

### Policy Definition Schema

A **PolicyDefinition** is the fundamental domain entity representing a single configurable system setting. Key invariants:

**Mandatory Constraints:**
- `AutoApply` MUST be `false` (user consent required - non-negotiable security principle)
- `PolicyId` MUST be unique across entire policy corpus
- `Mechanism` MUST map to a registered `IExecutor` implementation
- `Reversible` flag determines whether policy supports rollback operations
- `Applicability` constraints must be verified before application (OS build, SKU)

**Dependency Graph:**
- Policies can declare dependencies via `Dependencies[]` array
- Dependency types: `Required`, `Prerequisite`, `Recommended`, `Conflict`
- Circular dependencies are INVALID and detected at runtime (should be compile-time validated in future)
- Topological sort via DFS ensures application order respects dependency DAG

**Parameterization:**
- Policies support `AllowedValues` for user-selectable options
- `ServiceConfigOptions` for multi-parameter service control (startup type + stop action)
- `TaskConfigOptions` for complex task scheduler operations (triggers, actions, conditions)
- `FirewallEndpoint` for per-IP/port firewall rule configuration

**Risk Classification:**
- `RiskLevel`: Low, Medium, High (UI warning indicators)
- `KnownBreakage`: Array of documented side effects with severity + workarounds
- `RequiresConfirmation`: Forces explicit user acknowledgment (recommended `true`)

### Change Tracking & Audit Trail

**ChangeRecord** captures every state mutation:
- `ChangeId`: UUID for unique identification
- `Operation`: Apply | Revert | Unknown
- `PreviousState`: Serialized snapshot BEFORE change (enables rollback)
- `NewState`: Serialized snapshot AFTER change
- `Success`: Boolean outcome (failed changes still recorded)
- `SnapshotId`: Groups logically related changes (e.g., single Apply batch)

**SQLite Persistence:**
- `changes` table: All modifications with full audit metadata
- `snapshots` table: System state baselines (OS version, domain join status, MDM enrollment)
- `snapshot_policies` table: Per-policy state at snapshot creation time
- Transactional inserts ensure atomicity (all-or-nothing semantics)

---

## 3. SERVICE LAYER ARCHITECTURE

### Dependency Injection Container

**Critical Services (Singleton Scope):**
- `PolicyLoader`: YAML deserialization from disk, cached in-memory
- `PolicyValidator`: Schema validation, constraint checking
- `CompatibilityChecker`: OS applicability filtering (build number, SKU)
- `DependencyResolver`: Topological sort for dependency-aware application
- `PolicyEngineCore`: Main orchestrator - coordinates all policy operations
- `ChangeLog`: SQLite persistence layer with semaphore-based concurrency control
- `IPCServer`: Named pipe listener with multi-threaded connection handling
- `CommandValidator`: JSON schema validation for IPC commands
- `CallerValidator`: Security authorization (admin check, integrity level, binary signing)

**Executor Registration (Multi-Registration Pattern):**
```csharp
services.AddSingleton<IExecutor, RegistryExecutor>();
services.AddSingleton<IExecutor, ServiceExecutor>();
services.AddSingleton<IExecutor, TaskExecutor>();
services.AddSingleton<IExecutor, FirewallExecutor>();
services.AddSingleton<IExecutor, PowerShellExecutor>();
```
- `ExecutorFactory` resolves via `MechanismType` enum → `IExecutor` lookup
- Strategy pattern enables polymorphic dispatch at runtime

### Policy Engine Orchestration Flow

**ApplyInternalAsync() - The Core Business Logic:**

1. **Pre-Application Phase:**
   - Load policies from cache (or disk on first call)
   - Resolve dependencies via topological sort (DFS)
   - Create Windows Restore Point (if `CreateRestorePoint: true`)
   - Generate snapshot ID, persist system state baseline

2. **Validation Phase:**
   - Check `CompatibilityChecker.IsApplicable()` per policy
   - Reject non-applicable policies (log + skip)
   - Verify user confirmation for `RequiresConfirmation` policies

3. **Execution Phase (Per Policy):**
   - Get executor: `ExecutorFactory.GetExecutor(policy.Mechanism)`
   - Call `executor.ApplyAsync(policy, cancellationToken)`
   - Capture `ChangeRecord` with before/after state
   - Emit progress callback: `progressCallback(percent, message)`
   - On error: Check `ContinueOnError` flag
     - `true`: Log failure, continue to next policy
     - `false`: Abort entire operation, rollback pending

4. **Post-Application Phase:**
   - Save all `ChangeRecord[]` to SQLite (transactional insert)
   - Return `ApplyResult`:
     - `Success`: `failedPolicies.Count == 0`
     - `AppliedPolicies[]`, `FailedPolicies[]`
     - `Changes[]`: Full audit trail
     - `RestorePointId`, `SnapshotId` for future revert

**RevertAsync() - Rollback Logic:**
- Query `ChangeLog` for most recent successful Apply operations
- Filter by `PolicyIds[]` (or all if null)
- For each change: Call `executor.RevertAsync(policy, originalChange)`
- Restore `PreviousState` from change record
- Create reverse `ChangeRecord` with `Operation.Revert`
- Persist revert changes to SQLite

---

## 4. IPC COMMUNICATION & SECURITY

### Named Pipe Security Model

**DACL Configuration:**
```
Allow: CreatorOwner (FullControl)       → Service can create multiple instances
Allow: LocalSystem (FullControl)        → SYSTEM account access
Allow: BuiltinAdministrators (FullControl) → Admin users
Allow: Interactive SID (Read|Write|Sync) → Logged-in user sessions
Deny: Anonymous SID (FullControl)       → Reject anonymous connections
Deny: Network SID (FullControl)         → Local-only, no remote access
SetAccessRuleProtection(true)           → Explicit ACL, no inheritance
```

**Caller Validation (Multi-Layer Security):**

1. **Impersonation Context:**
   - `RunAsClient()` impersonation on pipe connection
   - Extract `WindowsIdentity.GetCurrent()` to get caller SID/username

2. **Privilege Level Verification:**
   - Read-only commands (Audit, GetPolicies, GetState): Any authenticated user
   - Privileged commands (Apply, Revert, CreateSnapshot): Admin + High/System integrity
   - Check via `WindowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator)`

3. **Integrity Level Check:**
   - Extract token integrity level via `GetTokenInformation()`
   - Parse integrity SID (e.g., `S-1-16-12288` = High, `S-1-16-16384` = System)
   - Reject Medium integrity or lower for privileged operations

4. **Binary Signing Verification:**
   - Get client PID: `GetNamedPipeClientProcessId()`
   - Resolve process main module path
   - Call `WinVerifyTrust()` Windows API to check Authenticode signature
   - Reject unsigned, revoked, or untrusted certificates

**Fail-Closed Policy:** ANY validation failure → Connection rejected with error response

### Command/Response Pattern

**Polymorphic Command Serialization:**
```json
{
  "commandType": "Apply",
  "commandId": "550e8400-e29b-41d4-a716-446655440000",
  "protocolVersion": 1,
  "timestamp": "2026-01-11T10:30:00Z",
  "policyIds": ["tel-001", "tel-002"],
  "createRestorePoint": true,
  "dryRun": false,
  "continueOnError": false
}
```
- `commandType` is JSON discriminator for deserialization
- Base class: `CommandBase` (abstract)
- Derived: `ApplyCommand`, `RevertCommand`, `AuditCommand`, etc.

**Response Streaming (Progress Reporting):**
- For `ApplyCommand`: Service emits `ProgressResponse` JSON lines during execution
- Final response: `ApplyResult` with full outcome
- UI reads via `StreamReader.ReadLineAsync()` loop
- Progress detected by presence of `"Percent"` field in JSON

**Error Handling:**
- All exceptions wrapped in `ResponseBase.Errors[]` array
- `ErrorInfo` contains: `Code`, `Message`, `Details` (stack trace in debug)
- Common codes: `ValidationFailed`, `Unauthorized`, `UnsupportedMechanism`, `ExecutionFailed`

---

## 5. EXECUTOR IMPLEMENTATIONS (STRATEGY PATTERN)

### IExecutor Interface Contract

```csharp
public interface IExecutor
{
    MechanismType MechanismType { get; }
    Task<bool> IsAppliedAsync(PolicyDefinition policy, CancellationToken ct);
    Task<string?> GetCurrentValueAsync(PolicyDefinition policy, CancellationToken ct);
    Task<ChangeRecord> ApplyAsync(PolicyDefinition policy, CancellationToken ct);
    Task<ChangeRecord> RevertAsync(PolicyDefinition policy, ChangeRecord original, CancellationToken ct);
}
```

**Invariants:**
- `ApplyAsync()` MUST capture `PreviousState` before modification
- `RevertAsync()` MUST use `original.PreviousState` for restoration
- All methods MUST respect `CancellationToken` for cooperative cancellation
- Failed operations return `ChangeRecord { Success=false, ErrorMessage }`

### RegistryExecutor

**Mechanism:** Direct Win32 registry manipulation via `Microsoft.Win32.Registry` API

**Key Parsing:**
- `ParseKeyPath()`: Split on first backslash → hive + subkey
- Supported hives: `HKLM`, `HKCU`, `HKCR`, `HKU`, `HKCC`
- Reject traversal sequences (`..`, absolute paths outside hive)

**Value Type Handling:**
- `DWord` (REG_DWORD): 32-bit integer, hex format `0xXXXXXXXX`
- `QWord` (REG_QWORD): 64-bit integer
- `String` (REG_SZ): Null-terminated string
- `ExpandString` (REG_EXPAND_SZ): Environment variable expansion (`%SystemRoot%`)
- `MultiString` (REG_MULTI_SZ): Array of strings (null-delimited)
- `Binary` (REG_BINARY): Byte array (hex encoded)

**Apply Logic:**
1. Capture current value via `key.GetValue(valueName)`
2. Serialize to hex string for `PreviousState`
3. Create subkey if not exists: `hive.CreateSubKey(subKey)`
4. Convert `ValueData` to appropriate .NET type
5. Call `key.SetValue(valueName, value, valueKind)`
6. Return `ChangeRecord` with before/after snapshots

**Revert Logic:**
- If `PreviousState == null`: Delete value via `key.DeleteValue(valueName)`
- Else: Parse hex string, restore original value with original type
- Handle type conversions carefully (hex → int/long/byte[])

### PowerShellExecutor

**Mechanism:** External PowerShell script execution with signature verification

**Security Hardening:**
- **Path Traversal Prevention:** `IsUnderRoot()` check ensures script within `policies/scripts/` directory
- **Parameter Sanitization:** `IsSafePowerShellParameterName()` whitelist (alphanumeric + underscore only)
- **Signature Verification:** `Get-AuthenticodeSignature` PowerShell cmdlet, require `Status == "Valid"`
- **Timeout Enforcement:** 5-minute execution limit, `Process.Kill(entireProcessTree: true)` on timeout

**Execution Flow:**
1. Resolve script path (reject `..` sequences)
2. Verify Authenticode signature (if `RequiresSignature: true`)
3. Build `ProcessStartInfo`:
   - `powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File {script}`
   - Redirect stdout/stderr
   - `UseShellExecute = false` (security)
   - `CreateNoWindow = true`
4. Add parameters via `-ParameterName ParameterValue`
5. Start process, capture output
6. Wait for exit with timeout
7. Return `ChangeRecord` with stdout/stderr in `Description`

**Revert Handling:**
- If `RevertScriptPath` provided: Execute revert script with `-PreviousState` parameter
- Else: Warn "Cannot revert without revert script"
- Revert scripts must be idempotent (safe to run multiple times)

**Audit Pattern Matching (Brittle - Known Technical Debt):**
- Pattern 1: `Get-Service svc1,svc2 | Where-Object { $_.Status -eq 'Stopped' }`
  - Parse service names via regex: `Get-Service\s+([\w,-]+)`
  - Audit each service via `ServiceController` API
- Pattern 2: `Get-NetFirewallRule -DisplayName 'pattern'`
  - Use `INetFwPolicy2` COM to count matching rules
- **Recommendation:** Replace with structured `VerificationCommand` DSL or direct API calls

### ServiceExecutor

**Mechanism:** Windows Service control via `System.ServiceProcess.ServiceController`

**Startup Type Mapping (Registry-Based):**
- Registry: `HKLM\SYSTEM\CurrentControlSet\Services\{ServiceName}\Start`
- Values: `0=Boot`, `1=System`, `2=Automatic`, `3=Manual`, `4=Disabled`

**Apply Logic:**
1. Capture current startup type from registry
2. Capture current status via `ServiceController.Status` (Running/Stopped)
3. Modify registry `Start` value
4. If `StopService: true`: Call `ServiceController.Stop()`
5. Return `ChangeRecord` with previous state

**Revert Logic:**
- Restore previous startup type via registry
- If previously running: Call `ServiceController.Start()`

**Edge Cases:**
- Service may have dependent services (check `DependentServices` property)
- Stop operation may timeout (handle `TimeoutException`)

### TaskExecutor

**Mechanism:** Windows Task Scheduler via COM (`TaskScheduler` library)

**Supported Actions:**
- `disable`: Set `task.Definition.Settings.Enabled = false`
- `delete`: Remove task via `taskFolder.DeleteTask(taskName)`
- `modify-triggers`: Adjust trigger conditions (time, event log, etc.)
- `export`: Save task XML to backup location

**Apply Logic:**
1. Connect to Task Scheduler COM server
2. Get task folder + task definition
3. Execute action based on `TaskDetails.Action`
4. Register modified task (for modify operations)
5. Return `ChangeRecord` with task XML snapshot

**Revert Logic:**
- Re-enable disabled tasks
- Restore deleted tasks from XML backup (if retained)
- Restore original trigger configuration

### FirewallExecutor

**Mechanism:** Windows Firewall via COM (`INetFwPolicy2` interface)

**Apply Logic:**
1. Parse `FirewallMechanismDetails` (rule name, action, direction, remote address/port)
2. Create `INetFwRule` COM object
3. Configure:
   - `Name`, `Description`
   - `Action`: Block | Allow
   - `Direction`: Inbound | Outbound
   - `RemoteAddresses`, `RemotePorts`
   - `Protocol`: TCP | UDP | Any
   - `Enabled` per profile (Domain/Private/Public)
4. Add to `INetFwPolicy2.Rules` collection
5. Broadcast `WM_SETTINGCHANGE` to notify firewall service
6. Return `ChangeRecord` with rule names

**Revert Logic:**
- Remove previously added rules by name
- Restore per-profile enablement state

---

## 6. UI ARCHITECTURE (AVALONIA MVVM)

### Dependency Injection Container (UI Process)

**Services (Singleton Scope):**
- `ServiceClient`: IPC client, named pipe connection manager, standalone fallback
- `SettingsService`: User preferences (theme, window position, last selected profile)
- `NavigationService`: Event-driven cross-ViewModel navigation (publish/subscribe)
- `IThemeService`: Light/Dark mode, system dark mode detection, persistence
- `IAccessibilityService`: WCAG 2.1 Level AA compliance (keyboard nav, screen reader support)
- `ITelemetryMonitorService`: Real-time system telemetry flow monitoring

**ViewModels (Singleton Scope for State Persistence):**
- `MainViewModel`: Navigation hub, apply command orchestration, progress observables
- `StatusRailViewModel`: Real-time system state indicators (service status, pending changes)
- `PolicySelectionViewModel`: Policy tree with checkboxes, filter/search, profile selection
- `AuditViewModel`: Audit execution, compliance status display (applied/not applied/N/A)
- `PreviewViewModel`: Dry-run simulation, impact visualization
- `ApplyViewModel`: Pre-apply validation, post-apply summary
- `HistoryViewModel`: Change replay from SQLite, timeline visualization
- `DriftViewModel`: Baseline comparison, drift alerts
- `ReportsViewModel`: Compliance report generation (PDF/CSV export)
- `DiffViewModel`: Before/after state diff rendering

**Views (Transient Scope):**
- Views are data-bound to ViewModels via Avalonia binding syntax
- ViewModels persist across navigation (state retention)
- Views recreated on navigation (lightweight XAML instantiation)

### MVVM Binding with CommunityToolkit.Mvvm

**Observable Properties (Auto-Generated PropertyChanged):**
```csharp
[ObservableProperty]
private bool _isProcessing;  // Generates IsProcessing property + PropertyChanged event

[ObservableProperty]
private int _progressValue;
```
- Source generators create boilerplate code at compile-time
- UI binds: `<ProgressBar Value="{Binding ProgressValue}" />`

**Relay Commands (Auto-Generated ICommand):**
```csharp
[RelayCommand]
private async Task ApplySelectedPoliciesAsync()
{
    // Implementation
}
// Generates: ApplySelectedPoliciesCommand (ICommand)
// UI binds: <Button Command="{Binding ApplySelectedPoliciesCommand}" />
```

**Can-Execute Logic:**
```csharp
[RelayCommand(CanExecute = nameof(CanApplyPolicies))]
private async Task ApplySelectedPoliciesAsync() { }

private bool CanApplyPolicies() => SelectedPolicies.Any() && !IsProcessing;
```
- Automatically calls `CanExecuteChanged` when `IsProcessing` or `SelectedPolicies` changes

### Progress Reporting Architecture

**UI Binding Flow:**
```
ServiceClient.ProgressReceived event (int percent, string message)
    ↓
MainViewModel.OnProgressReceived(percent, message)
    ↓
ProgressValue = percent         [ObservableProperty]
StatusMessage = message         [ObservableProperty]
    ↓
Avalonia Binding Engine (PropertyChanged notifications)
    ↓
<ProgressBar Value="{Binding ProgressValue}" />
<TextBlock Text="{Binding StatusMessage}" />
```

**Named Pipe Streaming:**
- Service writes JSON lines during `ApplyAsync()`:
  ```json
  {"Percent": 25, "Message": "Applying tel-001", "CommandId": "..."}
  ```
- UI reads via `StreamReader.ReadLineAsync()` loop
- Detects progress by presence of `"Percent"` field
- Final response: `ApplyResult` (no `Percent` field)

### Elevation Handling

**Scenario:** User runs UI without admin rights, attempts Apply operation

**Flow:**
1. UI sends `ApplyCommand` to service via named pipe
2. Service rejects with `UnauthorizedAccessException` (caller validation fails)
3. UI catches exception in `ApplyConfirmedAsync()`
4. UI launches `PrivacyHardeningElevated.exe` with `Verb="runas"`
5. Windows UAC dialog prompts user for admin password
6. User approves → Helper runs with admin token
7. Helper connects to service via named pipe (now authorized)
8. Helper sends `ApplyCommand` to service
9. Service executes (running as SYSTEM, already privileged)
10. Helper waits for result, writes to stdout
11. UI reads helper stdout, displays result

**ProcessStartInfo Configuration:**
```csharp
new ProcessStartInfo
{
    FileName = elevatedHelperPath,
    Arguments = "apply tel-001 tel-002",
    Verb = "runas",                    // Triggers UAC prompt
    UseShellExecute = true,            // Required for 'runas'
    CreateNoWindow = false,            // Show console for user feedback
    RedirectStandardOutput = false     // Cannot redirect with UseShellExecute
}
```

### Theme System

**Dark Mode Detection:**
1. Load saved preference: `%APPDATA%/PrivacyHardeningUI/settings.json`
2. If not saved: Detect system preference
   - Registry: `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`
   - Key: `AppsUseLightTheme` (0=dark, 1=light)
3. Apply theme via `Application.RequestedThemeVariant`

**Theme Persistence:**
- User toggles theme → `IThemeService.SetTheme(dark)`
- Service raises `ThemeChanged` event
- All ViewModels subscribe, update UI-specific properties (e.g., icon paths)
- Save to `settings.json` for next launch

---

## 7. STATE MANAGEMENT & PERSISTENCE

### SQLite Schema Design

**changes Table (Audit Log):**
```sql
CREATE TABLE changes (
    change_id TEXT PRIMARY KEY,           -- UUID
    operation TEXT NOT NULL,              -- "Apply" | "Revert" | "Unknown"
    policy_id TEXT NOT NULL,
    applied_at TEXT NOT NULL,             -- ISO 8601 timestamp
    mechanism TEXT NOT NULL,              -- "Registry" | "Service" | etc.
    description TEXT,
    previous_state TEXT,                  -- JSON or hex string
    new_state TEXT,
    success INTEGER NOT NULL,             -- 1=success, 0=failure
    error_message TEXT,
    snapshot_id TEXT,                     -- Groups related changes
    created_at TEXT DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX idx_policy_id ON changes(policy_id);
CREATE INDEX idx_applied_at ON changes(applied_at);
CREATE INDEX idx_snapshot_id ON changes(snapshot_id);
```

**snapshots Table (System Baselines):**
```sql
CREATE TABLE snapshots (
    snapshot_id TEXT PRIMARY KEY,
    created_at TEXT NOT NULL,
    description TEXT,
    os_version TEXT,                      -- e.g., "10.0.22631"
    os_build TEXT,
    os_sku TEXT,                          -- Pro, Enterprise, etc.
    computer_name TEXT,
    domain_joined INTEGER,                -- 0=no, 1=yes
    mdm_managed INTEGER,
    defender_tamper_protection INTEGER,
    restore_point_id TEXT                 -- Windows System Restore Point ID
);
```

**snapshot_policies Table (Policy State at Snapshot Time):**
```sql
CREATE TABLE snapshot_policies (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    snapshot_id TEXT NOT NULL,
    policy_id TEXT NOT NULL,
    is_applied INTEGER NOT NULL,          -- 0=not applied, 1=applied
    current_value TEXT,                   -- Serialized value at snapshot time
    FOREIGN KEY (snapshot_id) REFERENCES snapshots(snapshot_id)
);
```

### Concurrency Control

**SemaphoreSlim for Serialized Access:**
```csharp
private readonly SemaphoreSlim _dbLock = new(1, 1);

public async Task SaveChangesAsync(ChangeRecord[] changes, CancellationToken ct)
{
    await _dbLock.WaitAsync(ct);  // Acquire lock (blocks if already held)
    try
    {
        // SQLite transaction (INSERT INTO changes...)
    }
    finally
    {
        _dbLock.Release();  // Release lock
    }
}
```
- SQLite does not support true multi-writer concurrency
- Semaphore ensures single-threaded access (prevents `SQLITE_BUSY` errors)
- IPC server can call from multiple threads; operations serialize automatically

### Snapshot Lifecycle

**Creation (Apply Workflow):**
1. Generate UUID: `Guid.NewGuid().ToString()`
2. Capture system info: OS version, domain status, MDM enrollment
3. Insert into `snapshots` table
4. For each policy in apply batch:
   - Query current state via executor
   - Insert into `snapshot_policies` (snapshot_id, policy_id, is_applied, current_value)
5. Attach `snapshot_id` to all `ChangeRecord[]` for this apply operation
6. Save changes to `changes` table

**Revert (Rollback Workflow):**
1. Query most recent snapshot: `SELECT * FROM snapshots ORDER BY created_at DESC LIMIT 1`
2. Load snapshot policies: `SELECT * FROM snapshot_policies WHERE snapshot_id = ?`
3. For each policy: Lookup corresponding `ChangeRecord` from `changes` table
4. Call `executor.RevertAsync(policy, originalChange)`
5. Create new snapshot for revert operation (grouping revert changes)

**Drift Detection:**
- Load baseline snapshot (`snapshot_policies`)
- For each policy: Query current system state via executor
- Compare: `baseline.is_applied` vs `current.is_applied`
- If different: Create `DriftItem { policyId, reason, severity }`
- Return `DriftDetectionResult { DriftDetected, DriftedPolicies[] }`

---

## 8. SECURITY ARCHITECTURE & THREAT MODEL

### Attack Surface Analysis

**1. IPC Named Pipe:**
- **Threat:** Malicious local process sends crafted commands
- **Mitigation:**
  - DACL restricts to Interactive + Admin users only
  - Deny Anonymous, Deny Network (local-only)
  - Caller validation with impersonation + integrity level check
  - Binary signing verification (Authenticode)
  - Command schema validation (reject malformed JSON)
  - 1 MB message size limit (DoS prevention)

**2. PowerShell Script Execution:**
- **Threat:** Unsigned/malicious scripts execute arbitrary code
- **Mitigation:**
  - Path traversal prevention (`IsUnderRoot()`)
  - Parameter sanitization (alphanumeric + underscore whitelist)
  - Authenticode signature verification (if `RequiresSignature: true`)
  - 5-minute timeout (runaway script kill)
  - `UseShellExecute=false` (prevent command injection)

**3. Registry Manipulation:**
- **Threat:** Arbitrary registry modification, kernel key access
- **Mitigation:**
  - Key path validation (reject `..`, absolute paths)
  - Restricted to standard user hives (HKLM, HKCU)
  - No HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control (kernel keys)
  - Previous state capture enables rollback

**4. Privilege Escalation:**
- **Threat:** Non-admin user executes privileged operations
- **Mitigation:**
  - Service runs as SYSTEM (isolated from user process)
  - Caller validation requires admin + high integrity for Apply/Revert
  - Elevation helper requires UAC prompt (explicit user consent)
  - Helper binary must be Authenticode-signed

**5. Denial of Service:**
- **Threat:** Malicious client floods IPC server with requests
- **Mitigation:**
  - 4 concurrent connection limit (resource exhaustion prevention)
  - 1 MB message size limit (memory exhaustion prevention)
  - PowerShell timeout enforcement (CPU exhaustion prevention)
  - Semaphore-based SQLite access (database lock prevention)

**6. Information Disclosure:**
- **Threat:** Error messages expose sensitive system details
- **Mitigation:**
  - Sanitize error messages in responses (no stack traces in production)
  - Registry key paths logged but not returned to non-admin callers
  - Audit logs stored with SYSTEM-only ACLs

### Defense-in-Depth Layers

**Layer 1: Network Isolation**
- Named pipes: Local-only (no remote access)
- DACL denies Network SID

**Layer 2: Process Isolation**
- Service runs in separate process (SYSTEM account)
- UI runs as user (least privilege)

**Layer 3: Authentication**
- Caller impersonation + identity extraction
- WindowsIdentity.GetCurrent() validation

**Layer 4: Authorization**
- Command-level RBAC (read-only vs privileged)
- Admin group membership check
- Integrity level verification (High/System required)

**Layer 5: Binary Trust**
- Authenticode signature verification on client binary
- Certificate revocation checking (via `WinVerifyTrust()`)

**Layer 6: Input Validation**
- JSON schema validation (CommandValidator)
- Parameter sanitization (PowerShell, file paths)
- Type-safe deserialization (no arbitrary object graphs)

**Layer 7: Audit Logging**
- All operations logged to SQLite
- Change records with before/after snapshots
- Immutable append-only log (no deletions)

---

## 9. ARCHITECTURAL PATTERNS & DESIGN PRINCIPLES

### Patterns in Use

**1. Strategy Pattern (Executors):**
- Interface: `IExecutor`
- Implementations: `RegistryExecutor`, `ServiceExecutor`, `TaskExecutor`, `FirewallExecutor`, `PowerShellExecutor`
- Factory: `ExecutorFactory` resolves via `MechanismType` enum
- Benefit: Polymorphic dispatch, open/closed principle (new mechanisms without modifying core)

**2. Command Pattern (IPC):**
- Base: `CommandBase`
- Concrete: `ApplyCommand`, `RevertCommand`, `AuditCommand`, etc.
- Serialization: JSON with polymorphic discriminator (`commandType`)
- Benefit: Decouples request from execution, enables queueing/logging/undo

**3. Repository Pattern (ChangeLog):**
- Abstraction: `ChangeLog` class
- Storage: SQLite database
- Operations: `SaveChangesAsync()`, `GetChangeHistoryAsync()`, `CreateSnapshotAsync()`
- Benefit: Isolates persistence logic, swappable backends (future: SQL Server, PostgreSQL)

**4. Observer Pattern (UI Events):**
- `ServiceClient.ProgressReceived` event
- `IThemeService.ThemeChanged` event
- `NavigationService` publish/subscribe
- Benefit: Loose coupling, reactive UI updates

**5. Dependency Injection:**
- Container: Microsoft.Extensions.DependencyInjection
- Lifetime scopes: Singleton (services), Transient (views), Scoped (not used)
- Benefit: Testability, inversion of control, explicit dependencies

**6. MVVM (Model-View-ViewModel):**
- Model: `PolicyDefinition`, `ChangeRecord`, domain entities
- View: Avalonia XAML views
- ViewModel: `MainViewModel`, `PolicySelectionViewModel`, etc.
- Binding: CommunityToolkit.Mvvm source generators
- Benefit: Separation of concerns, testable presentation logic

### SOLID Principles

**Single Responsibility:**
- `PolicyLoader`: Only loads policies
- `PolicyValidator`: Only validates policies
- `ChangeLog`: Only persists state
- Each executor: Only handles one mechanism type

**Open/Closed:**
- New executors added without modifying `PolicyEngineCore`
- New commands added without modifying IPC server (polymorphic dispatch)

**Liskov Substitution:**
- All `IExecutor` implementations interchangeable
- `CommandBase` derived types usable polymorphically

**Interface Segregation:**
- `IExecutor`: Small, focused interface (4 methods)
- `IThemeService`: Single concern (theme management)

**Dependency Inversion:**
- `PolicyEngineCore` depends on `IExecutor` abstraction, not concrete types
- UI depends on `ServiceClient` abstraction, not named pipe details

---

## 10. KNOWN TECHNICAL DEBT & IMPROVEMENT AREAS

### Critical Issues

**1. No Schema Versioning for SQLite**
- **Problem:** Database migrations are manual (e.g., `EnsureColumnExistsAsync()`)
- **Impact:** Brittle upgrades, potential data loss on version mismatch
- **Solution:** Implement migration framework (e.g., FluentMigrator, DbUp, or custom versioning table)
- **Priority:** HIGH (blocking production deployments)

**2. Policy Cache Invalidation**
- **Problem:** `_cachedPolicies` lives for service lifetime, no refresh without restart
- **Impact:** New policies require service restart
- **Solution:** File watcher on policies directory + cache invalidation, or TTL-based cache
- **Priority:** MEDIUM (operational friction)

**3. Circular Dependency Detection at Runtime**
- **Problem:** `DependencyResolver` throws `InvalidOperationException` during apply
- **Impact:** Bad policies crash apply workflow
- **Solution:** Validate dependency graph during `PolicyLoader.LoadAllPoliciesAsync()`
- **Priority:** MEDIUM (fail-fast principle violated)

**4. PowerShell Audit Pattern Matching**
- **Problem:** Regex-based parsing of script output (brittle, error-prone)
- **Impact:** Script changes break audit logic
- **Solution:** Structured `VerificationCommand` DSL or direct API calls (e.g., `Get-Service` → `ServiceController`)
- **Priority:** MEDIUM (code smell, maintenance burden)

### Performance Issues

**5. No Rate Limiting on IPC Commands**
- **Problem:** Malicious caller could spam commands
- **Impact:** Potential DoS (though 4-connection limit mitigates)
- **Solution:** Per-caller command rate limiter (token bucket algorithm)
- **Priority:** LOW (defense-in-depth)

**6. No Differential Auditing**
- **Problem:** Audit checks ALL policies every time
- **Impact:** Slow on large policy sets (100+ policies)
- **Solution:** Track last-audit timestamp, only check drifted policies
- **Priority:** MEDIUM (user experience)

**7. SQLite Index Optimization**
- **Problem:** Only indexed on `policy_id`, `applied_at`, `snapshot_id` (single-column)
- **Impact:** Queries by date range may be slow
- **Solution:** Compound index on `(policy_id, applied_at)` for history queries
- **Priority:** LOW (marginal gains)

### Observability Gaps

**8. No Structured Logging for Compliance**
- **Problem:** Logs are free-form text to Windows Event Log
- **Impact:** Limited visibility into policy application history for audits
- **Solution:** Event Tracing for Windows (ETW) or structured syslog integration
- **Priority:** MEDIUM (compliance/auditing requirement)

**9. Error Messages May Leak System Info**
- **Problem:** Registry errors expose full key paths
- **Impact:** Potential information disclosure to non-admin callers
- **Solution:** Sanitize error messages in `ResponseBase`, redact sensitive paths
- **Priority:** LOW (security hardening)

### Code Quality

**10. UI State Persistence Limited**
- **Problem:** `MainViewModel` stores state in memory only
- **Impact:** Settings lost on app restart (window position, last selected profile)
- **Solution:** `SettingsService` should persist ViewModel state to JSON
- **Priority:** LOW (user experience enhancement)

---

## 11. TESTING STRATEGY & QUALITY ASSURANCE

### Test Pyramid (Recommended)

**Unit Tests (70% coverage target):**
- Executor implementations: Mock policies, verify `ApplyAsync()`/`RevertAsync()` logic
- `DependencyResolver`: Test topological sort, circular dependency detection
- `PolicyValidator`: Schema validation, constraint checking
- `CommandValidator`: JSON deserialization, polymorphic command handling
- `CallerValidator`: Mock `WindowsIdentity`, test admin/integrity checks

**Integration Tests (20% coverage target):**
- IPC communication: Real named pipe, test command/response roundtrip
- SQLite persistence: Verify transactional inserts, snapshot creation
- Policy loading: YAML deserialization, applicability filtering
- End-to-end apply workflow: Real registry/service modifications (isolated environment)

**UI Tests (10% coverage target):**
- Avalonia headless testing: ViewModel command execution, property change notifications
- Mock `ServiceClient`: Test standalone mode fallback
- Navigation flow: Verify page transitions, state persistence

### Test Infrastructure (Currently Missing)

**Required Tooling:**
- **xUnit** or **NUnit**: Unit test framework
- **Moq** or **NSubstitute**: Mocking library
- **FluentAssertions**: Readable assertions
- **Avalonia.Headless**: UI testing without display
- **Testcontainers**: Isolated test environments (for integration tests)

**CI/CD Integration:**
- GitHub Actions: Run tests on every PR
- Code coverage: SonarQube or Coverlet
- Static analysis: Roslyn analyzers, StyleCop

---

## 12. DEPLOYMENT & OPERATIONAL CONSIDERATIONS

### Service Installation

**Manual Installation:**
```powershell
# Create service
sc create PrivacyHardeningService binPath= "C:\Program Files\PrivacyHardeningFramework\PrivacyHardeningService.exe" start= demand
sc config PrivacyHardeningService obj= "LocalSystem"
sc description PrivacyHardeningService "Privacy Hardening Framework Service"

# Start service
net start PrivacyHardeningService
```

**Automated Installation (Inno Setup):**
- Installer script: `scripts/installer.iss`
- Bundles: Service exe, UI exe, CLI exe, policies YAML
- Post-install: Registers service, adds Start Menu shortcuts
- Uninstall: Stops service, removes files, cleans registry

### Configuration Management

**Policy Directory Discovery (Precedence Order):**
1. Development: `{ProjectRoot}/../../../policies/` (relative to executable)
2. Production: `%PROGRAMDATA%\PrivacyHardeningFramework\policies\`
3. Portable: `{ExecutableDirectory}\policies\`

**Settings Persistence:**
- UI settings: `%APPDATA%\PrivacyHardeningUI\settings.json`
- Service state: SQLite database at `%PROGRAMDATA%\PrivacyHardeningFramework\state.db`
- Logs: Windows Event Viewer (Application log, source: `.NET Runtime`)

### Upgrade Path

**Service Upgrade:**
1. Stop service: `net stop PrivacyHardeningService`
2. Replace binary: Copy new `PrivacyHardeningService.exe`
3. Auto-migration: `ChangeLog.InitializeDatabaseAsync()` adds new columns
4. Start service: `net start PrivacyHardeningService`
5. IPC reconnection: UI auto-reconnects on next operation

**Policy Updates:**
- Add/modify YAML files in policies directory
- Service restart required (cache invalidation not implemented)
- Future: File watcher for hot reload

**Database Migrations:**
- Currently: Manual `ALTER TABLE` in `ChangeLog` constructor
- Recommended: Migration framework with version tracking

---

## 13. CROSS-CUTTING CONCERNS

### Logging & Observability

**Current State:**
- Windows Event Log (Application log)
- Log levels: Critical, Error, Warning, Information, Debug
- Source: `.NET Runtime`

**Recommended Enhancements:**
- Structured logging: Add `Microsoft.Extensions.Logging.Abstractions` with JSON formatter
- Correlation IDs: Add `CommandId` to all log entries for request tracing
- ETW provider: For performance profiling
- Health checks: HTTP endpoint for monitoring (e.g., `/health`)

### Error Handling Philosophy

**Fail-Closed:**
- Authorization failures → Reject command
- Signature verification failures → Reject script execution
- Schema validation failures → Reject command

**Fail-Safe:**
- Service crashes → Windows Service Manager auto-restarts
- SQLite transaction failures → All-or-nothing (no partial state)
- Policy application failures → Rollback available via revert

**User Communication:**
- Technical errors → Logged, generic message to user
- Business logic errors → User-friendly message (e.g., "Policy XYZ requires Windows Pro")

### Resource Management

**Async/Await Throughout:**
- All I/O operations non-blocking
- `CancellationToken` support for cooperative cancellation
- No `Task.Wait()` or `.Result` (deadlock prevention)

**IDisposable Patterns:**
- `ChangeLog`: Disposes SQLite connection
- `IPCServer`: Disposes named pipe streams
- `ServiceController`: Disposed after use

**Timeout Enforcement:**
- IPC connection: 1.5 seconds
- PowerShell execution: 5 minutes
- Named pipe read: No timeout (streaming progress)

---

## 14. FUTURE ARCHITECTURAL ENHANCEMENTS

### Phase 1: Foundational Improvements

**1. Schema Versioning**
- Implement migration framework (FluentMigrator or DbUp)
- Version tracking table: `schema_versions(version, applied_at)`
- Auto-migration on service startup

**2. Policy Cache Invalidation**
- `FileSystemWatcher` on policies directory
- On YAML change: Reload specific policy, invalidate cache
- Notify connected clients via IPC event (new command: `PolicyUpdated`)

**3. Circular Dependency Validation**
- Move to `PolicyValidator.ValidateAllPolicies()`
- Run during service startup (fail-fast)
- Report all circular dependencies in single pass (not just first detected)

**4. Structured Logging**
- Replace `ILogger<T>` with structured logger (e.g., Serilog)
- JSON output: `{"Timestamp":"...", "Level":"...", "CommandId":"...", "Message":"..."}`
- Sinks: Event Log, file, ETW

### Phase 2: Performance & Scalability

**5. Differential Auditing**
- Track last audit timestamp per policy
- Only re-check policies with drift or never audited
- UI: Show "last audited" timestamp

**6. Parallel Policy Application**
- Analyze dependency graph for independent policies
- Execute independent policies in parallel (TPL Dataflow)
- Respect dependencies: Wait for prerequisites before applying dependents

**7. Background Drift Detection**
- Service timer: Check drift every N hours
- Raise event to connected clients if drift detected
- UI: Show notification badge

### Phase 3: Enterprise Features

**8. Remote Management**
- gRPC or HTTP API (alternative to named pipes)
- Mutual TLS authentication
- Centralized policy deployment server

**9. Compliance Reporting**
- Generate PDF/CSV reports (compliance status, change history)
- CIS Benchmark mapping
- NIST 800-53 control mapping

**10. Group Policy Integration**
- Export policies as GPO settings
- Hybrid mode: Respect GPO + allow local overrides

---

## 15. AI AGENTIC CODING GUIDELINES

### When Developing NEW Features

**1. Understand the Domain First:**
- Read existing `PolicyDefinition` YAML files to understand schema
- Trace IPC flow: Command → Service → Executor → Response
- Review existing executors before creating new mechanisms

**2. Follow Existing Patterns:**
- New mechanism? Implement `IExecutor` interface
- New command? Derive from `CommandBase`, add to `IPCServer` dispatcher
- New UI view? Create ViewModel (singleton), View (transient), wire via DI

**3. Security Checklist:**
- Input validation: Sanitize all user-provided data
- Authorization: Check caller privileges in service
- Audit logging: Log all state mutations
- Error handling: Fail-closed on security checks

**4. Testing Requirements:**
- Unit test: Business logic (executors, validators, resolvers)
- Integration test: IPC communication, SQLite persistence
- Manual test: UI flow, elevation handling, error scenarios

### When REFACTORING Existing Code

**1. Preserve Behavior:**
- Run existing tests (if available)
- Add tests for current behavior before refactoring
- Use git bisect to identify regressions

**2. Incremental Changes:**
- One pattern at a time (don't mix concerns)
- Separate commits: Refactor + Feature
- Review diffs carefully (use `git diff --word-diff`)

**3. Update Documentation:**
- XML doc comments on public APIs
- Update this AI prompt if architecture changes
- Keep YAML schema docs in sync

### When DEBUGGING Issues

**1. Reproduce Locally:**
- Run service in console mode (not as Windows Service)
- Attach debugger to both UI + Service processes
- Enable verbose logging (`appsettings.json`: `"LogLevel": {"Default": "Debug"}`)

**2. Check Audit Trail:**
- Query SQLite `changes` table for recent operations
- Compare `PreviousState` vs `NewState` for unexpected mutations
- Verify `snapshot_id` grouping for transaction boundaries

**3. Common Failure Modes:**
- **"Service unavailable"**: Check service status (`sc query PrivacyHardeningService`)
- **"Unauthorized"**: Verify UI running as admin or helper elevation
- **"Policy not applied"**: Check `IsApplicable()` (OS build, SKU mismatch)
- **"Circular dependency"**: Validate dependency graph in YAML

### Code Style & Conventions

**C# Naming:**
- PascalCase: Classes, methods, properties, enums
- camelCase: Local variables, method parameters
- _camelCase: Private fields (with underscore prefix)
- UPPER_CASE: Constants

**Async Naming:**
- Suffix async methods with `Async` (e.g., `ApplyAsync()`)
- Return `Task` or `Task<T>` (never `async void` except event handlers)
- Always accept `CancellationToken` parameter

**Error Handling:**
- Specific exceptions: `InvalidOperationException`, `ArgumentException`, `UnauthorizedAccessException`
- Wrap external API exceptions: `try { Registry.GetValue() } catch (SecurityException ex) { throw new UnauthorizedAccessException("...", ex); }`
- Never swallow exceptions silently (always log or rethrow)

---

## 16. FINAL DIRECTIVE: PROFESSIONAL ENGINEERING RIGOR

This is NOT a prototype. This is NOT a hackathon project. This is a **production-grade security application** managing critical system configurations.

**Non-Negotiable Requirements:**

1. **Security First:**
   - Every input validated
   - Every privilege boundary checked
   - Every state mutation audited
   - Fail-closed on security checks

2. **Reliability:**
   - Transactional state changes (all-or-nothing)
   - Rollback capability for every operation
   - Graceful degradation (standalone mode)
   - No silent failures (log + report errors)

3. **Maintainability:**
   - Follow existing patterns (Strategy, Command, MVVM)
   - Dependency injection for all services
   - Unit tests for business logic
   - Documentation for complex logic

4. **User Sovereignty:**
   - `AutoApply` MUST be false (user consent required)
   - Clear warning for high-risk policies
   - Transparent change preview (diff view)
   - Easy rollback mechanism

5. **Performance:**
   - Async I/O throughout (no blocking calls)
   - Timeout enforcement (DoS prevention)
   - Resource cleanup (IDisposable)
   - Efficient SQLite queries (indexes)

**When in Doubt:**
- Prioritize security over convenience
- Prioritize correctness over performance
- Prioritize user control over automation
- Prioritize explicit over implicit

**Code Review Checklist:**
- [ ] Security: Input validation, authorization, audit logging
- [ ] Correctness: Unit tests pass, manual testing performed
- [ ] Performance: No blocking I/O, timeouts enforced
- [ ] Maintainability: Follows existing patterns, documented
- [ ] User Experience: Error messages clear, rollback available

---

## APPENDIX: KEY FILES & LOCATIONS

### Service Project
- `PrivacyHardeningService/Program.cs`: DI container setup, service host
- `PrivacyHardeningService/ServiceMain.cs`: Windows Service entry point
- `PrivacyHardeningService/IPCServer.cs`: Named pipe server, connection handling
- `PrivacyHardeningService/PolicyEngineCore.cs`: Apply/Revert orchestration
- `PrivacyHardeningService/Executors/`: IExecutor implementations
- `PrivacyHardeningService/ChangeLog.cs`: SQLite persistence
- `PrivacyHardeningService/CallerValidator.cs`: IPC authorization

### UI Project
- `PrivacyHardeningUI/App.axaml.cs`: UI DI container, theme initialization
- `PrivacyHardeningUI/ViewModels/MainViewModel.cs`: Navigation hub, apply orchestration
- `PrivacyHardeningUI/ServiceClient.cs`: IPC client, standalone fallback
- `PrivacyHardeningUI/Views/`: Avalonia XAML views
- `PrivacyHardeningUI/Styles/`: Theme resources (light/dark)

### Contracts Project
- `PrivacyHardeningContracts/PolicyDefinition.cs`: Core domain model
- `PrivacyHardeningContracts/Commands/`: IPC command types
- `PrivacyHardeningContracts/Responses/`: IPC response types
- `PrivacyHardeningContracts/ChangeRecord.cs`: Audit log entry

### Policy Definitions
- `policies/`: YAML policy definitions (loaded by service)
- `policies/scripts/`: PowerShell scripts for PowerShellExecutor

### Build & Deployment
- `scripts/installer.iss`: Inno Setup installer definition
- `scripts/capture-ui-screens.ps1`: Automated UI testing/screenshots
- `.github/workflows/`: CI/CD pipelines (CodeQL, release automation)

---

## GLOSSARY OF PROFESSIONAL TERMS

- **Strategy Pattern**: Behavioral design pattern enabling algorithm selection at runtime
- **Dependency Injection**: Inversion of control technique for managing object dependencies
- **MVVM**: Model-View-ViewModel architectural pattern for UI separation
- **Named Pipe**: Windows IPC mechanism for inter-process communication
- **DACL**: Discretionary Access Control List (Windows security descriptor)
- **Impersonation**: Windows security feature to run code with caller's identity
- **Integrity Level**: Windows security boundary (Low/Medium/High/System)
- **Authenticode**: Microsoft code-signing technology for binary trust
- **Topological Sort**: Graph algorithm for dependency ordering (DAG)
- **Transactional Semantics**: All-or-nothing guarantees for state mutations
- **Fail-Closed**: Security policy rejecting access on validation failure
- **Fail-Safe**: Reliability strategy maintaining safety on component failure
- **Defense-in-Depth**: Layered security approach with multiple controls
- **Idempotent**: Operation producing same result when applied multiple times
- **Semaphore**: Concurrency primitive for resource access control
- **Async/Await**: .NET asynchronous programming model (TAP)
- **CancellationToken**: Cooperative cancellation mechanism for async operations
- **Observable Property**: MVVM property raising PropertyChanged notifications
- **Relay Command**: MVVM command implementation with can-execute logic
- **Polymorphic Serialization**: JSON discriminator-based type deserialization
- **Audit Trail**: Immutable log of all state changes for compliance
- **Snapshot**: Point-in-time system state baseline for drift detection
- **Rollback**: Restoration to previous state using audit trail
- **Drift Detection**: Comparison of current state vs baseline snapshot

---

**END OF ADVANCED AI AGENTIC DEVELOPMENT PROMPT**

*This document represents the complete architectural knowledge required to develop, maintain, and extend the Privacy Hardening Framework at a professional software engineering level. Use this as the foundation for all AI-assisted coding, refactoring, and debugging tasks.*
