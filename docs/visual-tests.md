Visual tests and screenshot capture
==================================

Purpose
-------
Provides manual and automated helpers to capture screenshots of the UI for visual regression checks.

Quick manual checks
-------------------
1. Run the UI: `powershell -NoProfile -ExecutionPolicy Bypass -File LaunchGUI.ps1`
2. Verify main window renders with updated title, theme button, and tab icons.
3. Inspect `Policy Selection`, `Audit`, and `Diff View` tabs to ensure spacing and typography look consistent.

Automated screenshot helper
---------------------------
A PowerShell helper is provided at `scripts\capture-ui-screens.ps1`.
It will:
- Build the UI project (Release)
- Optionally launch the UI in background
- Capture a primary-screen screenshot after a short delay
- Save the screenshot to `docs/screenshots/` with a timestamped filename

Usage
-----
- Dry run (build + create folder):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\capture-ui-screens.ps1 -NoRun
```

- Capture (launch app, capture after 4s):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\capture-ui-screens.ps1
```

Notes
-----
- The script uses `System.Drawing`/`System.Windows.Forms` to capture the primary display. It captures the whole screen; if you prefer capturing a single window, run the UI and use a manual screenshot tool.
- Running the capture will briefly launch the UI and then terminate it after capturing.
- For CI-based visual testing, consider using a headless rendering approach or a GUI-less Avalonia renderer to generate deterministic bitmaps.
