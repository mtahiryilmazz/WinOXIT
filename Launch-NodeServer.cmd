@echo off
setlocal enabledelayedexpansion
title WinoxIT — Virtual Server ^& LAN Gateway

:: Check Administrator Privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [!] Yonetici yetkileri algilanamadi. Yonetici olarak yeniden baslatiliyor...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

cd /d "%~dp0"

echo ================================================================
echo  ⚡ WinoxIT Enterprise Virtual Server ^& LAN Gateway
echo ================================================================
echo.

:: Check Node.js installation
where node >nul 2>&1
if %errorLevel% neq 0 (
    echo [HATA] Sistemde Node.js yuklu bulunamadi!
    echo Lutfen https://nodejs.org adresinden Node.js kurun veya winget ile yukleyin:
    echo winget install OpenJS.NodeJS -e
    echo.
    pause
    exit /b 1
)

:: Firewall Rule for Port 9999
echo [*] Windows Guvenlik Duvari Port 9999 TCP kurali kontrol ediliyor...
netsh advfirewall firewall show rule name="WinoxIT Virtual Server Port 9999" >nul 2>&1
if %errorLevel% neq 0 (
    echo [+] Guvenlik duvari kurali olusturuluyor: Port 9999 TCP (Acik)
    netsh advfirewall firewall add rule name="WinoxIT Virtual Server Port 9999" dir=in action=allow protocol=TCP localport=9999 >nul 2>&1
) else (
    echo [OK] Port 9999 Guvenlik Duvari kurali zaten aktif.
)

echo.
echo [*] Web Kokpiti varsayilan tarayicida aciliyor...
start http://localhost:9999/dashboard.html

echo [*] Node.js Virtual Server baslatiliyor...
echo.
node server.js

pause
