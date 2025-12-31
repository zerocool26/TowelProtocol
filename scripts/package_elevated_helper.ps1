# Package PrivacyHardeningElevated for distribution
# Usage: Run from repo root in PowerShell:
#   .\scripts\package_elevated_helper.ps1 -Configuration Release
param(
    [string]$Configuration = 'Release'
)

$solutionRoot = (Get-Location).Path
$targetFramework = 'net8.0-windows10.0.22621.0'
$projectRel = 'src\PrivacyHardeningElevated'
$buildDir = Join-Path $solutionRoot ($projectRel + "\bin\" + $Configuration + "\" + $targetFramework)
$exeName = 'PrivacyHardeningElevated.exe'

if (!(Test-Path $buildDir)) {
    Write-Host "Build output not found at $buildDir — building project..."
    dotnet build "$solutionRoot\PrivacyHardeningFramework.sln" -c $Configuration | Out-Host
}

$exePath = Join-Path -Path $buildDir -ChildPath $exeName
if (!(Test-Path $exePath)) {
    Write-Error "Could not find $exeName at $exePath. Ensure build succeeded."; exit 1
}

$distDir = Join-Path -Path $solutionRoot -ChildPath 'dist\PrivacyHardeningElevated'
if (Test-Path $distDir) { Remove-Item -Recurse -Force $distDir }
New-Item -ItemType Directory -Path $distDir | Out-Null

Copy-Item -Path $exePath -Destination $distDir

# Include a minimal README alongside the binary
$readmeSrc = Join-Path -Path $solutionRoot -ChildPath 'src\PrivacyHardeningElevated\README.md'
if (Test-Path $readmeSrc) { Copy-Item $readmeSrc -Destination $distDir }
else {
    "PrivacyHardeningElevated helper. Launch with elevated privileges to perform privileged actions." | Out-File -FilePath (Join-Path $distDir 'README.txt') -Encoding UTF8
}

# Create zip
$zipPath = Join-Path -Path $solutionRoot -ChildPath 'dist\PrivacyHardeningElevated.zip'
if (Test-Path $zipPath) { Remove-Item $zipPath }
Compress-Archive -Path (Join-Path $distDir '*') -DestinationPath $zipPath

Write-Host "Packaged elevated helper to: $zipPath"
Write-Host "Contents:"; Get-ChildItem -Path $distDir | ForEach-Object { Write-Host " - $($_.Name)" }
# Package PrivacyHardeningElevated for distribution
# Usage: Run from repo root in PowerShell:
#   .\scripts\package_elevated_helper.ps1 -Configuration Release
param(
    [string]$Configuration = 'Release'
)

$solutionRoot = (Get-Location).Path
$targetFramework = 'net8.0-windows10.0.22621.0'
$projectRel = Join-Path -Path 'src' -ChildPath 'PrivacyHardeningElevated'
$buildDir = Join-Path -Path $solutionRoot -ChildPath "${projectRel}\bin\$Configuration\$targetFramework"
$exeName = 'PrivacyHardeningElevated.exe'

if (!(Test-Path $buildDir)) {
    Write-Host "Build output not found at $buildDir — building project..."
    dotnet build "$solutionRoot\PrivacyHardeningFramework.sln" -c $Configuration | Out-Host
}

$exePath = Join-Path -Path $buildDir -ChildPath $exeName
if (!(Test-Path $exePath)) {
    Write-Error "Could not find $exeName at $exePath. Ensure build succeeded."; exit 1
}

$distDir = Join-Path -Path $solutionRoot -ChildPath 'dist\PrivacyHardeningElevated'
if (Test-Path $distDir) { Remove-Item -Recurse -Force $distDir }
New-Item -ItemType Directory -Path $distDir | Out-Null

Copy-Item -Path $exePath -Destination $distDir

# Include a minimal README alongside the binary
$readmeSrc = Join-Path -Path $solutionRoot -ChildPath 'src\PrivacyHardeningElevated\README.md'
if (Test-Path $readmeSrc) { Copy-Item $readmeSrc -Destination $distDir }
else {
    "PrivacyHardeningElevated helper. Launch with elevated privileges to perform privileged actions." | Out-File -FilePath (Join-Path $distDir 'README.txt') -Encoding UTF8
}

# Create zip
$zipPath = Join-Path -Path $solutionRoot -ChildPath 'dist\PrivacyHardeningElevated.zip'
if (Test-Path $zipPath) { Remove-Item $zipPath }
Compress-Archive -Path (Join-Path $distDir '*') -DestinationPath $zipPath

Write-Host "Packaged elevated helper to: $zipPath"
Write-Host "Contents:"; Get-ChildItem -Path $distDir | ForEach-Object { Write-Host " - $($_.Name)" }
