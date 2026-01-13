<#
  Diagnose-Environment.ps1

  Non-invasive diagnostics for "commands are stuck" situations.
  This script does NOT call dotnet.

  It checks:
  - where dotnet.exe would be resolved from
  - whether repo-local NuGet caches are writable
  - whether there are leftover WinUI files that could confuse the build
  - whether NuGet sources are overridden by machine/user configs

  Output is written to .\build_logs\diagnose_environment_<timestamp>.log
#>

[CmdletBinding()]
param()

$repoRoot = $PSScriptRoot
$logDir = Join-Path $repoRoot 'build_logs'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$stamp = (Get-Date).ToString('yyyyMMdd_HHmmss')
$logPath = Join-Path $logDir "diagnose_environment_${stamp}.log"

function Write-Log {
    param([string]$Message)
    $line = "[{0}] {1}" -f (Get-Date).ToString('s'), $Message
    $line | Tee-Object -FilePath $logPath -Append
}

Write-Log "RepoRoot: $repoRoot"
Write-Log "PSVersion: $($PSVersionTable.PSVersion)"
Write-Log "OS: $([System.Environment]::OSVersion.VersionString)"
Write-Log "User: $env:USERNAME"
Write-Log "PWD: $PWD"

# dotnet resolution (without running it)
$cmd = Get-Command dotnet -ErrorAction SilentlyContinue
if ($cmd) {
    Write-Log "dotnet command type: $($cmd.CommandType)"
    Write-Log "dotnet source: $($cmd.Source)"
} else {
    Write-Log "dotnet not found via Get-Command (PATH resolution)."
}

$candidates = @()
if ($env:DOTNET_ROOT) { $candidates += (Join-Path $env:DOTNET_ROOT 'dotnet.exe') }
$candidates += 'C:\Program Files\dotnet\dotnet.exe'
$candidates += 'C:\Program Files (x86)\dotnet\dotnet.exe'
foreach ($p in $candidates) {
    Write-Log ("dotnet candidate: {0} exists={1}" -f $p, (Test-Path -LiteralPath $p))
}

# NuGet config locations
$nugetUser = Join-Path $env:APPDATA 'NuGet\NuGet.Config'
$nugetMachine = Join-Path ${env:ProgramFiles(x86)} 'NuGet\Config\NuGet.Config'
$nugetRepo = Join-Path $repoRoot 'NuGet.config'
Write-Log "NuGet (repo): $nugetRepo exists=$([bool](Test-Path -LiteralPath $nugetRepo))"
Write-Log "NuGet (user): $nugetUser exists=$([bool](Test-Path -LiteralPath $nugetUser))"
Write-Log "NuGet (machine): $nugetMachine exists=$([bool](Test-Path -LiteralPath $nugetMachine))"

# Repo-local cache writability
$nugetRoot = Join-Path $repoRoot '.nuget'
$nugetPackages = Join-Path $nugetRoot 'packages'
$nugetHttp = Join-Path $nugetRoot 'http-cache'
$nugetPlugins = Join-Path $nugetRoot 'plugins-cache'
$paths = @($nugetRoot, $nugetPackages, $nugetHttp, $nugetPlugins)
foreach ($p in $paths) {
    try {
        New-Item -ItemType Directory -Path $p -Force | Out-Null
        $testFile = Join-Path $p ("write_test_{0}.tmp" -f [Guid]::NewGuid().ToString('N'))
        Set-Content -LiteralPath $testFile -Value 'ok' -ErrorAction Stop
        Remove-Item -LiteralPath $testFile -Force -ErrorAction SilentlyContinue
        Write-Log "Cache path writable: $p"
    } catch {
        Write-Log "Cache path NOT writable: $p :: $($_.Exception.Message)"
    }
}

# Leftover WinUI files
$winuiXaml = Get-ChildItem -Path (Join-Path $repoRoot 'src\PrivacyHardeningUI\Views') -Filter '*.xaml' -ErrorAction SilentlyContinue
$winuiXamlCs = Get-ChildItem -Path (Join-Path $repoRoot 'src\PrivacyHardeningUI\Views') -Filter '*.xaml.cs' -ErrorAction SilentlyContinue
Write-Log ("WinUI leftovers: Views\\*.xaml count={0}" -f ($winuiXaml | Measure-Object).Count)
Write-Log ("WinUI leftovers: Views\\*.xaml.cs count={0}" -f ($winuiXamlCs | Measure-Object).Count)

Write-Log "Done. Log: $logPath"
