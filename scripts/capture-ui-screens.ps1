param(
    [string]$ProjectPath = "src\PrivacyHardeningUI\PrivacyHardeningUI.csproj",
    [int]$DelaySeconds = 4,
    [switch]$NoRun
)

$ErrorActionPreference = 'Stop'

$projectFull = Resolve-Path -Path $ProjectPath -ErrorAction SilentlyContinue
if (-not $projectFull) {
    Write-Host "ERROR: Project file not found: $ProjectPath" -ForegroundColor Red
    exit 2
}
$projectFull = $projectFull.Path
$projectDir = Split-Path $projectFull -Parent

Write-Host "Building UI project..." -ForegroundColor Gray
& dotnet build "$projectFull" -c Release | Write-Host

# Ensure screenshots folder exists
$screensDir = Join-Path $PSScriptRoot "..\docs\screenshots" | Resolve-Path -ErrorAction SilentlyContinue | ForEach-Object { $_.ProviderPath }
if (-not $screensDir) { $screensDir = Join-Path $PSScriptRoot "..\docs\screenshots"; New-Item -Path $screensDir -ItemType Directory -Force | Out-Null }

if ($NoRun) {
    Write-Host "Dry run complete; built project and ensured screenshots folder: $screensDir" -ForegroundColor Green
    exit 0
}

# Start UI in background
Write-Host "Launching UI in background..." -ForegroundColor Gray
$psi = Start-Process -FilePath "dotnet" -ArgumentList "run --project `"$projectFull`" -c Release" -WorkingDirectory $projectDir -PassThru

Write-Host "Waiting $DelaySeconds seconds for UI to render..." -ForegroundColor Gray
Start-Sleep -Seconds $DelaySeconds

# Capture primary screen
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($bounds.X, $bounds.Y, 0, 0, $bitmap.Size)

$timestamp = Get-Date -Format yyyyMMdd-HHmmss
$outPath = Join-Path $screensDir "screenshot-$timestamp.png"
$bitmap.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)

$graphics.Dispose()
$bitmap.Dispose()

Write-Host "Saved screenshot to: $outPath" -ForegroundColor Green

# Terminate the started process if still running
try {
    if ($psi -and -not $psi.HasExited) {
        Write-Host "Stopping UI process (Id: $($psi.Id))..." -ForegroundColor Gray
        Stop-Process -Id $psi.Id -Force
    }
} catch {
    Write-Host "Warning: failed to stop UI process: $_" -ForegroundColor Yellow
}
