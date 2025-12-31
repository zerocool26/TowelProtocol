param(
    [string]$PreviousState
)

if (-not $PreviousState) {
    Write-Error "PreviousState parameter missing"
    exit 2
}

try {
    $data = $PreviousState | ConvertFrom-Json
    $progData = [Environment]::GetFolderPath('CommonApplicationData')
    $outDir = Join-Path $progData 'PrivacyHardeningFramework\reverts'
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    $outFile = Join-Path $outDir ("revert_$(Get-Date -Format 'yyyyMMddHHmmss').json")
    $data | ConvertTo-Json -Compress | Out-File -FilePath $outFile -Encoding utf8
    Write-Output "Reverted (simulated). Logged to $outFile"
    exit 0
} catch {
    Write-Error $_.Exception.Message
    exit 1
}
