Advanced developer prompt — orchestrate end-to-end release and quality tasks

Context (short):
- Repository: PrivacyHardeningFramework (Windows privacy hardening toolkit)
- Stack: C#, .NET 8 (net8.0-windows10.0.22621.0), Avalonia UI, Windows Service, Named-pipe IPC
- Key artifacts: `src/PrivacyHardeningElevated/PrivacyHardeningElevated.exe`, `dist/PrivacyHardeningElevated.zip`
- Current state: local git repository initialized, CI + CodeQL workflows and packaging scripts present, sample scripts and installer prototype added, build passes.

Objectives (ordered):
1. Automate release creation when a Git tag is pushed: build solution, run packaging script, create a draft GitHub release, and attach `dist/PrivacyHardeningElevated.zip` as an asset.
2. Ensure release workflow uses `GITHUB_TOKEN` and supports optional code-signing steps (placeholder steps if secrets are provided).
3. Provide clear verification steps and logging so humans can validate the release artifact.
4. Update repo artifacts and docs to mention the release workflow and how to trigger it.

Constraints and safety:
- Do not use private secrets inline; refer to `secrets.CODE_SIGN_CERT` and `secrets.CODE_SIGN_PASSWORD` if present and only use them when they exist.
- Non-destructive changes only unless explicit tests exist (no system changes triggered by CI).
- Keep Windows-specific steps running on `windows-latest` runner.

Expected outputs and files to create or update:
- `.github/workflows/release-on-tag.yml` — workflow that triggers on tag push, builds, packages, creates draft release, uploads `dist/PrivacyHardeningElevated.zip`.
- `docs/release.md` — short doc describing how to tag and trigger the release and how to supply code-signing secrets.
- Commit with a concise message and update the project's todo list accordingly.

Step-by-step actionable plan the agent should execute now:
1. Add the release GitHub Actions workflow file with these high-level steps:
   - on: push: tags: ['v*']
   - checkout, setup dotnet 8
   - build the solution (Release)
   - run `scripts/package_elevated_helper.ps1 -Configuration Release`
   - if `secrets.CODE_SIGN_CERT` provided, run a placeholder `signtool` step using the secret (documented only)
   - create a draft GitHub release with the tag name and push logs as body
   - upload `dist/PrivacyHardeningElevated.zip` as a release asset
2. Create `docs/release.md` documenting how to create a tag, required secrets, and how to inspect the release artifacts.
3. Commit files and run a local git commit.
4. Update the todo list: add/mark a task for release automation completed.

Verification the agent must perform locally:
- Ensure the workflow YAML is syntactically valid (best-effort by linting YAML structure)
- Ensure `scripts/package_elevated_helper.ps1` exists and `dist/PrivacyHardeningElevated.zip` is produced locally by running the packaging script (for local verification only)
- Run `dotnet build` locally as final sanity check (already passing) and note success

Notes for a human maintainer reading the release:
- The workflow uses GitHub's `GITHUB_TOKEN` to create the release; it will create a draft release named after the pushed tag (e.g., v0.1.0).
- Signing is optional and requires adding secrets to the GitHub repository; instructions are included in `docs/release.md`.

Deliverable summary (what to commit):
- `.github/workflows/release-on-tag.yml`
- `docs/release.md`
- Update to `README.md` or `docs` if necessary to reference release docs
- Git commit with message: `ci: add release-on-tag workflow and release docs`

Now run these steps and report back with the exact files created and the git commit hash.

Design system & visual assets
--------------------------------

See `docs/design.md` for the UI design system, token locations, fonts, icon assets, and steps to update or add new visual resources (colors, typography, spacing, and themes).

Key locations:
- `src/PrivacyHardeningUI/Styles/` — existing theme and control styles (`ThemeResources.Light.axaml`, `ThemeResources.Dark.axaml`, `ControlStyles.axaml`).
- `src/PrivacyHardeningUI/Assets/Fonts/` — bundled fonts (use `scripts/download-inter-fonts.ps1` and `scripts/download-icon-font.ps1` to populate).
- `src/PrivacyHardeningUI/Assets/Icons/` — SVG icons (create if missing) and icon font fallbacks.

Recommended quick steps to enable updated visuals locally:
1. Run the helper scripts to fetch recommended fonts:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\download-inter-fonts.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\download-icon-font.ps1
```

2. Rebuild the solution and run the UI to verify fonts and theme:

```powershell
dotnet build "PrivacyHardeningFramework.sln" -c Release
dotnet run --project src\PrivacyHardeningUI -c Release
```

3. See `docs/design.md` for how to add tokens, update themes, and include icons in XAML controls.
