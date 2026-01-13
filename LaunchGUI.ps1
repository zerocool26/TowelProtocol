Param(
    [string]$ProjectPath = $(Join-Path $PSScriptRoot "src\PrivacyHardeningUI\PrivacyHardeningUI.csproj"),
    [string]$ServiceProjectPath = $(Join-Path $PSScriptRoot "src\PrivacyHardeningService\PrivacyHardeningService.csproj"),
    [switch]$NoBuild,
    [switch]$NoService,
    [switch]$Background,
    [switch]$Foreground,
    [switch]$Legacy,
    [int]$BuildTimeoutSeconds = 600,
    [switch]$Help
)

$common = Join-Path $PSScriptRoot 'scripts\DotNetSafe.Common.ps1'
if (-not (Test-Path -LiteralPath $common)) {
    Write-Host "ERROR: Missing shared helper: $common" -ForegroundColor Red
    exit 5
}

. $common

function Show-Help {
    Write-Host "Privacy Hardening Framework - GUI Launcher" -ForegroundColor Cyan
    Write-Host "Usage: LaunchGUI.ps1 [-ProjectPath <path>] [-NoBuild] [-Foreground] [-Background] [-Legacy] [-BuildTimeoutSeconds <sec>] [-Help]" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Options:" -ForegroundColor Green
    Write-Host "  -ProjectPath   Path to the UI project file (default: src\\PrivacyHardeningUI\\PrivacyHardeningUI.csproj)" -ForegroundColor Gray
    Write-Host "  -NoBuild       Skip an explicit build before launching (dotnet run may restore/build if necessary)" -ForegroundColor Gray
    Write-Host "  -Foreground    Run in the current console (blocking). Useful to see logs." -ForegroundColor Gray
    Write-Host "  -Background    Explicitly launch in background (non-blocking)." -ForegroundColor Gray
    Write-Host "  -Legacy        Launch the legacy Windows Forms GUI embedded in this script" -ForegroundColor Gray
    Write-Host "  -BuildTimeoutSeconds  Kill build if it runs longer than this many seconds (default: 600)" -ForegroundColor Gray
    Write-Host "  -Help          Show this help message" -ForegroundColor Gray
}

if ($Help) {
    Show-Help
    exit 0
}

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " Privacy Hardening Framework - GUI Launcher" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""

# Resolve project path
$resolvedProjectPath = Resolve-Path -LiteralPath $ProjectPath -ErrorAction SilentlyContinue
if (-not $resolvedProjectPath) {
    Write-Host "ERROR: Project file not found: $ProjectPath" -ForegroundColor Red
    exit 2
}
$resolvedProjectPath = $resolvedProjectPath.Path

# Verify dotnet is available (prefer explicit dotnet.exe path)
$dotnetExe = Get-DotNetExePath
if (-not $dotnetExe) {
    Write-Host "ERROR: 'dotnet' CLI not found. Please install the .NET SDK (recommended: .NET 8 SDK) or ensure dotnet.exe is available." -ForegroundColor Red
    exit 3
}

Initialize-DotNetSafeEnvironment -RepoRoot $PSScriptRoot

Write-Host "Launching full-featured GUI (project: $resolvedProjectPath)" -ForegroundColor Green

