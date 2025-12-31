# Release workflow — Creating a draft release from a tag

This document explains how to trigger the automated release workflow and optional signing behavior.

Trigger
- Push a Git tag matching `v*` (for example `v1.0.0`) to the repository. The workflow `release-on-tag.yml` will run on `windows-latest`.

What the workflow does
- Restores and builds the solution (`dotnet restore` + `dotnet build -c Release`).
- Runs `scripts/package_elevated_helper.ps1 -Configuration Release` to produce `dist/PrivacyHardeningElevated.zip`.
- If `secrets.CODE_SIGN_CERT` and `secrets.CODE_SIGN_PASSWORD` are present, a placeholder signing step runs (replace with real signing steps if needed).
- Creates a DRAFT GitHub release named for the tag and uploads `PrivacyHardeningElevated.zip` as an asset.

Required repository files
- `.github/workflows/release-on-tag.yml` (workflow)
- `scripts/package_elevated_helper.ps1` (packaging script that produces `dist/PrivacyHardeningElevated.zip`)

Optional secrets
- `CODE_SIGN_CERT` — Base64 PFX (if you want the workflow to perform code signing).
- `CODE_SIGN_PASSWORD` — Password for the PFX.

Notes for maintainers
- The workflow uses the provided `GITHUB_TOKEN` to create a draft release. After the draft is created, review and publish the release manually to attach release notes or additional assets.
- Replace the placeholder signing step with a secure signing step using `signtool.exe` or Azure Key Vault signing if required.

Local verification steps
1. Run build locally:

```powershell
dotnet restore "PrivacyHardeningFramework.sln"
dotnet build "PrivacyHardeningFramework.sln" -c Release
```

2. Run the packaging script locally to verify it produces the artifact:

```powershell
.\scripts\package_elevated_helper.ps1 -Configuration Release
# confirm file exists
Test-Path .\dist\PrivacyHardeningElevated.zip
```

3. Create a local test tag and push it (if you want to trigger the workflow in CI):

```powershell
git tag v0.0.0-test
git push origin v0.0.0-test
```

Troubleshooting
- If `dist/PrivacyHardeningElevated.zip` is not produced, inspect the packaging script output and ensure any required build artifacts exist.
- If release creation fails, ensure `GITHUB_TOKEN` is available to workflows (default in GitHub Actions) and the repository permissions allow workflow to create releases.

Contact
- For questions about the packaging script or signing steps, open an issue in the repository with `release` label.
Release automation and tagging

This document explains how releases are created automatically when you push tags matching `v*`.

Triggering a release

1. Create and push a tag locally:

```powershell
# Create an annotated tag
git tag -a v0.1.0 -m "Release v0.1.0"
# Push tag to origin
git push origin v0.1.0
```

2. The GitHub Actions workflow (`.github/workflows/release-on-tag.yml`) will run on `windows-latest`, build the solution, run the packaging script to produce `dist/PrivacyHardeningElevated.zip`, and create a draft release named after the tag.

Code-signing (optional)

If you want the workflow to sign the elevated helper (recommended), add these repository secrets:
- `CODE_SIGN_CERT` — base64-encoded PFX certificate (or your preferred secure storage)
- `CODE_SIGN_PASSWORD` — PFX password

The current workflow includes a placeholder `Optional code-sign (placeholder)` step; replace it with a `signtool` invocation that uses the certificate and password. Be sure to protect your secrets and rotate certificates periodically.

Verifying the release

- After the workflow completes, inspect the Draft Release in GitHub Releases for the tag; confirm `PrivacyHardeningElevated.zip` is attached.
- Download the ZIP and validate the binary and README inside.

Notes

- The workflow uses `softprops/action-gh-release` and `softprops/action-upload-release-asset` to create draft releases and upload assets.
- If you prefer, modify the workflow to publish the release automatically (change `draft` flag usage) or to create GitHub Release notes using the changelog or other automation.
