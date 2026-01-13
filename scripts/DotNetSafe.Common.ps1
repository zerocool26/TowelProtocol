<#
  DotNetSafe.Common.ps1
  Shared helpers used by DotNetSafe.ps1 and LaunchGUI.ps1.

  Keep this file side-effect free: define functions only.
#>

function Get-DotNetExePath {
    # Prefer a real dotnet.exe path over any alias/shim to avoid odd hangs.
    $candidates = @()

    if ($env:DOTNET_ROOT) {
        $candidates += (Join-Path $env:DOTNET_ROOT 'dotnet.exe')
    }

    $candidates += @(
        'C:\Program Files\dotnet\dotnet.exe',
        'C:\Program Files (x86)\dotnet\dotnet.exe'
    )

    foreach ($p in $candidates) {
        if ($p -and (Test-Path -LiteralPath $p)) {
            return $p
        }
    }

    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source -and (Test-Path -LiteralPath $cmd.Source)) {
        return $cmd.Source
    }

    return $null
}

function Initialize-DotNetSafeEnvironment {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    # These mitigate several real-world “dotnet is stuck” scenarios:
    # - first-time experience / telemetry
    # - build servers (msbuild/razor/roslyn) deadlocks
    # - NuGet cache contention (AV/OneDrive/roaming profile)
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_CLI_DISABLE_BUILD_SERVERS = '1'
    $env:MSBUILDDISABLENODEREUSE = '1'
    $env:NUGET_XMLDOC_MODE = 'skip'

    $localNuGetRoot = Join-Path $RepoRoot '.nuget'
    $localPackages = Join-Path $localNuGetRoot 'packages'
    $localHttpCache = Join-Path $localNuGetRoot 'http-cache'
    $localPluginsCache = Join-Path $localNuGetRoot 'plugins-cache'

    New-Item -ItemType Directory -Path $localPackages -Force | Out-Null
    New-Item -ItemType Directory -Path $localHttpCache -Force | Out-Null
    New-Item -ItemType Directory -Path $localPluginsCache -Force | Out-Null

    $env:NUGET_PACKAGES = $localPackages
    $env:NUGET_HTTP_CACHE_PATH = $localHttpCache
    $env:NUGET_PLUGINS_CACHE_PATH = $localPluginsCache
}

