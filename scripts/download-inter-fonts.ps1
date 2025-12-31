Param()

$outDir = Join-Path -Path $PSScriptRoot -ChildPath "..\src\PrivacyHardeningUI\Assets\Fonts"
$outDir = Resolve-Path -Path $outDir -ErrorAction SilentlyContinue | ForEach-Object { $_.ProviderPath } 
if (-not $outDir) {
    $outDir = Join-Path -Path $PSScriptRoot -ChildPath "..\src\PrivacyHardeningUI\Assets\Fonts"
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

$files = @(
    @{ url = 'https://github.com/google/fonts/raw/main/ofl/inter/Inter-Regular.ttf'; name = 'Inter-Regular.ttf' },
    @{ url = 'https://github.com/google/fonts/raw/main/ofl/inter/Inter-Bold.ttf'; name = 'Inter-Bold.ttf' }
)

foreach ($f in $files) {
    $dest = Join-Path $outDir $($f.name)
    if (Test-Path $dest) {
        Write-Host "Skipping existing: $($f.name)"
        continue
    }

    Write-Host "Downloading $($f.name) ..."
    try {
        Invoke-WebRequest -Uri $f.url -OutFile $dest -UseBasicParsing -ErrorAction Stop
        Write-Host "Saved to $dest"
    }
    catch {
        Write-Warning "Failed to download $($f.url): $_"
    }
}

Write-Host "Done. Ensure fonts are added to project resources under Assets/Fonts/."
