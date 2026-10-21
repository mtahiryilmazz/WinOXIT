use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::fs;
use std::path::Path;
use super::run_powershell;

#[derive(Debug, Serialize, Deserialize, Clone)]
pub struct SystemInfo {
    pub computer_name: String,
    pub manufacturer: String,
    pub model: String,
    pub os_caption: String,
    pub os_version: String,
    pub os_architecture: String,
    pub cpu_name: String,
    pub cpu_cores: u32,
    pub cpu_logical: u32,
    pub total_ram_gb: f64,
    pub motherboard: String,
    pub bios_version: String,
    pub gpu_name: String,
}

#[derive(Debug, Serialize, Deserialize, Clone)]
pub struct HardwareFingerprint {
    pub motherboard_serial: String,
    pub bios_uuid: String,
    pub cpu_id: String,
    pub disk_serials: Vec<String>,
    pub ram_serials: Vec<String>,
    pub hwid_hash: String,
    pub timestamp: String,
}

#[derive(Debug, Serialize, Deserialize, Clone)]
pub struct DriftReport {
    pub matches_baseline: bool,
    pub baseline_hwid: String,
    pub current_hwid: String,
    pub changes: Vec<String>,
}

/// Retrieve basic system overview
pub fn get_system_info() -> SystemInfo {
    let script = r#"
        $cs = Get-CimInstance Win32_ComputerSystem
        $os = Get-CimInstance Win32_OperatingSystem
        $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
        $bb = Get-CimInstance Win32_BaseBoard
        $bios = Get-CimInstance Win32_BIOS
        $gpu = Get-CimInstance Win32_VideoController | Select-Object -First 1
        @{
            computer_name = [string]$cs.Name
            manufacturer = [string]$cs.Manufacturer
            model = [string]$cs.Model
            os_caption = [string]$os.Caption
            os_version = [string]$os.Version
            os_architecture = [string]$os.OSArchitecture
            cpu_name = [string]$cpu.Name
            cpu_cores = [int]$cpu.NumberOfCores
            cpu_logical = [int]$cpu.NumberOfLogicalProcessors
            total_ram_gb = [math]::Round($cs.TotalPhysicalMemory / 1GB, 1)
            motherboard = "$($bb.Manufacturer) $($bb.Product)"
            bios_version = [string]$bios.SMBIOSBIOSVersion
            gpu_name = [string]$gpu.Name
        } | ConvertTo-Json -Compress
    "#;

    let (_, stdout, _) = run_powershell(script);
    serde_json::from_str(&stdout).unwrap_or(SystemInfo {
        computer_name: "LocalHost".into(),
        manufacturer: "PC".into(),
        model: "Desktop".into(),
        os_caption: "Windows 11 / 10".into(),
        os_version: "10.0".into(),
        os_architecture: "64-bit".into(),
        cpu_name: "Generic x86-64".into(),
        cpu_cores: 4,
        cpu_logical: 8,
        total_ram_gb: 16.0,
        motherboard: "Standard Motherboard".into(),
        bios_version: "1.0".into(),
        gpu_name: "Standard Display".into(),
    })
}

