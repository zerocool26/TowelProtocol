PrivacyHardening Integration Runner

This small runner launches the `PrivacyHardeningElevated.exe` helper with UAC to allow manual verification of the elevated flow.

How to use:

1. Build the solution in Release:

```powershell
dotnet build "PrivacyHardeningFramework.sln" -c Release
```

2. Start the service (PrivacyHardeningService) if not already running.

3. Run this runner (it will prompt for UAC):

```powershell
dotnet run --project tests\PrivacyHardeningIntegration -c Release
```

Notes:
- The runner is intentionally manual because UAC prompts cannot be automated reliably in CI.
- On success the elevated helper will run; check its exit code and service logs for verification.
- Modify the runner's arguments if you want to call specific actions in the elevated helper.
