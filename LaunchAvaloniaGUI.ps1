# Privacy Hardening Framework - Avalonia UI Launcher
# Full-featured GUI with policy selection, audit, and diff views

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " Privacy Hardening Framework - Avalonia GUI" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""

$projectPath = Join-Path $PSScriptRoot "src\PrivacyHardeningUI\PrivacyHardeningUI.csproj"

if (-not (Test-Path $projectPath)) {
    Write-Host "ERROR: Project file not found at: $projectPath" -ForegroundColor Red
    pause
    exit 1
}

Write-Host "Launching Avalonia GUI..." -ForegroundColor Green
Write-Host "  - Full policy selection interface" -ForegroundColor Gray
Write-Host "  - Individual checkbox for each policy" -ForegroundColor Gray
Write-Host "  - Audit and diff views" -ForegroundColor Gray
Write-Host "  - 52+ policies loaded" -ForegroundColor Gray
Write-Host ""
Write-Host "Press Ctrl+C in this window to close the GUI" -ForegroundColor Yellow
Write-Host ""

# Launch the Avalonia application
dotnet run --project $projectPath
