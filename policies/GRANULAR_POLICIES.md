# Granular Control Policies

## Overview

This directory contains **atomic, granular privacy policies** that provide **complete user control** over every privacy setting. Each policy follows the principle: **User is the Ultimate Authority**.

## Key Principles

### ✅ What These Policies Provide

1. **Individual Control** - Each policy controls ONE specific setting
2. **Multiple Value Options** - Not just on/off, but all available values with explanations
3. **Full Transparency** - Exact registry paths, service names, endpoints shown
4. **No Defaults** - User must explicitly choose every setting
5. **Detailed Breakage Info** - Know exactly what might break
6. **Complete Reversibility** - Clear instructions for undoing changes

### ❌ What These Policies DON'T Do

1. **Bundle Settings** - No "disable all telemetry" bulk actions
2. **Auto-Select** - No pre-configuration or recommendations forced on users
3. **Hide Details** - All technical details visible
4. **Make Assumptions** - User decides everything explicitly

## Policy Categories

### Atomic Registry Policies (One Key Per Policy)

**Example**: Activity History
- ❌ OLD APPROACH: One policy sets 3 registry keys
- ✅ NEW APPROACH: Three separate policies (tel-004-a, tel-004-b, tel-004-c)

**Benefits**:
- User can enable ANY combination
- Clear understanding of each setting's purpose
- Easy to troubleshoot if one causes issues

### Parameterized Policies (Multiple Value Options)

**Example**: Diagnostic Data Level (tel-001)
- Value 0: Security (Enterprise only) - Minimal data
- Value 1: Basic - Standard privacy
- Value 2: Enhanced - More data
- Value 3: Full - Windows default

**User chooses** which level with full explanation of each.

### Granular Service Configuration

**Example**: DiagTrack Service (svc-001)

User configures EACH aspect independently:
1. **Startup Type**: Automatic / AutomaticDelayed / Manual / Disabled
2. **Service Action**: NoAction / Stop / StopAndDisable
3. **Recovery Options**: KeepExisting / DisableRecovery / TakeNoAction

### Per-Endpoint Firewall Rules

**Example**: Telemetry Endpoints (net-001-a, net-001-b, ...)

Each endpoint is a separate policy:
- net-001-a: vortex.data.microsoft.com
- net-001-b: watson.telemetry.microsoft.com
- net-001-c: settings-win.data.microsoft.com
- ... (50+ endpoints)

User selects which specific endpoints to block.

### Multi-Action Task Policies

**Example**: Compatibility Appraiser (task-001)

User chooses from multiple actions:
1. **Disable** - Reversible, safe
2. **Delete** - Permanent removal
3. **ModifyTriggers** - Disable specific triggers
4. **ExportOnly** - Backup without changes

Each action has different privacy/reversibility trade-offs.

## New Granular Control Policies (Session 2025-12-30)

### Telemetry Category

| Policy ID | Name | Type | Description |
|-----------|------|------|-------------|
| tel-001 | Diagnostic Data Level | Parameterized (4 values) | Choose exact telemetry level |
| tel-004-a | Activity Feed | Atomic Registry | Local activity tracking |
| tel-004-b | Publishing User Activities | Atomic Registry | MS Account activity sharing |
| tel-004-c | Uploading User Activities | Atomic Registry | Cloud activity uploads |

### Services Category

| Policy ID | Name | Type | Description |
|-----------|------|------|-------------|
| svc-001 | DiagTrack Service | Multi-Parameter Service | 3 independent configuration options |

### Network Category

| Policy ID | Name | Type | Description |
|-----------|------|------|-------------|
| net-001-a | Block vortex.data.microsoft.com | Per-Endpoint Firewall | Individual endpoint control |

### Scheduled Tasks Category

| Policy ID | Name | Type | Description |
|-----------|------|------|-------------|
| task-001 | Compatibility Appraiser | Multi-Action Task | 4 action options with different trade-offs |

## Example Configurations

### Maximum Privacy + Maximum Reversibility

```yaml
Policies to Enable:
- tel-001: Set to "Basic" (value 1)
- tel-004-a: Disable Activity Feed
- tel-004-b: Disable Publishing
- tel-004-c: Disable Uploading
- svc-001: Startup=Manual, Action=Stop, Recovery=KeepExisting
- task-001: Action=Disable (not Delete)
```

**Result**: High privacy with full ability to reverse all changes

### Maximum Privacy + Permanent Changes

```yaml
Policies to Enable:
- tel-001: Set to "Security" (value 0, Enterprise only)
- tel-004-a, b, c: All disabled
- svc-001: Startup=Disabled, Action=StopAndDisable, Recovery=TakeNoAction
- task-001: Action=Delete
- net-001-a: Block vortex endpoint
```

**Result**: Maximum privacy hardening (some changes permanent)

### Selective Privacy (User Custom Mix)

```yaml
User might choose:
- tel-001: "Basic" (not Security) for compatibility
- tel-004-a: Disable (no local tracking)
- tel-004-b: KEEP ENABLED (allow MS Account sync)
- tel-004-c: Disable (no cloud upload)
- svc-001: Startup=AutomaticDelayed, Action=NoAction, Recovery=KeepExisting
- task-001: Action=ModifyTriggers (disable daily, keep idle)
```

**Result**: Custom configuration meeting specific requirements

## Technical Implementation

### Enhanced Models Created

1. **PolicyValueOption** - Represents selectable values with descriptions
2. **SelectableOption<T>** - Generic option structure for services/tasks
3. **AdvancedOptions** - User-toggleable advanced settings
4. **FirewallEndpoint** - Per-endpoint firewall configuration
5. **ServiceConfigOptions** - Multi-parameter service control
6. **TaskConfigOptions** - Multi-action task control
7. **PolicyDependency** - User-visible dependencies with override

### Benefits

- Type-safe configuration
- Full IntelliSense support in UI
- Clear data structures for serialization
- Easy to extend with new options

## Future Expansion

### Planned Atomic Policies

- 100+ total policies covering all Windows 11 privacy settings
- Each following granular control principles
- All individually selectable
- Complete transparency and reversibility

### Categories to Complete

1. **Telemetry**: 40+ atomic policies
2. **Network**: 25+ per-endpoint policies
3. **Services**: 15+ multi-parameter service policies
4. **Tasks**: 30+ multi-action task policies
5. **AI & Copilot**: 10+ feature control policies
6. **Windows Defender**: 12+ cloud feature policies
7. **UX & Ads**: 15+ interface privacy policies

## User Control Philosophy

> **"The user owns their system and must have complete, detailed control over every privacy-related change. NO assumptions, NO convenient defaults that reduce control, NO 'trust us' black boxes."**

Every policy in this directory embodies this philosophy.

---

**Last Updated**: 2025-12-30
**Session**: Granular Control Implementation
**Status**: Foundation Complete - Ready for Expansion