try {
    if (-not $NoBuild) {
        Write-Host "Building Full Solution (including Service and UI)..." -ForegroundColor Gray

        $solutionPath = Join-Path $PSScriptRoot "PrivacyHardeningFramework.sln"
        $buildArgs = @(
            'build', $solutionPath,
            '-c', 'Release',
            '--disable-build-servers',
            '-p:RestoreDisableParallel=true',
            '-p:BuildInParallel=false',
            '-v', 'minimal'
        )

        $buildResult = Invoke-DotNetSafe -RepoRoot $PSScriptRoot -DotNetArgs $buildArgs -TimeoutSeconds $BuildTimeoutSeconds -LogPrefix 'full_build'
        if ($buildResult.TimedOut) {
            Write-Host "ERROR: Build timed out after ${BuildTimeoutSeconds}s and was terminated." -ForegroundColor Red
            Write-Host "  Stdout: $($buildResult.Stdout)" -ForegroundColor Yellow
            Write-Host "  Stderr: $($buildResult.Stderr)" -ForegroundColor Yellow
            exit 124
        }

        if ($null -eq $buildResult.ExitCode -or $buildResult.ExitCode -ne 0) {
            Write-Host "ERROR: Build failed. Aborting launch." -ForegroundColor Red
            Write-Host "  Stdout: $($buildResult.Stdout)" -ForegroundColor Yellow
            Write-Host "  Stderr: $($buildResult.Stderr)" -ForegroundColor Yellow
            exit ($(if ($null -eq $buildResult.ExitCode) { 125 } else { $buildResult.ExitCode }))
        }
    }

    # Start Background Service if requested
    if (-not $NoService) {
        Write-Host "Ensuring Privacy Hardening Service is available..." -ForegroundColor Gray
        
        $serviceRunArgs = @('run', '--project', $ServiceProjectPath, '-c', 'Release', '--no-build')
        
        # In a dev environment, we use the process name or check if the pipe exists
        $serviceRunning = Get-Process -Name "PrivacyHardeningService" -ErrorAction SilentlyContinue
        if ($serviceRunning) {
            Write-Host "Service process already running (PID: $($serviceRunning.Id))." -ForegroundColor Yellow
        } else {
            Write-Host "Starting background service locally..." -ForegroundColor Gray
            # Launch in a separate window so we can see if it crashes, or hidden if background
            $windowStyle = if ($Foreground) { "Normal" } else { "Hidden" }
            Start-Process -FilePath $dotnetExe -ArgumentList $serviceRunArgs -WorkingDirectory (Split-Path $ServiceProjectPath -Parent) -WindowStyle $windowStyle
            
            Write-Host "Waiting for service to initialize..." -ForegroundColor Gray
            Start-Sleep -Seconds 3
        }
    }

    # If we just built successfully, avoid a second build (and potential restore/build stalls)
    $runArgs = @('run', '--project', $resolvedProjectPath, '-c', 'Release')
    if (-not $NoBuild) {
        $runArgs += '--no-build'
    }

    if ($Background) {
        Write-Host "Starting GUI in background..." -ForegroundColor Gray
        Start-Process -FilePath $dotnetExe -ArgumentList $runArgs -WorkingDirectory (Split-Path $resolvedProjectPath -Parent) | Out-Null
        Write-Host "GUI launched (background)." -ForegroundColor Green
        exit 0
    }

    # Default behavior: background launch (non-blocking) unless user explicitly requests foreground.
    if (-not $Foreground) {
        Write-Host "Starting GUI in background (default). Use -Foreground to run in this console." -ForegroundColor Gray
        Start-Process -FilePath $dotnetExe -ArgumentList $runArgs -WorkingDirectory (Split-Path $resolvedProjectPath -Parent) | Out-Null
        Write-Host "GUI launched (background)." -ForegroundColor Green
        exit 0
    }

    if ($Legacy) {
        Write-Host "Launching legacy Windows Forms GUI (interactive)..." -ForegroundColor Yellow
        # Fall through to legacy UI below (wrapped execution)
    } else {
        Write-Host "Starting GUI... (this window will show application output)" -ForegroundColor Gray
        & $dotnetExe @runArgs
        exit $LASTEXITCODE
    }
} catch {
    Write-Host "ERROR: Failed to launch GUI: $_" -ForegroundColor Red
    exit 4
}

