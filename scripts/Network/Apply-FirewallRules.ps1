# Version: 1.0.0
# Purpose: Block known telemetry endpoints via Windows Firewall
# Mechanism: NetSecurity module (supported API)
# Reversibility: Remove-NetFirewallRule -DisplayName "PHF_Telemetry_Block*"

<#
.SYNOPSIS
    Blocks outbound connections to documented Windows telemetry endpoints.
.DESCRIPTION
    Creates Windows Firewall rules blocking Microsoft telemetry hostnames.
    Uses NetSecurity module (supported API). Does NOT modify hosts file.
    All rules are tagged for easy identification and removal.
.PARAMETER RulePrefix
    Prefix for firewall rule names. Default: "PHF_Telemetry_Block"
.PARAMETER LogPath
    Path to write execution log.
.NOTES
    Execution: Service only, constrained language mode
    Reversibility: Remove-NetFirewallRule -DisplayName "$RulePrefix*"
.EXAMPLE
    .\Apply-FirewallRules.ps1 -LogPath "C:\ProgramData\PrivacyHardeningFramework\logs\firewall.log"
#>

#Requires -Version 5.1
#Requires -RunAsAdministrator
#Requires -Modules NetSecurity

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$RulePrefix = "PHF_Telemetry_Block",

    [Parameter(Mandatory = $true)]
    [string]$LogPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Telemetry endpoints sourced from Microsoft documentation
# Last updated: 2024-12-30
# Reference: https://learn.microsoft.com/en-us/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services
$TelemetryEndpoints = @(
    "vortex.data.microsoft.com",
    "vortex-win.data.microsoft.com",
    "telecommand.telemetry.microsoft.com",
    "oca.telemetry.microsoft.com",
    "watson.telemetry.microsoft.com",
    "statsfe2.ws.microsoft.com",
    "statsfe2-bg.ws.microsoft.com",
    "ceuswatcab01.blob.core.windows.net",
    "ceuswatcab02.blob.core.windows.net",
    "eaus2watcab01.blob.core.windows.net",
    "eaus2watcab02.blob.core.windows.net",
    "weus2watcab01.blob.core.windows.net",
    "weus2watcab02.blob.core.windows.net",
    "kmwatson.events.data.microsoft.com",
    "kmwatsonc.events.data.microsoft.com",
    "oca.microsoft.com",
    "sqm.telemetry.microsoft.com"
)

function Write-Log {
    param([string]$Message)
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logEntry = "[$timestamp] $Message"
    Add-Content -Path $LogPath -Value $logEntry
    Write-Verbose $logEntry
}

try {
    Write-Log "Starting firewall rule application (v1.0.0)"
    Write-Log "Rule prefix: $RulePrefix"
    Write-Log "Total endpoints to block: $($TelemetryEndpoints.Count)"

    $appliedCount = 0
    $skippedCount = 0

    foreach ($endpoint in $TelemetryEndpoints) {
        $ruleName = "$RulePrefix`_$endpoint"

        # Check if rule already exists
        $existing = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
        if ($existing) {
            Write-Log "Rule already exists (skipping): $ruleName"
            $skippedCount++
            continue
        }

        # Create outbound block rule
        $null = New-NetFirewallRule `
            -DisplayName $ruleName `
            -Description "Blocks telemetry endpoint: $endpoint (PrivacyHardeningFramework v1.0)" `
            -Direction Outbound `
            -Action Block `
            -RemoteAddress $endpoint `
            -Protocol Any `
            -Enabled True `
            -Profile Any `
            -Group "PrivacyHardeningFramework"

        Write-Log "Created rule: $ruleName"
        $appliedCount++
    }

    Write-Log "Completed successfully. Applied: $appliedCount, Skipped: $skippedCount"

    # Return structured result for service parsing
    return @{
        Success      = $true
        AppliedCount = $appliedCount
        SkippedCount = $skippedCount
        TotalRules   = $TelemetryEndpoints.Count
    }
}
catch {
    $errorMessage = "ERROR: $($_.Exception.Message)"
    Write-Log $errorMessage
    Write-Error $errorMessage
    throw
}

# NOTE: This script should be Authenticode-signed before deployment
# Sign with: Set-AuthenticodeSignature -FilePath .\Apply-FirewallRules.ps1 -Certificate $cert
