Design system and visual assets
================================

Overview
--------
This repository uses an Avalonia-based UI (`PrivacyHardeningUI`). Visual resources are organized so UI screens can consume consistent tokens (colors, typography, spacing) and shared styles (controls, icons, animations).

Where things live
-----------------
- `src/PrivacyHardeningUI/Styles/`
  - `ThemeResources.Light.axaml`, `ThemeResources.Dark.axaml` — color palettes, brushes, typography, spacing, and animation tokens.
  - `ControlStyles.axaml` — control templates and common control styling.
  - `IconStyles.axaml`, `Animations.axaml` — icon helpers and animation timings.
- `src/PrivacyHardeningUI/Assets/Fonts/` — bundled fonts. Recommended: Inter (for UI text) and a Material/Material Symbols icon font for icons.
- `src/PrivacyHardeningUI/Assets/Icons/` — (create) store SVG icons for richer vector icons.

Quick goals
-----------
- Make themes declarative and consumable by all Views/Controls.
- Provide bundled fonts for consistent typography and iconography.
- Add a small set of SVG icons and a helper `IconHelper` control to render them.
- Support Light, Dark, and High-Contrast modes via the existing `ThemeResources.*.axaml` files.

Fonts
-----
Recommended fonts:
- Inter (recommended for body text). Add `Inter-Regular.ttf` and `Inter-Bold.ttf` to `Assets/Fonts/`.
- Material Symbols Outlined or Material Icons (for icons) — add the TTF/OTF to `Assets/Fonts/`.

To fetch the recommended fonts (helper scripts included):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\download-inter-fonts.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\download-icon-font.ps1
```

If download fails (no network), manually place the font files into `src/PrivacyHardeningUI/Assets/Fonts/` and rebuild.

Design tokens and extension
---------------------------
- Colors, brushes, typography, spacing, and animation durations are defined as XAML resources in `ThemeResources.*.axaml`.
- To add or change tokens, edit the appropriate `ThemeResources.*.axaml` file and update keys like `AccentColor`, `FontSizeBody`, `SpacingMD`, etc.
- For a more modular setup, you can split tokens into `Assets/DesignTokens/Colors.axaml` and `Assets/DesignTokens/Typography.axaml` and merge them from `Styles/Theme.axaml`.

Icons
-----
- Add SVG files under `src/PrivacyHardeningUI/Assets/Icons/` and use the existing `IconHelper` control to render them.
- If an icon font is present, `AppIconFontFamily` resource will resolve to the embedded font; otherwise XAML falls back to system icon fonts.

Packaging & installer notes
--------------------------
- Ensure the installer and packaging steps include font files. Update `installers/` scripts or `Launch*` helpers to copy `Assets/Fonts/*` into the packaged app folder.

Next implementation steps (recommended)
--------------------------------------
1. Add missing fonts (run the scripts or add fonts manually).
2. Add a small `Assets/Icons/` set with SVGs for common actions (check, close, info, warning).
3. Consolidate theme loading by adding a `Styles/Theme.axaml` that references tokens and control styles.
4. Update a primary window (`MainWindow.axaml`) to use the new style resources for buttons, cards, and inputs.
5. Create `docs/visual-tests.md` describing manual snapshot checks and how to capture screenshots.

If you'd like, I can now:
- Create `Assets/DesignTokens/` files and `Styles/Theme.axaml` to modularize tokens; and/or
- Add a few SVG icons and update `IconHelper` to render them; and/or
- Update `LaunchGUI.ps1`/`LaunchGUI.bat` to ensure fonts are available when running locally.

Tell me which of the implementation steps you'd like me to do next, or I can start with the fonts+icons work automatically.