# LEGACY WINDOWS FORMS GUI BELOW (kept for reference and optional use)
# It will only run when -Legacy switch is provided
if (-not $Legacy) { return }

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# Create main form
$form = New-Object System.Windows.Forms.Form
$form.Text = "Privacy Hardening Framework"
$form.Size = New-Object System.Drawing.Size(900, 700)
$form.StartPosition = "CenterScreen"
$form.BackColor = [System.Drawing.Color]::FromArgb(240, 240, 240)

# Title Label
$titleLabel = New-Object System.Windows.Forms.Label
$titleLabel.Text = "Windows 11 Privacy Hardening Framework"
$titleLabel.Font = New-Object System.Drawing.Font("Segoe UI", 16, [System.Drawing.FontStyle]::Bold)
$titleLabel.Location = New-Object System.Drawing.Point(20, 20)
$titleLabel.Size = New-Object System.Drawing.Size(850, 40)
$titleLabel.ForeColor = [System.Drawing.Color]::FromArgb(0, 120, 212)
$form.Controls.Add($titleLabel)

# Subtitle Label
$subtitleLabel = New-Object System.Windows.Forms.Label
$subtitleLabel.Text = "Granular Control Over Every Privacy Setting - User is the Ultimate Authority"
$subtitleLabel.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$subtitleLabel.Location = New-Object System.Drawing.Point(20, 60)
$subtitleLabel.Size = New-Object System.Drawing.Size(850, 20)
$subtitleLabel.ForeColor = [System.Drawing.Color]::Gray
$form.Controls.Add($subtitleLabel)

# Info Panel
$infoPanel = New-Object System.Windows.Forms.Panel
$infoPanel.Location = New-Object System.Drawing.Point(20, 100)
$infoPanel.Size = New-Object System.Drawing.Size(850, 150)
$infoPanel.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle
$infoPanel.BackColor = [System.Drawing.Color]::White
$form.Controls.Add($infoPanel)

# Info Text
$infoText = New-Object System.Windows.Forms.TextBox
$infoText.Multiline = $true
$infoText.ReadOnly = $true
$infoText.ScrollBars = "Vertical"
$infoText.Location = New-Object System.Drawing.Point(10, 10)
$infoText.Size = New-Object System.Drawing.Size(830, 130)
$infoText.Font = New-Object System.Drawing.Font("Consolas", 9)
$infoText.BorderStyle = [System.Windows.Forms.BorderStyle]::None
$infoText.BackColor = [System.Drawing.Color]::White
$infoText.Text = @"
 Framework Status: Production Ready
 Build Status: 0 Errors, 6 Warnings (Nullable - Acceptable)
 Total Policies: 28 (13 Telemetry, 9 Services, 5 Firewall, 1 Task)
 Granular Control Models: 7 (PolicyValueOption, ServiceConfigOptions, TaskConfigOptions, etc.)

Session Continuation Complete - 2025-12-30:
 Created 10 new atomic policies (6 telemetry, 4 firewall)
 Enhanced PolicyDefinition with 13 granular control properties
 Updated DependencyResolver for type-aware dependency handling
 Added policy validation and diagnostics to PolicyLoader
"@
$infoPanel.Controls.Add($infoText)

# Available Commands GroupBox
$commandsGroup = New-Object System.Windows.Forms.GroupBox
$commandsGroup.Text = "Available Commands"
$commandsGroup.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$commandsGroup.Location = New-Object System.Drawing.Point(20, 270)
$commandsGroup.Size = New-Object System.Drawing.Size(850, 300)
$form.Controls.Add($commandsGroup)

# Buttons
$yPos = 30

