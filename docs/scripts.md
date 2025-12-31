Using PowerShell snapshot and revert scripts

This project supports PowerShell-based mechanisms with optional snapshot and revert scripts.

Mechanism details (example JSON for a policy):

{
  "ScriptPath": "samples/snapshot_example.ps1",
  "RequiresSignature": false,
  "SnapshotScriptPath": "samples/snapshot_example.ps1",
  "RevertScriptPath": "samples/revert_example.ps1"
}

How it works
- When `SnapshotScriptPath` is provided, the `PowerShellExecutor` will run it before applying the main script and save its output into `ChangeRecord.PreviousState`.
- When `RevertScriptPath` is provided, the `PowerShellExecutor` will run the revert script during `RevertAsync` and pass the previous state as the parameter `PreviousState` (string).

Best practices
- Snapshot scripts should output a compact JSON string describing the values needed to restore state.
- Revert scripts should accept a `PreviousState` string (JSON) and perform the restoration. They should return a non-error exit code and (optionally) write a short message to stdout.
- Always sign production scripts and set `RequiresSignature` to true in mechanism details.

Sample scripts location: `scripts/samples/` (examples included: `snapshot_example.ps1`, `revert_example.ps1`).
