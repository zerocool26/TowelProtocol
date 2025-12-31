@echo off
REM Privacy Hardening Framework - GUI Launcher (batch wrapper)
echo Privacy Hardening Framework - GUI Launcher
echo ==========================================
echo.
echo Passing arguments to PowerShell launcher. Use -Help for options.
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0LaunchGUI.ps1" %*
exit /b %ERRORLEVEL%