function Invoke-ExternalProcessWithTimeout {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$ArgumentList,

        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,

        [int]$TimeoutSeconds = 0,

        [string]$StdoutPath,
        [string]$StderrPath
    )

    if (-not (Test-Path -LiteralPath $WorkingDirectory)) {
        throw "WorkingDirectory does not exist: $WorkingDirectory"
    }

    function ConvertTo-QuotedWindowsArgument {
        param([string]$Arg)
        if ($null -eq $Arg) { return '""' }
        # Basic Windows command-line quoting: wrap in quotes if spaces or quotes, escape embedded quotes.
        if ($Arg -match '[\s"]') {
            return '"' + ($Arg -replace '"', '\\"') + '"'
        }
        return $Arg
    }

    $redirectStdout = [bool]$StdoutPath
    $redirectStderr = [bool]$StderrPath

    # Create log directories up-front.
    if ($redirectStdout) {
        $stdoutDir = Split-Path -Parent $StdoutPath
        if ($stdoutDir) { New-Item -ItemType Directory -Path $stdoutDir -Force | Out-Null }
    }
    if ($redirectStderr) {
        $stderrDir = Split-Path -Parent $StderrPath
        if ($stderrDir) { New-Item -ItemType Directory -Path $stderrDir -Force | Out-Null }
    }

    # Windows PowerShell 5.1 quirk: Start-Process without -Wait doesn't reliably populate ExitCode,
    # and using Process async DataReceived handlers can destabilize the host.
    # To get a reliable exit code + logs + timeout, run the command inside a separate powershell.exe
    # process ("trampoline") using -EncodedCommand. The trampoline redirects stdout/stderr to files
    # and then exits with the real process exit code.

    function ConvertTo-PSSingleQuotedLiteral {
        param([string]$Text)
        if ($null -eq $Text) { return "''" }
        return "'" + ($Text -replace "'", "''") + "'"
    }

    $psExe = (Get-Command powershell.exe -ErrorAction SilentlyContinue).Source
    if (-not $psExe -or -not (Test-Path -LiteralPath $psExe)) {
        $psExe = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    }
    if (-not (Test-Path -LiteralPath $psExe)) {
        throw "powershell.exe not found; cannot run external process safely with timeout/logging."
    }

    $dotnetLit = ConvertTo-PSSingleQuotedLiteral $FilePath
    $wdLit = ConvertTo-PSSingleQuotedLiteral $WorkingDirectory

    $stdoutTarget = if ($redirectStdout) { ConvertTo-PSSingleQuotedLiteral $StdoutPath } else { '$null' }
    $stderrTarget = if ($redirectStderr) { ConvertTo-PSSingleQuotedLiteral $StderrPath } else { '$null' }

    $argsLits = if ($ArgumentList -and $ArgumentList.Count -gt 0) {
        ($ArgumentList | ForEach-Object { ConvertTo-PSSingleQuotedLiteral $_ }) -join ', '
    } else {
        ''
    }

    $inner = @(
        "`$ErrorActionPreference = 'Stop'",
        "Set-Location -LiteralPath $wdLit",
        ("`$a = @($argsLits)"),
        ("& $dotnetLit @a 1> $stdoutTarget 2> $stderrTarget"),
        'exit $LASTEXITCODE'
    ) -join "; "

    $enc = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($inner))

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $psExe
    $psi.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand $enc"
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $false
    $psi.RedirectStandardError = $false

    $p = New-Object System.Diagnostics.Process
    $p.StartInfo = $psi

    $timedOut = $false
    $exitCodeError = $null
    $exitCode = $null

    if (-not $p.Start()) {
        throw "Failed to start process trampoline: $psExe"
    }

    if ($TimeoutSeconds -gt 0) {
        $exited = $p.WaitForExit([Math]::Max(1, $TimeoutSeconds) * 1000)
        if (-not $exited) {
            $timedOut = $true
            try { $p.Kill() } catch { }
        }
    } else {
        $p.WaitForExit()
    }

    if (-not $timedOut) {
        try { $exitCode = $p.ExitCode } catch { $exitCodeError = $_.Exception.Message }
    }

    [pscustomobject]@{
        Process       = $p
        TimedOut      = $timedOut
        HasExited     = (-not $timedOut)
        ExitCode      = $exitCode
        ExitCodeError = $exitCodeError
        Stdout        = $StdoutPath
        Stderr        = $StderrPath
    }
}

function Invoke-DotNetSafe {
    [CmdletBinding(PositionalBinding = $false)]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,

        [Parameter(Mandatory = $true)]
        [string[]]$DotNetArgs,

        [string]$WorkingDirectory,

        [int]$TimeoutSeconds = 900,

        [switch]$NoTimeout,

        [string]$LogPrefix = 'dotnet'
    )

    $dotnetExe = Get-DotNetExePath
    if (-not $dotnetExe) {
        throw "dotnet.exe not found. Install the .NET SDK (recommended: .NET 8 SDK) or ensure dotnet.exe is available."
    }

    Initialize-DotNetSafeEnvironment -RepoRoot $RepoRoot

    $wd = if ($WorkingDirectory) { $WorkingDirectory } else { $RepoRoot }
    $logDir = Join-Path $RepoRoot 'build_logs'
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null

    $stamp = (Get-Date).ToString('yyyyMMdd_HHmmss')
    $stdoutLog = Join-Path $logDir "${LogPrefix}_${stamp}.out.log"
    $stderrLog = Join-Path $logDir "${LogPrefix}_${stamp}.err.log"

    $effectiveTimeout = if ($NoTimeout) { 0 } else { [Math]::Max(0, $TimeoutSeconds) }

    Invoke-ExternalProcessWithTimeout -FilePath $dotnetExe -ArgumentList $DotNetArgs -WorkingDirectory $wd -TimeoutSeconds $effectiveTimeout -StdoutPath $stdoutLog -StderrPath $stderrLog
}
