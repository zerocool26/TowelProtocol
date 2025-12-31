Installer packaging (prototype)

This project includes a simple installer prototype using Inno Setup. It is intended as a starting point — customize and sign your installer before distribution.

Prerequisites
- Inno Setup Compiler (https://jrsoftware.org/isinfo.php)

How to build the prototype installer
1. Build the solution in Release:

```powershell
dotnet build "PrivacyHardeningFramework.sln" -c Release
```

2. Run the packaging script to create the elevated helper ZIP (optional, included in the installer):

```powershell
.\scripts\package_elevated_helper.ps1 -Configuration Release
```

3. Open `installers/innosetup/PrivacyHardeningInstaller.iss` in Inno Setup and click Compile.

Notes
- The Inno script uses `{#SourceDir}` which defaults to the current working directory. Adjust the `Source:` paths inside the `[Files]` section if your build outputs are elsewhere.
- Sign the produced installer EXE with a code-signing certificate prior to release.
- For MSIX packaging, consider the MSIX Packaging Tool or `makeappx`/`signtool` workflows; MSIX is recommended for modern Windows deployments.