# Audit Button
$btnAudit = New-Object System.Windows.Forms.Button
$btnAudit.Text = "Run System Audit"
$btnAudit.Location = New-Object System.Drawing.Point(20, $yPos)
$btnAudit.Size = New-Object System.Drawing.Size(200, 40)
$btnAudit.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$btnAudit.BackColor = [System.Drawing.Color]::FromArgb(0, 120, 212)
$btnAudit.ForeColor = [System.Drawing.Color]::White
$btnAudit.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
$btnAudit.Add_Click({
    $result = [System.Windows.Forms.MessageBox]::Show(
        "This will audit your current Windows privacy settings.`n`nNote: The service must be running for this to work.`n`nLaunch CLI command?",
        "Run Audit",
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Question
    )
    if ($result -eq [System.Windows.Forms.DialogResult]::Yes) {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = "powershell"
        $psi.Arguments = "-NoExit -Command ""cd '$PSScriptRoot'; & '.\\DotNetSafe.ps1' run --project 'src\PrivacyHardeningCLI\PrivacyHardeningCLI.csproj' -- audit"""
        $psi.WorkingDirectory = $PSScriptRoot
        [System.Diagnostics.Process]::Start($psi) | Out-Null
    }
})
$commandsGroup.Controls.Add($btnAudit)

$yPos += 50

# List Policies Button
$btnList = New-Object System.Windows.Forms.Button
$btnList.Text = "List All Policies"
$btnList.Location = New-Object System.Drawing.Point(20, $yPos)
$btnList.Size = New-Object System.Drawing.Size(200, 40)
$btnList.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$btnList.BackColor = [System.Drawing.Color]::FromArgb(0, 120, 212)
$btnList.ForeColor = [System.Drawing.Color]::White
$btnList.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
$btnList.Add_Click({
    $result = [System.Windows.Forms.MessageBox]::Show(
        "This will list all 28 available privacy policies.`n`nNote: The service must be running for this to work.`n`nLaunch CLI command?",
        "List Policies",
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Question
    )
    if ($result -eq [System.Windows.Forms.DialogResult]::Yes) {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = "powershell"
        $psi.Arguments = "-NoExit -Command ""cd '$PSScriptRoot'; & '.\\DotNetSafe.ps1' run --project 'src\PrivacyHardeningCLI\PrivacyHardeningCLI.csproj' -- list-policies"""
        $psi.WorkingDirectory = $PSScriptRoot
        [System.Diagnostics.Process]::Start($psi) | Out-Null
    }
})
$commandsGroup.Controls.Add($btnList)

$yPos += 50

# Test Connection Button
$btnTest = New-Object System.Windows.Forms.Button
$btnTest.Text = "Test Service Connection"
$btnTest.Location = New-Object System.Drawing.Point(20, $yPos)
$btnTest.Size = New-Object System.Drawing.Size(200, 40)
$btnTest.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$btnTest.BackColor = [System.Drawing.Color]::FromArgb(0, 120, 212)
$btnTest.ForeColor = [System.Drawing.Color]::White
$btnTest.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
$btnTest.Add_Click({
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "powershell"
    $psi.Arguments = "-NoExit -Command ""cd '$PSScriptRoot'; & '.\\DotNetSafe.ps1' run --project 'src\PrivacyHardeningCLI\PrivacyHardeningCLI.csproj' -- test-connection"""
    $psi.WorkingDirectory = $PSScriptRoot
    [System.Diagnostics.Process]::Start($psi) | Out-Null
})
$commandsGroup.Controls.Add($btnTest)

$yPos += 50

# Revert All Button (Warning color)
$btnRevert = New-Object System.Windows.Forms.Button
$btnRevert.Text = "[WARN] Revert All Policies"
$btnRevert.Location = New-Object System.Drawing.Point(20, $yPos)
$btnRevert.Size = New-Object System.Drawing.Size(200, 40)
$btnRevert.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$btnRevert.BackColor = [System.Drawing.Color]::FromArgb(200, 50, 50)
$btnRevert.ForeColor = [System.Drawing.Color]::White
$btnRevert.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
$btnRevert.Add_Click({
    $result = [System.Windows.Forms.MessageBox]::Show(
        "[WARNING] This will revert ALL applied privacy policies!`n`nThis is an emergency rollback function.`n`nAre you sure?",
        "Revert All Policies",
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Warning
    )
    if ($result -eq [System.Windows.Forms.DialogResult]::Yes) {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = "powershell"
        $psi.Arguments = "-NoExit -Command ""cd '$PSScriptRoot'; & '.\\DotNetSafe.ps1' run --project 'src\PrivacyHardeningCLI\PrivacyHardeningCLI.csproj' -- revert-all"""
        $psi.WorkingDirectory = $PSScriptRoot
        [System.Diagnostics.Process]::Start($psi) | Out-Null
    }
})
$commandsGroup.Controls.Add($btnRevert)

