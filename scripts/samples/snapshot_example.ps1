# Snapshot example: capture a registry value and output JSON
$ErrorActionPreference = 'Stop'

$sampleKey = 'HKLM:\SOFTWARE\PrivacyHardeningFramework\Samples'
$valueName = 'ExampleSetting'

$value = $null
try {
    $prop = Get-ItemProperty -Path $sampleKey -ErrorAction Stop
    $value = $prop.$valueName
} catch {
    # Key or value does not exist
    $value = $null
}

$result = [PSCustomObject]@{
    Captured = $value
    Source = $sampleKey
    ValueName = $valueName
    Timestamp = (Get-Date).ToString('o')
}

# Output compressed JSON for the PowerShellExecutor to capture
$result | ConvertTo-Json -Compress
exit 0