/// Generate cryptographic SHA-256 Hardware Fingerprint (HWID)
pub fn generate_hwid() -> HardwareFingerprint {
    let script = r#"
        $bb = Get-CimInstance Win32_BaseBoard | Select-Object -ExpandProperty SerialNumber -ErrorAction SilentlyContinue
        $csp = Get-CimInstance Win32_ComputerSystemProduct | Select-Object -ExpandProperty UUID -ErrorAction SilentlyContinue
        $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1 -ExpandProperty ProcessorId -ErrorAction SilentlyContinue
        $disks = @(Get-CimInstance Win32_DiskDrive | ForEach-Object { "$($_.Model)_$($_.SerialNumber)" })
        $rams = @(Get-CimInstance Win32_PhysicalMemory | ForEach-Object { "$($_.Manufacturer)_$($_.SerialNumber)" })
        @{
            mb_serial = [string]$bb
            uuid = [string]$csp
            cpu_id = [string]$cpu
            disks = $disks
            rams = $rams
        } | ConvertTo-Json -Compress
    "#;

    let (_, stdout, _) = run_powershell(script);
    let v: serde_json::Value = serde_json::from_str(&stdout).unwrap_or(serde_json::json!({}));

    let mb = v["mb_serial"].as_str().unwrap_or("UNKNOWN_MB").to_string();
    let uuid = v["uuid"].as_str().unwrap_or("UNKNOWN_UUID").to_string();
    let cpu = v["cpu_id"].as_str().unwrap_or("UNKNOWN_CPU").to_string();
    
    let disks: Vec<String> = v["disks"]
        .as_array()
        .map(|arr| arr.iter().filter_map(|x| x.as_str().map(|s| s.to_string())).collect())
        .unwrap_or_default();

    let rams: Vec<String> = v["rams"]
        .as_array()
        .map(|arr| arr.iter().filter_map(|x| x.as_str().map(|s| s.to_string())).collect())
        .unwrap_or_default();

    // Deterministic cryptographic hash
    let mut hasher = Sha256::new();
    hasher.update(mb.as_bytes());
    hasher.update(b"|");
    hasher.update(uuid.as_bytes());
    hasher.update(b"|");
    hasher.update(cpu.as_bytes());
    hasher.update(b"|");
    for d in &disks { hasher.update(d.as_bytes()); hasher.update(b";"); }
    hasher.update(b"|");
    for r in &rams { hasher.update(r.as_bytes()); hasher.update(b";"); }
    
    let hash_bytes = hasher.finalize();
    let hwid_hash = hex::encode(hash_bytes);

    HardwareFingerprint {
        motherboard_serial: mb,
        bios_uuid: uuid,
        cpu_id: cpu,
        disk_serials: disks,
        ram_serials: rams,
        hwid_hash,
        timestamp: chrono::Local::now().to_rfc3339(),
    }
}

/// Save HWID baseline to disk for future drift auditing
pub fn save_baseline(path_str: &str) -> Result<HardwareFingerprint, String> {
    let hwid = generate_hwid();
    let json = serde_json::to_string_pretty(&hwid).map_err(|e| e.to_string())?;
    fs::write(path_str, json).map_err(|e| e.to_string())?;
    Ok(hwid)
}

/// Check current hardware against saved baseline to detect swapped/missing components
pub fn audit_drift(path_str: &str) -> Result<DriftReport, String> {
    if !Path::new(path_str).exists() {
        return Err("No baseline file found. Please create a baseline first.".into());
    }

    let content = fs::read_to_string(path_str).map_err(|e| e.to_string())?;
    let baseline: HardwareFingerprint = serde_json::from_str(&content).map_err(|e| e.to_string())?;
    let current = generate_hwid();

    let mut changes = Vec::new();
    if baseline.motherboard_serial != current.motherboard_serial {
        changes.push(format!("Motherboard changed: was '{}', now '{}'", baseline.motherboard_serial, current.motherboard_serial));
    }
    if baseline.bios_uuid != current.bios_uuid {
        changes.push(format!("System UUID changed: was '{}', now '{}'", baseline.bios_uuid, current.bios_uuid));
    }
    if baseline.cpu_id != current.cpu_id {
        changes.push(format!("CPU ID changed: was '{}', now '{}'", baseline.cpu_id, current.cpu_id));
    }
    if baseline.disk_serials != current.disk_serials {
        changes.push(format!("Storage configuration changed! Baseline disks: {:?}, Current: {:?}", baseline.disk_serials, current.disk_serials));
    }
    if baseline.ram_serials != current.ram_serials {
        changes.push(format!("RAM modules modified! Baseline RAM: {:?}, Current: {:?}", baseline.ram_serials, current.ram_serials));
    }

    Ok(DriftReport {
        matches_baseline: changes.is_empty(),
        baseline_hwid: baseline.hwid_hash,
        current_hwid: current.hwid_hash,
        changes,
    })
}