# Documentation Buttons (Right side)
$docY = 30

# View Policies Button
$btnViewPolicies = New-Object System.Windows.Forms.Button
$btnViewPolicies.Text = "View Policy Files"
$btnViewPolicies.Location = New-Object System.Drawing.Point(630, $docY)
$btnViewPolicies.Size = New-Object System.Drawing.Size(200, 40)
$btnViewPolicies.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$btnViewPolicies.BackColor = [System.Drawing.Color]::FromArgb(80, 160, 80)
$btnViewPolicies.ForeColor = [System.Drawing.Color]::White
$btnViewPolicies.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
$btnViewPolicies.Add_Click({
    $policiesPath = Join-Path $PSScriptRoot "policies"
    if (Test-Path $policiesPath) {
        explorer $policiesPath
    }
})
$commandsGroup.Controls.Add($btnViewPolicies)

$docY += 50

# View Session Summary Button
$btnSummary = New-Object System.Windows.Forms.Button
$btnSummary.Text = "Session Summary"
$btnSummary.Location = New-Object System.Drawing.Point(630, $docY)
$btnSummary.Size = New-Object System.Drawing.Size(200, 40)
$btnSummary.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$btnSummary.BackColor = [System.Drawing.Color]::FromArgb(80, 160, 80)
$btnSummary.ForeColor = [System.Drawing.Color]::White
$btnSummary.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
$btnSummary.Add_Click({
    $summaryPath = Join-Path $PSScriptRoot "SESSION_CONTINUATION_2025-12-30.md"
    if (Test-Path $summaryPath) {
        notepad $summaryPath
    }
})
$commandsGroup.Controls.Add($btnSummary)

$docY += 50

# View Granular Policies Doc Button
$btnGranular = New-Object System.Windows.Forms.Button
$btnGranular.Text = "Granular Control Guide"
$btnGranular.Location = New-Object System.Drawing.Point(630, $docY)
$btnGranular.Size = New-Object System.Drawing.Size(200, 40)
$btnGranular.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$btnGranular.BackColor = [System.Drawing.Color]::FromArgb(80, 160, 80)
$btnGranular.ForeColor = [System.Drawing.Color]::White
$btnGranular.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
$btnGranular.Add_Click({
    $docPath = Join-Path $PSScriptRoot "policies\GRANULAR_POLICIES.md"
    if (Test-Path $docPath) {
        notepad $docPath
    }
})
$commandsGroup.Controls.Add($btnGranular)

# Status Bar
$statusBar = New-Object System.Windows.Forms.StatusBar
$statusBar.Text = "Ready | CLI available | Avalonia UI conversion in progress"
$statusBar.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$form.Controls.Add($statusBar)

# Note Label
$noteLabel = New-Object System.Windows.Forms.Label
$noteLabel.Text = "Note: This is a temporary Windows Forms GUI. The full Avalonia GUI is the long-term UI.`nCLI commands require the service to be running for most operations."
$noteLabel.Font = New-Object System.Drawing.Font("Segoe UI", 8, [System.Drawing.FontStyle]::Italic)
$noteLabel.Location = New-Object System.Drawing.Point(20, 590)
$noteLabel.Size = New-Object System.Drawing.Size(850, 40)
$noteLabel.ForeColor = [System.Drawing.Color]::Gray
$form.Controls.Add($noteLabel)

# Show the form
[void]$form.ShowDialog()
