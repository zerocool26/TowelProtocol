# Privacy Hardening Framework: Advanced Capability & Orchestration Prompt

**Use this prompt when you need to perform deep architectural changes, add complex features, or fine-tune the core engine of the Privacy Hardening Framework.**

---

## 🏗️ 1. ARCHITECTURAL IDENTITY & CONSTRAINTS

You are an expert systems engineer working on a **Privileged Windows Privacy Tool**. You must respect the "Security Separation of Concerns" (SSoC):

1.  **Standard User UI (`PrivacyHardeningUI`)**: Uses Avalonia + MVVM. It NEVER performs system changes directly. It communicates via Named Pipes.
2.  **Privileged Service (`PrivacyHardeningService`)**: A Windows Service (SYSTEM) that listens for commands, verifies authorization, and executes hardening logic.
3.  **Elevated Helper (`PrivacyHardeningElevated`)**: A standalone CLI used by the Service for specific atomic operations or for manual "Run as Admin" CLI usage.
4.  **Implicit Contract (`PrivacyHardeningContracts`)**: The source of truth for all IPC. Every action must follow the `CommandBase` -> `ResponseBase` pattern.

### 🛡️ Standalone Safety Protocol
When the service is not running (**Standalone Mode**), the UI must:
- Fallback to local YAML policy parsing for definitions.
- Provide "Mock/Placeholder" results for State, Audit, and Drift queries.
- **DISABLE** "Apply" and "Revert" actions with clear tooltips.
- Never "Hang" or "Crash" on pipe connection failures.

---

## 🔧 2. CAPABILITY EXPANSION & FINE-TUNING

### A. Granular Policy Schema (The "Deep-Tune" Engine)
Current policies are often binary. To add "Advanced Configuration", expand `PolicyDefinition` to include:
- `List<PolicyOption> Options`: Sub-settings (e.g., Telemetry Level: 0, 1, 2, 3).
- `PolicyImpact Impact`: Performance vs. Privacy vs. Compatibility rating.
- `PolicyEvidenceCheck Evidence`: The exact Registry key/path used to verify state.

### B. Evidence-Based Auditing
Audit results should not just be "Compliant/Non-Compliant". They must provide **Evidence**:
- **Expected**: `HKEY_LOCAL_MACHINE\...\Value = 1`
- **Actual**: `HKEY_LOCAL_MACHINE\...\Value = (missing)`
- **Remediation**: The specific PowerShell or Registry change required.

### C. The Preview/Diff Workspace (The "Gatekeeper")
Before applying changes, the UI MUST present a **Diff Workspace**:
- **Left**: Current System State.
- **Right**: Proposed State (after policies).
- **Middle**: Selection/Checkboxes for atomic selection.
- **Footer**: Safety warning regarding un-signed policy files.

---

## 📈 3. FUNCTIONAL ROADMAP FOR AGENTS

### 1. Unified Status & Health
- Implement a `SystemHealthScore` based on active vs. inactive privacy policies.
- Add "Management Signals" (MDM, Domain, SCCM detection) to avoid breaking enterprise machines.

### 2. The Timeline (History & Rollback)
- Every successful "Apply" must generate a `SystemSnapshot`.
- The `HistoryView` should group changes by session.
- Implement "Atomic Revert" (Roll back only Session ID: X).

### 3. Reporting & Redaction (Professional Export)
- Generate PDF/HTML reports of current security posture.
- Implement "Redaction Rules" for reports (hide Username, ComputerName, IP if requested).

---

## 🛠️ 4. CODING STANDARDS & PATTERNS

- **MVVM**: Use `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`).
- **XAML**: Stay within Avalonia's specific flavor (use `StaticResource`, avoid WPF-only triggers).
- **IPC**: All service calls must be `async` and include `CancellationToken` support in the client.
- **Error Handling**: Use the `ServiceUnavailableException` for all IPC timeouts/failures to trigger the Standalone UI state gracefully.
- **DI**: Register all services in `App.axaml.cs` using `Host.CreateDefaultBuilder()`.

---

## 🎯 5. THE "COMMANDER" MODE (HOW TO START)
When asked to "Improve Everything", you should:
1. **Audit the IPC Contract**: Ensure the Command/Response pair is robust.
2. **Harden the ViewModel**: Ensure it handles `Success=false` from the service without UI breakage.
3. **Refine the Visual Density**: Ensure the UI looks like a "Professional Tool" (Denser grids, status indicators, readable logs).
4. **Test Standalone First**: If it works in Standalone mode, it will be stable with the Service.
