@echo off
setlocal
cd /d "%~dp0"
title WinoxIT - Windows Sandbox Launcher

echo =======================================================
echo        WinoxIT - Portable Windows Sandbox Launcher
echo =======================================================
echo.
echo Initializing dynamic Windows Sandbox configuration...

set "WSB_TEMP=%TEMP%\WinoxIT_Auto.wsb"
set "HOST_DIR=%~dp0"
:: Strip trailing backslash if present
if "%HOST_DIR:~-1%"=="\" set "HOST_DIR=%HOST_DIR:~0,-1%"

(
echo ^<Configuration^>
echo   ^<VGpu^>Default^</VGpu^>
echo   ^<Networking^>Default^</Networking^>
echo   ^<MappedFolders^>
echo     ^<MappedFolder^>
echo       ^<HostFolder^>%HOST_DIR%^</HostFolder^>
echo       ^<SandboxFolder^>C:\WinoxIT^</SandboxFolder^>
echo       ^<ReadOnly^>false^</ReadOnly^>
echo     ^</MappedFolder^>
echo   ^</MappedFolders^>
echo   ^<LogonCommand^>
echo     ^<Command^>powershell -NoExit -Command "Write-Host 'WinoxIT Sandbox Test Ortami Baslatildi' -ForegroundColor Cyan; cd C:\WinoxIT; if (Test-Path '.\bin\Release\WinoxIT.exe') { .\bin\Release\WinoxIT.exe } else { .\Launch-NodeServer.cmd }"^</Command^>
echo   ^</LogonCommand^>
echo ^</Configuration^>
) > "%WSB_TEMP%"

echo [OK] Dynamic WSB generated: %WSB_TEMP%
echo [INFO] Launching Windows Sandbox...
start "" "%WSB_TEMP%"
exit /b 0
