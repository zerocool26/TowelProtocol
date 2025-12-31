Packaging PrivacyHardeningElevated

This document explains how to package the elevated helper for distribution and include it in release artifacts.

1. Build the solution (Release):

```powershell
dotnet build "PrivacyHardeningFramework.sln" -c Release
```

2. Use the provided packaging script to collect the built binary and produce a ZIP:

```powershell
# from repo root
.\scripts\package_elevated_helper.ps1 -Configuration Release
```

3. The script creates `dist\PrivacyHardeningElevated.zip` containing `PrivacyHardeningElevated.exe` and a README. Include this ZIP in installers or release assets.

Notes and recommendations
- Always sign the elevated binary with a code signing certificate before publishing to avoid UAC warnings and to support the service's Authenticode checks.
- If you produce an installer (MSI or MSIX), ensure the elevated helper is installed alongside the UI and that file ACLs and execution policy allow it to be launched by the UI via `runas`.
- Update your installer to place `PrivacyHardeningElevated.exe` in the same folder as the UI so the UI can locate and launch it.
- Consider publishing release assets in GitHub Releases and include the ZIP as a downloadable artifact.
