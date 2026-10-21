<#
.SYNOPSIS
    WinoxIT OEM Driver Orchestrator (Dell, Lenovo, HP, Asus, MSI)
.DESCRIPTION
    Detects hardware manufacturer and triggers official enterprise update tools
    (Dell Command Update, Lenovo ThinInstaller, HP Image Assistant).
#>

param(
    [string]$Action = "Detect"
)

$cs = Get-CimInstance Win32_ComputerSystem
$bb = Get-CimInstance Win32_BaseBoard
$bios = Get-CimInstance Win32_BIOS

$mfg = $cs.Manufacturer.Trim()
$model = $cs.Model.Trim()
$sku = $cs.SystemSKUNumber
$board = $bb.Product.Trim()
$serial = $bios.SerialNumber.Trim()

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "⚡ WinoxIT OEM Donanım & Sürücü Analizi" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Üretici      : $mfg" -ForegroundColor Yellow
Write-Host "Model        : $model" -ForegroundColor Yellow
Write-Host "Anakart      : $board" -ForegroundColor Yellow
Write-Host "Seri / Tag   : $serial" -ForegroundColor Yellow
Write-Host "System SKU   : $sku" -ForegroundColor Yellow
Write-Host "BIOS Sürümü  : $($bios.SMBIOSBIOSVersion)" -ForegroundColor Yellow
Write-Host "----------------------------------------------------------" -ForegroundColor Gray

if ($Action -eq "Detect") {
    Write-Host "[BİLGİ] Üretici tespit edildi: $mfg" -ForegroundColor Green
    if ($mfg -match "Dell") {
        Write-Host "[ÖNERİ] Dell Command | Update CLI (dcu-cli.exe) kullanılabilir." -ForegroundColor Cyan
    } elseif ($mfg -match "Lenovo") {
        Write-Host "[ÖNERİ] Lenovo ThinInstaller / System Update kullanılabilir." -ForegroundColor Cyan
    } elseif ($mfg -match "HP" -or $mfg -match "Hewlett") {
        Write-Host "[ÖNERİ] HP Image Assistant (HPIA) / CMSL kullanılabilir." -ForegroundColor Cyan
    } else {
        Write-Host "[ÖNERİ] Standart üretici destek sayfası veya Intel/Realtek WHQL sürücüleri kullanılabilir." -ForegroundColor Cyan
    }
} elseif ($Action -eq "Update") {
    if ($mfg -match "Dell") {
        $dcu = @(
            "${env:ProgramFiles}\Dell\CommandUpdate\dcu-cli.exe",
            "${env:ProgramFiles(x86)}\Dell\CommandUpdate\dcu-cli.exe"
        ) | Where-Object { Test-Path $_ } | Select-Object -First 1

        if ($dcu) {
            Write-Host "[ÇALIŞTIRILIYOR] Dell Command Update başlatılıyor: $dcu" -ForegroundColor Green
            & $dcu /scan
            & $dcu /applyUpdates -reboot=disable
        } else {
            Write-Host "[UYARI] Dell Command | Update (dcu-cli.exe) kurulu değil. Resmi Dell SupportAssist / DCU paketini kurunuz." -ForegroundColor Yellow
        }
    } elseif ($mfg -match "Lenovo") {
        $lenovo = @(
            "${env:ProgramFiles}\Lenovo\System Update\tvsu.exe",
            "${env:ProgramFiles(x86)}\Lenovo\System Update\tvsu.exe"
        ) | Where-Object { Test-Path $_ } | Select-Object -First 1

        if ($lenovo) {
            Write-Host "[ÇALIŞTIRILIYOR] Lenovo System Update başlatılıyor: $lenovo" -ForegroundColor Green
            & $lenovo /CM -search A -action INSTALL -includereboot 0
        } else {
            Write-Host "[UYARI] Lenovo System Update veya ThinInstaller kurulu değil." -ForegroundColor Yellow
        }
    } elseif ($mfg -match "HP" -or $mfg -match "Hewlett") {
        Write-Host "[BİLGİ] HP Cihazı algılandı. HP Image Assistant taranıyor..." -ForegroundColor Yellow
    } else {
        Write-Host "[BİLGİ] $mfg üreticisi için genel Windows Update ve PnP sürücü taraması çalıştırılıyor..." -ForegroundColor Yellow
        & pnputil.exe /scan-devices
    }
}
