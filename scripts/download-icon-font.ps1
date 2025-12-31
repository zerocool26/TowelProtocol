param(
    [string]$OutputDir = "src\PrivacyHardeningUI\Assets\Fonts",
    [string]$Url = "https://raw.githubusercontent.com/google/material-design-icons/master/font/MaterialIconsOutlined-Regular.otf",
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

$filename = Split-Path $Url -Leaf
$dest = Join-Path $OutputDir $filename

if ((Test-Path $dest) -and (-not $Force)) {
    Write-Host "Font already exists at $dest. Use -Force to overwrite." -ForegroundColor Yellow
    exit 0
}

Write-Host "Downloading font from $Url to $dest..." -ForegroundColor Green
try {
    Invoke-WebRequest -Uri $Url -OutFile $dest -UseBasicParsing
    Write-Host "Download complete." -ForegroundColor Green
    Write-Host "You can now rebuild the solution: dotnet build \"PrivacyHardeningFramework.sln\" -c Release" -ForegroundColor Gray
} catch {
    Write-Host "ERROR: Failed to download font: $_" -ForegroundColor Red
    exit 1
}
