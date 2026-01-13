<#
  DotNetSafe.ps1

  Purpose:
  - Run dotnet commands with “safe” environment defaults that avoid common hangs:
    * disables build servers
    * skips first-time experience / telemetry
    * uses repo-local NuGet caches
    * honors repo-local NuGet.config (nuget.org only by default)

  Usage examples:
    .\DotNetSafe.ps1 --info
    .\DotNetSafe.ps1 restore PrivacyHardeningFramework.sln
    .\DotNetSafe.ps1 build PrivacyHardeningFramework.sln -c Release
    .\DotNetSafe.ps1 run --project src\PrivacyHardeningUI\PrivacyHardeningUI.csproj

  Wrapper options (optional):
    .\DotNetSafe.ps1 -TimeoutSeconds 30 --info
    .\DotNetSafe.ps1 -NoTimeout build PrivacyHardeningFramework.sln
#>

# NOTE:
# We intentionally avoid a PowerShell param() block here.
# In Windows PowerShell 5.1, dotnet-style arguments like "--info" are treated as PowerShell parameters
# (and can even ambiguously match common parameters such as -InformationAction when using CmdletBinding).
# By parsing $args manually, we can reliably pass through *any* dotnet arguments, including those starting
# with "--", while still supporting wrapper options like -TimeoutSeconds / -NoTimeout.

$TimeoutSeconds = 900
$NoTimeout = $false
$DotNetArgs = New-Object System.Collections.Generic.List[string]

for ($i = 0; $i -lt $args.Count; $i++) {
    $a = [string]$args[$i]

    # Wrapper options (case-insensitive)
    if ($a -ieq '-NoTimeout' -or $a -ieq '--no-timeout') {
        $NoTimeout = $true
        continue
    }

    if ($a -ieq '-TimeoutSeconds' -or $a -ieq '--timeout-seconds') {
        if ($i + 1 -ge $args.Count) {
            Write-Error "Missing value after ${a}"
            exit 2
        }
        $i++
        try {
            $TimeoutSeconds = [int]$args[$i]
        } catch {
            Write-Error "Invalid integer for ${a}: '$($args[$i])'"
            exit 2
        }
        continue
    }

    if ($a -match '^(--timeout-seconds)=(.+)$') {
        try {
            $TimeoutSeconds = [int]$Matches[2]
        } catch {
            Write-Error "Invalid integer for --timeout-seconds: '$($Matches[2])'"
            exit 2
        }
        continue
    }

    [void]$DotNetArgs.Add($a)
}

$common = Join-Path $PSScriptRoot 'scripts\DotNetSafe.Common.ps1'
if (-not (Test-Path -LiteralPath $common)) {
    Write-Error "Missing shared helper: $common"
    exit 5
}

. $common

$repoRoot = $PSScriptRoot

if (-not $DotNetArgs -or $DotNetArgs.Count -eq 0) {
    Write-Host "DotNetSafe.ps1: no arguments provided. Example: .\DotNetSafe.ps1 --info" -ForegroundColor Yellow
    exit 2
}

try {
    $result = Invoke-DotNetSafe -RepoRoot $repoRoot -DotNetArgs $DotNetArgs.ToArray() -TimeoutSeconds $TimeoutSeconds -NoTimeout:$NoTimeout -LogPrefix 'dotnet_safe'

    if ($result.TimedOut) {
        Write-Host "ERROR: dotnet timed out after ${TimeoutSeconds}s and was terminated." -ForegroundColor Red
        Write-Host "  Stdout: $($result.Stdout)" -ForegroundColor Yellow
        Write-Host "  Stderr: $($result.Stderr)" -ForegroundColor Yellow
        exit 124
    }

    if ($null -eq $result.ExitCode) {
        $detail = if ($result.ExitCodeError) { " ($($result.ExitCodeError))" } else { "" }
        Write-Host "ERROR: dotnet exited but an exit code could not be determined.$detail" -ForegroundColor Red
        Write-Host "  Stdout: $($result.Stdout)" -ForegroundColor Yellow
        Write-Host "  Stderr: $($result.Stderr)" -ForegroundColor Yellow
        exit 125
    }

    if ($result.ExitCode -ne 0) {
        Write-Host "dotnet exited with code $($result.ExitCode)." -ForegroundColor Red
        Write-Host "  Stdout: $($result.Stdout)" -ForegroundColor Yellow
        Write-Host "  Stderr: $($result.Stderr)" -ForegroundColor Yellow
    }

    exit $result.ExitCode
} catch {
    Write-Error $_
    exit 4
}
