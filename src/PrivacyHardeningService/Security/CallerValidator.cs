using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using PrivacyHardeningContracts.Commands;

namespace PrivacyHardeningService.Security;

/// <summary>
/// Validates caller identity and authorization
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CallerValidator
{
    private readonly ILogger<CallerValidator> _logger;

    public CallerValidator(ILogger<CallerValidator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Validates that the connected client is authorized to send commands.
    /// This method is command-aware: read-only commands are allowed for non-admin users,
    /// while privileged commands require Administrator privileges.
    /// </summary>
    public bool ValidateCallerForCommand(NamedPipeServerStream pipeStream, CommandBase command)
    {
        try
        {
            bool allowed = false;

            pipeStream.RunAsClient(() =>
            {
                var identity = WindowsIdentity.GetCurrent();
                if (identity == null)
                {
                    _logger.LogWarning("Unable to determine caller identity");
                    allowed = false;
                    return;
                }

                var name = identity.Name ?? "<unknown>";
                _logger.LogDebug("Caller identity: {Identity}", name);

                var principal = new WindowsPrincipal(identity);

                // Determine whether the requested command is privileged
                var isPrivilegedCommand = command switch
                {
                    ApplyCommand => true,
                    RevertCommand => true,
                    CreateSnapshotCommand => true,
                    _ => false
                };

                if (isPrivilegedCommand)
                {
                    // Require Administrator for privileged commands
                    if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
                    {
                        _logger.LogWarning("Rejected non-administrator caller for privileged command: {Identity}", name);
                        allowed = false;
                        return;
                    }

                    // Require high or system integrity level as an additional guard
                    try
                    {
                        if (!IsHighOrSystemIntegrity(identity))
                        {
                            _logger.LogWarning("Rejected caller due to insufficient integrity level: {Identity}", name);
                            allowed = false;
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to determine integrity level for caller {Identity}", name);
                        // Fail closed: require integrity check success for privileged commands
                        allowed = false;
                        return;
                    }

                    // Additional check: verify the caller process binary is signed and trusted
                    try
                    {
                        var pid = GetNamedPipeClientProcessId(pipeStream.SafePipeHandle);
                        if (pid == null)
                        {
                            _logger.LogWarning("Could not determine client process id for caller {Identity}", name);
                            allowed = false;
                            return;
                        }

                        string? exePath = null;
                        try
                        {
                            var proc = Process.GetProcessById(pid.Value);
                            exePath = proc.MainModule?.FileName;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to get process info for PID {Pid}", pid.Value);
                            allowed = false;
                            return;
                        }

                        if (string.IsNullOrEmpty(exePath))
                        {
                            _logger.LogWarning("Unable to determine executable path for client PID {Pid}", pid.Value);
                            allowed = false;
                            return;
                        }

                        if (!IsFileAuthenticodeSignedAndTrusted(exePath))
                        {
                            _logger.LogWarning("Rejected caller because client binary is not signed or not trusted: {Path}", exePath);
                            allowed = false;
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to verify client binary signature for caller {Identity}", name);
                        allowed = false;
                        return;
                    }
                }

                // Additional optional checks could go here (integrity level, signature)

                allowed = true;
            });

            return allowed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Caller validation failed");
            return false;
        }
    }

    // Check if the provided WindowsIdentity corresponds to High or System integrity level
    private static bool IsHighOrSystemIntegrity(WindowsIdentity identity)
    {
        if (identity == null) return false;

        var token = identity.Token;
        var sidString = GetIntegritySidString(token);
        if (string.IsNullOrEmpty(sidString)) return false;

        var rid = ParseIntegrityRidFromSidString(sidString);
        // High = 0x3000 (12288), System = 0x4000 (16384)
        return rid >= 0x3000;
    }

    // Parse integrity RID from SID string like S-1-16-12288
    public static int ParseIntegrityRidFromSidString(string sid)
    {
        if (string.IsNullOrEmpty(sid)) return 0;
        // Integrity SIDs use the sub-authority 16 (S-1-16-<RID>)
        if (!sid.Contains("-16-")) return 0;
        var parts = sid.Split('-');
        if (parts.Length == 0) return 0;
        if (!int.TryParse(parts[^1], out var rid)) return 0;
        return rid;
    }

    private static string? GetIntegritySidString(IntPtr token)
    {
        const int TokenIntegrityLevel = 25; // TOKEN_INFORMATION_CLASS.TokenIntegrityLevel

        if (token == IntPtr.Zero) return null;

        // First call to get required buffer size
        int retLen = 0;
        GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out retLen);
        if (retLen == 0) return null;

        var buffer = Marshal.AllocHGlobal(retLen);
        try
        {
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, retLen, out retLen))
                return null;

            // TOKEN_MANDATORY_LABEL structure: SidAndAttributes (pointer to SID + attributes)
            var sidAndAttrsPtr = Marshal.ReadIntPtr(buffer);
            if (sidAndAttrsPtr == IntPtr.Zero) return null;

            if (!ConvertSidToStringSid(sidAndAttrsPtr, out var stringSidPtr))
                return null;

            var sidString = Marshal.PtrToStringAuto(stringSidPtr);
            LocalFree(stringSidPtr);
            return sidString;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass, IntPtr TokenInformation, int TokenInformationLength, out int ReturnLength);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool ConvertSidToStringSid(IntPtr pSid, out IntPtr ptrSid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(IntPtr Pipe, out uint ClientProcessId);

    private static int? GetNamedPipeClientProcessId(SafePipeHandle handle)
    {
        if (handle == null || handle.IsInvalid) return null;
        var h = handle.DangerousGetHandle();
        if (h == IntPtr.Zero) return null;

        if (GetNamedPipeClientProcessId(h, out uint pid))
        {
            return (int)pid;
        }

        return null;
    }

    // Verify that the specified file is Authenticode signed and the signature is trusted
    private static bool IsFileAuthenticodeSignedAndTrusted(string filePath)
    {
        try
        {
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2

            var fileInfo = new WINTRUST_FILE_INFO(filePath);

            var data = new WINTRUST_DATA();
            data.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA));
            data.dwUIChoice = WINTRUST_DATA.DW_UI_NONE;
            data.fdwRevocationChecks = WINTRUST_DATA.DW_REVOKE_NONE;
            data.dwUnionChoice = WINTRUST_DATA.DW_CHOICE_FILE;

            // Allocate and marshal file info
            var pFile = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
            try
            {
                Marshal.StructureToPtr(fileInfo, pFile, false);
                data.pInfoStruct = pFile;

                uint result = WinVerifyTrust(IntPtr.Zero, action, ref data);
                return result == 0;
            }
            finally
            {
                Marshal.FreeHGlobal(pFile);
                fileInfo.Free();
            }
        }
        catch
        {
            return false;
        }
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint WinVerifyTrust(IntPtr hWnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, ref WINTRUST_DATA pWVTData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;

        public WINTRUST_FILE_INFO(string fileName)
        {
            cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO));
            pcwszFilePath = Marshal.StringToCoTaskMemUni(fileName);
            hFile = IntPtr.Zero;
            pgKnownSubject = IntPtr.Zero;
        }

        public void Free()
        {
            if (pcwszFilePath != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pcwszFilePath);
                pcwszFilePath = IntPtr.Zero;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pInfoStruct;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;

        public const uint DW_UI_ALL = 1;
        public const uint DW_UI_NONE = 2;
        public const uint DW_UI_NOBAD = 3;
        public const uint DW_UI_NOGOOD = 4;

        public const uint DW_REVOKE_NONE = 0x00000000;
        public const uint DW_REVOKE_WHOLECHAIN = 0x00000001;

        public const uint DW_CHOICE_FILE = 1;
        public const uint DW_CHOICE_CATALOG = 2;
        public const uint DW_CHOICE_BLOB = 3;
        public const uint DW_CHOICE_SIGNER = 4;
        public const uint DW_CHOICE_CERT = 5;
    }
}
