CI Code-signing for Elevated Helper

This repository includes an optional GitHub Actions workflow that will sign the `PrivacyHardeningElevated.exe` binary and create a release artifact when you push a tag `v*`.

Files
- `.github/workflows/sign-and-release.yml` — builds the solution, runs tests, decodes a PFX certificate from a Base64 secret, signs the elevated helper with `signtool`, verifies the signature, packages the signed EXE into a ZIP, and creates a GitHub release with the ZIP attached.

Required repository secrets
- `SIGNING_PFX_B64` — The code-signing PFX file encoded as Base64. Create this by running:

  - On Windows PowerShell:

    ```powershell
    $bytes = [System.IO.File]::ReadAllBytes('path\to\yourcert.pfx')
    $b64 = [Convert]::ToBase64String($bytes)
    Set-Clipboard $b64
    ```

    Then paste the clipboard contents into the `SIGNING_PFX_B64` secret.

  - On Linux/macOS:

    ```bash
    base64 path/to/yourcert.pfx | pbcopy # or copy the printed output
    ```

- `SIGNING_PFX_PASSWORD` — Password for the PFX file (if any).

Notes and caveats
- The workflow expects `signtool.exe` to be available on the `windows-latest` runner (Windows SDK). If you use a self-hosted runner or different environment, adjust the path to `signtool`.
- The workflow decodes the Base64 secret into `cert.pfx` and uses `signtool sign /fd SHA256 /a /f cert.pfx /p <password> /tr <timestamp>` to sign the EXE.
- For security, avoid storing the raw PFX in the repository. Use the Base64 secret approach or GitHub's encrypted variables.
- If you require EV code signing with hardware tokens, you'll need to integrate a key-custodian service or Azure Key Vault signing step instead of a PFX file.

How to test locally
1. Build the elevated helper locally:

```powershell
dotnet build "src\PrivacyHardeningElevated\PrivacyHardeningElevated.csproj" -c Release
```

2. Sign locally with `signtool`:

```powershell
"C:\Program Files (x86)\Windows Kits\10\bin\x64\signtool.exe" sign /fd SHA256 /f path\to\cert.pfx /p PFX_PASSWORD /tr http://timestamp.digicert.com /td SHA256 path\to\PrivacyHardeningElevated.exe
```

3. Verify:

```powershell
"C:\Program Files (x86)\Windows Kits\10\bin\x64\signtool.exe" verify /pa path\to\PrivacyHardeningElevated.exe
```

If you'd like, I can also add a fallback Linux-based signing path (osslsigncode) or modify the existing release workflow to include signing. Tell me if you want those changes.
