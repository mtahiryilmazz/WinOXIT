# ═══════════════════════════════════════════════════════════════════
# SysForge Studio — Cryptographic Hardware Drift & HWID Engine
# 100% Offline · SHA-256 Hardware Fingerprint · Servis Donanım Takibi
# ═══════════════════════════════════════════════════════════════════

param(
    [string]$Action = "snapshot" # snapshot, save-baseline, compare, generate-report
)

$desktop = [Environment]::GetFolderPath('Desktop')
$baseDir = Join-Path $desktop "EkspertizArsivi"
$snapshotsDir = Join-Path $baseDir "Snapshots"
$reportsDir = Join-Path $baseDir "Raporlar"
$baselineFile = Join-Path $snapshotsDir "baseline_hardware.json"

if (-not (Test-Path $snapshotsDir)) { New-Item -ItemType Directory -Path $snapshotsDir -Force | Out-Null }
if (-not (Test-Path $reportsDir)) { New-Item -ItemType Directory -Path $reportsDir -Force | Out-Null }

function Get-HardwareSnapshot {
    $snapshot = [ordered]@{
        Timestamp = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ss")
        MachineName = $env:COMPUTERNAME
    }

    # 1. Motherboard
    try {
        $bb = Get-CimInstance Win32_BaseBoard -ErrorAction SilentlyContinue | Select-Object -First 1
        $snapshot.Motherboard = @{
            Manufacturer = if ($bb.Manufacturer) { $bb.Manufacturer.Trim() } else { "-" }
            Product = if ($bb.Product) { $bb.Product.Trim() } else { "-" }
            SerialNumber = if ($bb.SerialNumber) { $bb.SerialNumber.Trim() } else { "-" }
        }
    } catch {
        $snapshot.Motherboard = @{ Manufacturer = "-"; Product = "-"; SerialNumber = "-" }
    }

    # 2. BIOS & System UUID
    try {
        $bios = Get-CimInstance Win32_BIOS -ErrorAction SilentlyContinue | Select-Object -First 1
        $csp = Get-CimInstance Win32_ComputerSystemProduct -ErrorAction SilentlyContinue | Select-Object -First 1
        $snapshot.Bios = @{
            Manufacturer = if ($bios.Manufacturer) { $bios.Manufacturer.Trim() } else { "-" }
            Version = if ($bios.Version) { $bios.Version.Trim() } else { "-" }
            SerialNumber = if ($bios.SerialNumber) { $bios.SerialNumber.Trim() } else { "-" }
            Uuid = if ($csp.UUID) { $csp.UUID.Trim() } else { "-" }
        }
    } catch {
        $snapshot.Bios = @{ Manufacturer = "-"; Version = "-"; SerialNumber = "-"; Uuid = "-" }
    }

    # 3. CPU
    try {
        $cpu = Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue | Select-Object -First 1
        $snapshot.Processor = @{
            Name = if ($cpu.Name) { $cpu.Name.Trim() } else { "-" }
            ProcessorId = if ($cpu.ProcessorId) { $cpu.ProcessorId.Trim() } else { "-" }
            NumberOfCores = [int]($cpu.NumberOfCores)
            NumberOfLogicalProcessors = [int]($cpu.NumberOfLogicalProcessors)
        }
    } catch {
        $snapshot.Processor = @{ Name = "-"; ProcessorId = "-"; NumberOfCores = 0; NumberOfLogicalProcessors = 0 }
    }

    # 4. RAM Modules
    $ramList = @()
    try {
        $rams = Get-CimInstance Win32_PhysicalMemory -ErrorAction SilentlyContinue
        foreach ($r in $rams) {
            $capGb = [math]::Round(($r.Capacity / 1GB), 2)
            $ramList += [ordered]@{
                BankLabel = if ($r.BankLabel) { $r.BankLabel.Trim() } else { "-" }
                CapacityGB = $capGb
                SpeedMHz = [int]($r.Speed)
                Manufacturer = if ($r.Manufacturer) { $r.Manufacturer.Trim() } else { "-" }
                SerialNumber = if ($r.SerialNumber) { $r.SerialNumber.Trim() } else { "-" }
                PartNumber = if ($r.PartNumber) { $r.PartNumber.Trim() } else { "-" }
            }
        }
    } catch {}
    $snapshot.RamModules = $ramList

    # 5. Disks
    $diskList = @()
    try {
        $disks = Get-CimInstance Win32_DiskDrive -ErrorAction SilentlyContinue
        foreach ($d in $disks) {
            $szGb = [math]::Round(($d.Size / 1GB), 1)
            $diskList += [ordered]@{
                DeviceID = if ($d.DeviceID) { $d.DeviceID.Trim() } else { "-" }
                Model = if ($d.Model) { $d.Model.Trim() } else { "-" }
                SerialNumber = if ($d.SerialNumber) { $d.SerialNumber.Trim() } else { "-" }
                SizeGB = $szGb
                MediaType = if ($d.MediaType) { $d.MediaType.Trim() } else { "-" }
            }
        }
    } catch {}
    $snapshot.Disks = $diskList

    # 6. GPUs
    $gpuList = @()
    try {
        $gpus = Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue
        foreach ($g in $gpus) {
            $ramMb = [math]::Round(($g.AdapterRAM / 1MB), 0)
            $gpuList += [ordered]@{
                Name = if ($g.Name) { $g.Name.Trim() } else { "-" }
                DriverVersion = if ($g.DriverVersion) { $g.DriverVersion.Trim() } else { "-" }
                AdapterRAM_MB = $ramMb
            }
        }
    } catch {}
    $snapshot.Gpus = $gpuList

    # 7. Network Cards
    $netList = @()
    try {
        $nets = Get-CimInstance Win32_NetworkAdapter -ErrorAction SilentlyContinue | Where-Object { $_.MACAddress -and $_.PhysicalAdapter }
        foreach ($n in $nets) {
            $netList += [ordered]@{
                Name = if ($n.Name) { $n.Name.Trim() } else { "-" }
                MacAddress = $n.MACAddress.Trim()
            }
        }
    } catch {}
    $snapshot.NetworkCards = $netList

    # Compute Cryptographic SHA-256 Hash
    $sb = [System.Text.StringBuilder]::new()
    $sb.Append($snapshot.Motherboard.SerialNumber) | Out-Null
    $sb.Append("|") | Out-Null
    $sb.Append($snapshot.Bios.Uuid) | Out-Null
    $sb.Append("|") | Out-Null
    $sb.Append($snapshot.Processor.ProcessorId) | Out-Null

    foreach ($rm in ($snapshot.RamModules | Sort-Object BankLabel, SerialNumber)) {
        $sb.Append("|$($rm.Manufacturer):$($rm.SerialNumber):$($rm.CapacityGB)") | Out-Null
    }
    foreach ($dk in ($snapshot.Disks | Sort-Object SerialNumber)) {
        $sb.Append("|$($dk.Model):$($dk.SerialNumber)") | Out-Null
    }
    foreach ($gp in ($snapshot.Gpus | Sort-Object Name)) {
        $sb.Append("|$($gp.Name)") | Out-Null
    }
    foreach ($nc in ($snapshot.NetworkCards | Sort-Object MacAddress)) {
        $sb.Append("|$($nc.MacAddress)") | Out-Null
    }

    $hasher = [System.Security.Cryptography.SHA256]::Create()
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($sb.ToString())
    $hashBytes = $hasher.ComputeHash($bytes)
    $hashString = [BitConverter]::ToString($hashBytes) -replace "-", ""
    $snapshot.GlobalHardwareHash = $hashString.ToUpperInvariant()

    return $snapshot
}

