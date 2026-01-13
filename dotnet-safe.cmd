@echo off
REM dotnet-safe.cmd
REM Wrapper that runs DotNetSafe.ps1 from cmd.exe.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0DotNetSafe.ps1" %*
exit /b %ERRORLEVEL%
