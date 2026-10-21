<#
.SYNOPSIS
    WinoxIT Universal Offline NIC Driver Installer
.DESCRIPTION
    Scans the local universal_nic driver repository and injects WHQL network drivers
    directly into Windows DriverStore using PnPUtil without internet connection.
#>

param(
    [string]$DriverPath = $PSScriptRoot
)

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "⚡ WinoxIT Universal Offline NIC Enjektörü v1.0.0" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$infFiles = Get-ChildItem -Path $DriverPath -Filter "*.inf" -Recurse -ErrorAction SilentlyContinue

if ($infFiles.Count -eq 0) {
    Write-Host "[BİLGİ] $DriverPath altında doğrudan .inf dosyası bulunamadı." -ForegroundColor Yellow
    Write-Host "[TARAMA] Sistem PnP Aygıtları taranıyor ve eksik Ağ bağdaştırıcıları aranıyor..." -ForegroundColor Gray
    
    $missingNics = Get-PnpDevice -Class "Net" -ErrorAction SilentlyContinue | Where-Object { $_.Status -ne "OK" }
    if ($missingNics) {
        Write-Host "[UYARI] Sürücüsü eksik veya sorunlu ağ aygıtları bulundu:" -ForegroundColor Red
        $missingNics | Select-Object FriendlyName, InstanceId, Status | Format-Table -AutoSize
    } else {
        Write-Host "[OK] Aktif ağ bağdaştırıcıları çalışır durumda." -ForegroundColor Green
    }
    
    Write-Host "[PnPUtil] Windows Donanım Değişiklikleri taranıyor..." -ForegroundColor Gray
    & pnputil.exe /scan-devices
    exit 0
}

Write-Host "[INFO] $($infFiles.Count) adet sürücü paketi bulundu. DriverStore'a enjekte ediliyor..." -ForegroundColor Green
& pnputil.exe /add-driver "$DriverPath\*.inf" /subdirs /install

Write-Host "[TAMAMLANDI] Sürücü enjeksiyonu ve aygıt taraması tamamlandı." -ForegroundColor Green