function Compare-HardwareSnapshots($baseline, $current) {
    $differences = @()

    # Motherboard check
    if ($baseline.Motherboard.SerialNumber -ne $current.Motherboard.SerialNumber) {
        $differences += @{
            Component = "Motherboard (Anakart)"
            ChangeType = "MODIFIED"
            Description = "Anakart seri numarası değişti!"
            OldValue = $baseline.Motherboard.SerialNumber
            NewValue = $current.Motherboard.SerialNumber
        }
    }

    # BIOS UUID check
    if ($baseline.Bios.Uuid -ne $current.Bios.Uuid) {
        $differences += @{
            Component = "System UUID"
            ChangeType = "MODIFIED"
            Description = "Sistem UUID kimliği değişti!"
            OldValue = $baseline.Bios.Uuid
            NewValue = $current.Bios.Uuid
        }
    }

    # CPU check
    if ($baseline.Processor.ProcessorId -ne $current.Processor.ProcessorId) {
        $differences += @{
            Component = "Processor (İşlemci)"
            ChangeType = "MODIFIED"
            Description = "İşlemci modeli veya kimliği değişti!"
            OldValue = "$($baseline.Processor.Name) (ID: $($baseline.Processor.ProcessorId))"
            NewValue = "$($current.Processor.Name) (ID: $($current.Processor.ProcessorId))"
        }
    }

    # RAM Modules check
    $baseRams = @($baseline.RamModules)
    $currRams = @($current.RamModules)

    foreach ($bRam in $baseRams) {
        $found = $currRams | Where-Object { $_.SerialNumber -eq $bRam.SerialNumber -and $_.SerialNumber -ne "-" }
        if (-not $found) {
            $differences += @{
                Component = "RAM Modülü ($($bRam.BankLabel))"
                ChangeType = "REMOVED"
                Description = "RAM modülü söküldü veya değiştirildi! $($bRam.CapacityGB)GB $($bRam.Manufacturer)"
                OldValue = "SN: $($bRam.SerialNumber), $($bRam.CapacityGB)GB $($bRam.SpeedMHz)MHz"
                NewValue = "Yok / Sökülmüş"
            }
        }
    }
    foreach ($cRam in $currRams) {
        $found = $baseRams | Where-Object { $_.SerialNumber -eq $cRam.SerialNumber -and $_.SerialNumber -ne "-" }
        if (-not $found) {
            $differences += @{
                Component = "RAM Modülü ($($cRam.BankLabel))"
                ChangeType = "ADDED"
                Description = "Yeni RAM modülü takıldı! $($cRam.CapacityGB)GB $($cRam.Manufacturer)"
                OldValue = "Yok"
                NewValue = "SN: $($cRam.SerialNumber), $($cRam.CapacityGB)GB $($cRam.SpeedMHz)MHz"
            }
        }
    }

    # Disks check
    $baseDisks = @($baseline.Disks)
    $currDisks = @($current.Disks)
    foreach ($bDisk in $baseDisks) {
        $found = $currDisks | Where-Object { $_.SerialNumber -eq $bDisk.SerialNumber -and $_.SerialNumber -ne "-" }
        if (-not $found) {
            $differences += @{
                Component = "Depolama / Disk ($($bDisk.Model))"
                ChangeType = "REMOVED"
                Description = "Disk söküldü veya değiştirildi! $($bDisk.SizeGB)GB"
                OldValue = "SN: $($bDisk.SerialNumber), $($bDisk.SizeGB)GB $($bDisk.Model)"
                NewValue = "Yok / Sökülmüş"
            }
        }
    }
    foreach ($cDisk in $currDisks) {
        $found = $baseDisks | Where-Object { $_.SerialNumber -eq $cDisk.SerialNumber -and $_.SerialNumber -ne "-" }
        if (-not $found) {
            $differences += @{
                Component = "Depolama / Disk ($($cDisk.Model))"
                ChangeType = "ADDED"
                Description = "Yeni disk eklendi! $($cDisk.SizeGB)GB"
                OldValue = "Yok"
                NewValue = "SN: $($cDisk.SerialNumber), $($cDisk.SizeGB)GB $($cDisk.Model)"
            }
        }
    }

    # GPU check
    $baseGpu = ($baseline.Gpus | Select-Object -First 1).Name
    $currGpu = ($current.Gpus | Select-Object -First 1).Name
    if ($baseGpu -ne $currGpu) {
        $differences += @{
            Component = "GPU (Ekran Kartı)"
            ChangeType = "MODIFIED"
            Description = "Ekran kartı değiştirildi!"
            OldValue = $baseGpu
            NewValue = $currGpu
        }
    }

    $isMatch = ($differences.Count -eq 0) -and ($baseline.GlobalHardwareHash -eq $current.GlobalHardwareHash)

    return @{
        Status = if ($isMatch) { "MATCH" } else { "DRIFT_DETECTED" }
        IsMatch = $isMatch
        BaselineDate = $baseline.Timestamp
        CurrentDate = $current.Timestamp
        BaselineHash = $baseline.GlobalHardwareHash
        CurrentHash = $current.GlobalHardwareHash
        DifferenceCount = $differences.Count
        Differences = $differences
    }
}

