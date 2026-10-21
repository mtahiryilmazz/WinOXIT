@echo off
chcp 65001 >nul
title WinoxIT - Universal Offline NIC Driver Installer
color 0b

echo ================================================================
echo   ⚡ WINOXIT ENTERPRISE - UNIVERSAL OFFLINE NIC INJECTOR
echo ================================================================
echo.
echo [1/3] Donanimdaki eksik ag kartlari taraniyor...
echo.

pnputil.exe /enum-devices /class Net /problem
if %errorlevel% neq 0 (
    echo [BILGI] Sorunlu veya surucusu eksik ag bagdastiricisi kontrol edildi.
)

echo.
echo [2/3] WHQL Ag Suruculeri (Ethernet ^& Wi-Fi) enjekte ediliyor...
echo Hedef Dizin: %~dp0
echo.

pnputil.exe /add-driver "%~dp0*.inf" /subdirs /install

echo.
echo [3/3] Ag Baglantilari yenileniyor...
ipconfig /renew >nul 2>&1

echo.
echo ================================================================
echo   [OK] Islem tamamlandi. Ag baglantinizi kontrol edin.
echo ================================================================
pause
