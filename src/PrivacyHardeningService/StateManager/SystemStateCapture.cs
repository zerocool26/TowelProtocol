using System.DirectoryServices.ActiveDirectory;
using System.Management;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PrivacyHardeningContracts.Responses;

namespace PrivacyHardeningService.StateManager;

/// <summary>
/// Captures current system state for snapshots and auditing
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SystemStateCapture
{
    private readonly ILogger<SystemStateCapture> _logger;

    public SystemStateCapture(ILogger<SystemStateCapture> logger)
    {
        _logger = logger;
    }

    public async Task<SystemInfo> GetSystemInfoAsync(CancellationToken cancellationToken)
    {
        var windowsBuild = await GetWindowsBuildAsync(cancellationToken);
        var windowsVersion = Environment.OSVersion.VersionString;
        var windowsSku = await GetWindowsSkuAsync(cancellationToken);
        var isDomainJoined = IsDomainJoined();
        var isMDMManaged = await IsMDMManagedAsync(cancellationToken);
        var tamperProtectionEnabled = await IsDefenderTamperProtectionEnabledAsync(cancellationToken);

        return new SystemInfo
        {
            WindowsBuild = windowsBuild,
            WindowsVersion = windowsVersion,
            WindowsSku = windowsSku,
            IsDomainJoined = isDomainJoined,
            IsMDMManaged = isMDMManaged,
            DefenderTamperProtectionEnabled = tamperProtectionEnabled
        };
    }

    private async Task<int> GetWindowsBuildAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key != null)
            {
                var buildNumber = key.GetValue("CurrentBuildNumber") as string;
                if (int.TryParse(buildNumber, out var build))
                {
                    return build;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read Windows build from registry");
        }

        await Task.CompletedTask;
        return Environment.OSVersion.Version.Build;
    }

    private async Task<string> GetWindowsSkuAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT OperatingSystemSKU FROM Win32_OperatingSystem");
            var results = await Task.Run(() => searcher.Get(), cancellationToken);

            foreach (ManagementObject obj in results)
            {
                var sku = (uint)obj["OperatingSystemSKU"];
                return MapSkuToName(sku);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect Windows SKU via WMI");
        }

        return "Unknown";
    }

    private string MapSkuToName(uint sku)
    {
        // Windows 11 SKU values from GetProductInfo API
        return sku switch
        {
            4 => "Enterprise",
            27 => "Enterprise N",
            28 => "Enterprise Server",
            48 => "Professional",
            49 => "Professional N",
            98 => "Home N",
            101 => "Home",
            121 => "Education",
            122 => "Education N",
            125 => "Enterprise S",
            162 => "Professional Education",
            163 => "Professional Education N",
            164 => "Professional Workstation",
            165 => "Professional Workstation N",
            175 => "Enterprise G",
            176 => "Enterprise G N",
            _ => $"SKU_{sku}"
        };
    }

    private bool IsDomainJoined()
    {
        try
        {
            // Method 1: Check via registry
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters");
            var domain = key?.GetValue("Domain") as string;

            if (!string.IsNullOrEmpty(domain))
            {
                return true;
            }

            // Method 2: Try to get domain via ActiveDirectory
            try
            {
                var domain2 = Domain.GetComputerDomain();
                return domain2 != null;
            }
            catch (ActiveDirectoryObjectNotFoundException)
            {
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to determine domain join status");
            return false;
        }
    }

    private async Task<bool> IsMDMManagedAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Check for Intune/MDM enrollment via registry
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Enrollments");
            if (key != null)
            {
                var subKeyNames = key.GetSubKeyNames();
                foreach (var subKeyName in subKeyNames)
                {
                    using var enrollmentKey = key.OpenSubKey(subKeyName);
                    var providerID = enrollmentKey?.GetValue("ProviderID") as string;

                    // MS-DM Server indicates MDM management (Intune or other MDM)
                    if (providerID != null && providerID.Contains("MS DM Server", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to determine MDM enrollment status");
        }

        await Task.CompletedTask;
        return false;
    }

    private async Task<bool> IsDefenderTamperProtectionEnabledAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Check Tamper Protection via registry
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Defender\Features");
            if (key != null)
            {
                var tamperProtection = key.GetValue("TamperProtection");

                // Value of 5 or 1 means enabled (5 = managed by Intune, 1 = locally enabled)
                if (tamperProtection is int tamperValue)
                {
                    return tamperValue == 5 || tamperValue == 1;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check Defender Tamper Protection status");
        }

        await Task.CompletedTask;
        return false;
    }
}