# --- Action Handler ---
switch ($Action.ToLower()) {
    "save-baseline" {
        $snapshot = Get-HardwareSnapshot
        $json = $snapshot | ConvertTo-Json -Depth 6
        $json | Set-Content -Path $baselineFile -Encoding UTF8 -Force
        
        $archiveFile = Join-Path $snapshotsDir "Snapshot_$((Get-Date).ToString('yyyyMMdd_HHmmss')).json"
        $json | Set-Content -Path $archiveFile -Encoding UTF8 -Force

        @{
            status = "SAVED"
            baselinePath = $baselineFile
            archivePath = $archiveFile
            hardwareHash = $snapshot.GlobalHardwareHash
            timestamp = $snapshot.Timestamp
            ramCount = $snapshot.RamModules.Count
            diskCount = $snapshot.Disks.Count
        } | ConvertTo-Json -Compress
    }

    "compare" {
        if (-not (Test-Path $baselineFile)) {
            @{
                status = "NO_BASELINE"
                message = "İlk referans donanım kaydı bulunamadı. Lütfen önce referansı kaydedin."
            } | ConvertTo-Json -Compress
            exit 0
        }

        $baselineJson = Get-Content -Path $baselineFile -Raw -Encoding UTF8
        $baseline = $baselineJson | ConvertFrom-Json
        $current = Get-HardwareSnapshot
        $comp = Compare-HardwareSnapshots $baseline $current
        $comp | ConvertTo-Json -Depth 6 -Compress
    }

    "generate-report" {
        $current = Get-HardwareSnapshot
        $compResult = $null
        if (Test-Path $baselineFile) {
            $baseline = (Get-Content -Path $baselineFile -Raw -Encoding UTF8) | ConvertFrom-Json
            $compResult = Compare-HardwareSnapshots $baseline $current
        }

        $reportDate = (Get-Date).ToString("dd.MM.yyyy HH:mm:ss")
        $statusBadge = if ($compResult -and $compResult.IsMatch) {
            "<span style='background:#10b981;color:#fff;padding:6px 14px;border-radius:20px;font-weight:bold;'>ORİJİNAL DONANIM DOĞRULANDI (DEĞİŞİKLİK YOK)</span>"
        } elseif ($compResult -and -not $compResult.IsMatch) {
            "<span style='background:#ef4444;color:#fff;padding:6px 14px;border-radius:20px;font-weight:bold;'>KRİTİK UYARI: DONANIM FARKI TESPİT EDİLDİ ($($compResult.DifferenceCount) Değişiklik)</span>"
        } else {
            "<span style='background:#3b82f6;color:#fff;padding:6px 14px;border-radius:20px;font-weight:bold;'>İLK GİRİŞ EKSPERTİZİ (REFERANS YOK)</span>"
        }

        $diffRows = ""
        if ($compResult -and $compResult.Differences) {
            foreach ($d in $compResult.Differences) {
                $diffRows += "<tr><td><b>$($d.Component)</b></td><td style='color:#ef4444;'>$($d.ChangeType)</td><td>$($d.Description)</td><td><code>$($d.OldValue)</code></td><td><code>$($d.NewValue)</code></td></tr>"
            }
        } else {
            $diffRows = "<tr><td colspan='5' style='text-align:center;color:#10b981;padding:15px;'>Herhangi bir parça değişimi veya sökülme tespit edilmedi. Tüm seri numaraları ve hashler eşleşti.</td></tr>"
        }

        $ramRows = ""
        foreach ($r in $current.RamModules) {
            $ramRows += "<tr><td>$($r.BankLabel)</td><td>$($r.CapacityGB) GB</td><td>$($r.SpeedMHz) MHz</td><td>$($r.Manufacturer)</td><td><code>$($r.SerialNumber)</code></td></tr>"
        }

        $diskRows = ""
        foreach ($d in $current.Disks) {
            $diskRows += "<tr><td>$($d.Model)</td><td>$($d.SizeGB) GB</td><td>$($d.MediaType)</td><td><code>$($d.SerialNumber)</code></td></tr>"
        }

        $html = @"
<!DOCTYPE html>
<html lang="tr">
<head>
<meta charset="utf-8">
<title>SysForge Studio — Donanım Ekspertiz ve Drift Raporu</title>
<style>
  body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #0f172a; color: #e2e8f0; margin: 0; padding: 30px; }
  .card { background: #1e293b; border-radius: 12px; padding: 25px; margin-bottom: 25px; border: 1px solid #334155; }
  h1, h2, h3 { color: #38bdf8; margin-top: 0; }
  table { width: 100%; border-collapse: collapse; margin-top: 15px; }
  th, td { text-align: left; padding: 10px; border-bottom: 1px solid #334155; }
  th { background: #0f172a; color: #94a3b8; font-size: 13px; text-transform: uppercase; }
  code { background: #090d16; padding: 3px 8px; border-radius: 6px; font-family: 'Consolas', monospace; color: #38bdf8; }
</style>
</head>
<body>
<div class="card">
  <h2>🛡️ SysForge Studio — Donanım Güvenlik & Drift Ekspertiz Raporu</h2>
  <p><b>Cihaz Adı:</b> $($current.MachineName) | <b>Tarih:</b> $reportDate</p>
  <p><b>Global HWID SHA-256 Hash:</b> <code>$($current.GlobalHardwareHash)</code></p>
  <div style="margin: 20px 0;">$statusBadge</div>
</div>

<div class="card">
  <h3>🔍 Parça Değişim ve Sökülme Analizi (Hardware Drift)</h3>
  <table>
    <thead><tr><th>Bileşen</th><th>Tür</th><th>Açıklama</th><th>Servis Öncesi</th><th>Mevcut Durum</th></tr></thead>
    <tbody>$diffRows</tbody>
  </table>
</div>

<div class="card">
  <h3>⚡ RAM Modülleri</h3>
  <table>
    <thead><tr><th>Slot</th><th>Kapasite</th><th>Frekans</th><th>Üretici</th><th>Seri Numarası</th></tr></thead>
    <tbody>$ramRows</tbody>
  </table>
</div>

<div class="card">
  <h3>💾 Depolama Aygıtları</h3>
  <table>
    <thead><tr><th>Model</th><th>Kapasite</th><th>Tür</th><th>Seri Numarası</th></tr></thead>
    <tbody>$diskRows</tbody>
  </table>
</div>

<footer style="text-align:center;color:#64748b;margin-top:30px;font-size:12px;">
  Rapor SysForge Studio v6.1 Kriptografik HWID Çekirdeği tarafından çevrimdışı oluşturulmuştur.
</footer>
</body>
</html>
"@

        $reportFileName = "Ekspertiz_Raporu_$((Get-Date).ToString('yyyyMMdd_HHmmss')).html"
        $reportPath = Join-Path $reportsDir $reportFileName
        $html | Set-Content -Path $reportPath -Encoding UTF8 -Force
        
        @{
            status = "OK"
            reportPath = $reportPath
            reportFileName = $reportFileName
            timestamp = $reportDate
            hardwareHash = $current.GlobalHardwareHash
        } | ConvertTo-Json -Compress
    }

    default {
        # Return full current snapshot
        $snapshot = Get-HardwareSnapshot
        $snapshot | ConvertTo-Json -Depth 6 -Compress
    }
}
