# Privacy Hardening Framework - Avalonia UI Launcher
# Thin wrapper around LaunchGUI.ps1 (keeps all dotnet hardening in one place)

param(
    [switch]$NoBuild,
    [switch]$Background,
    [switch]$Help
)

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " Privacy Hardening Framework - Avalonia GUI" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""

$launcher = Join-Path $PSScriptRoot 'LaunchGUI.ps1'
if (-not (Test-Path -LiteralPath $launcher)) {
    Write-Host "ERROR: Launcher not found: $launcher" -ForegroundColor Red
    exit 1
}

$args = @()
if ($NoBuild) { $args += '-NoBuild' }
if ($Background) { $args += '-Background' }
if ($Help) { $args += '-Help' }

& $launcher @args
exit $LASTEXITCODE
