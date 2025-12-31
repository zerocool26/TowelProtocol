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
