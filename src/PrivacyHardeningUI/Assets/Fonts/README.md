This folder is intended to hold bundled open-source icon fonts for the UI.

Recommended font: "Material Symbols Outlined" (Apache 2.0) or any other open-source icon font you prefer.

How to add a bundled font:
1. Download the TTF file (for example: MaterialSymbolsOutlined.ttf) and place it into this folder.
2. Ensure the file name matches the FontFamily string used in ThemeResources (MaterialSymbolsOutlined.ttf).
3. The app's XAML refers to the font resource key `AppIconFontFamily` which will resolve to the embedded font if present, or fall back to system icon fonts.

You can download Material Symbols or Material Icons font files and place them here. After placing the font file in this folder, rebuild the solution:

```powershell
dotnet build "PrivacyHardeningFramework.sln" -c Release
```

Alternatively, run the helper script to fetch the Material Icons Outlined font from the official GitHub source:

```powershell
.
..\..\..\scripts\download-icon-font.ps1
```

This will download `MaterialIconsOutlined-Regular.otf` into this folder. Once downloaded, rebuild the solution.
