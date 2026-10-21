use serde::{Deserialize, Serialize};
use super::run_powershell;

#[derive(Debug, Serialize, Deserialize, Clone)]
pub struct PrinterInfo {
    pub name: String,
    pub port_name: String,
    pub is_default: bool,
    pub is_network: bool,
    pub driver_name: String,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct OperationResult {
    pub success: bool,
    pub message: String,
}

/// List all installed printers on the system
pub fn list_printers() -> Vec<PrinterInfo> {
    let script = r#"
        Get-CimInstance Win32_Printer | Select-Object Name, PortName, Default, Network, DriverName |
        ForEach-Object {
            @{
                name = [string]$_.Name
                port_name = [string]$_.PortName
                is_default = [bool]$_.Default
                is_network = [bool]$_.Network
                driver_name = [string]$_.DriverName
            }
        } | ConvertTo-Json -Compress
    "#;

    let (_, stdout, _) = run_powershell(script);
    if stdout.starts_with('[') {
        serde_json::from_str(&stdout).unwrap_or_default()
    } else if stdout.starts_with('{') {
        if let Ok(item) = serde_json::from_str::<PrinterInfo>(&stdout) {
            vec![item]
        } else {
            vec![]
        }
    } else {
        vec![]
    }
}

/// Fix 0x0000011b Network Printer RPC Authentication Failure
pub fn fix_rpc_11b() -> OperationResult {
    let script = r#"
        $regPath = "HKLM:\SYSTEM\CurrentControlSet\Control\Print"
        Set-ItemProperty -Path $regPath -Name "RpcAuthnLevelExemption" -Value 0 -Type DWord -Force
        Restart-Service -Name Spooler -Force
        "RPC 0x11b exemption applied and Spooler restarted."
    "#;

    let (ok, stdout, stderr) = run_powershell(script);
    OperationResult {
        success: ok,
        message: if ok { stdout } else { stderr },
    }
}

/// Fix 0x00000709 Default Printer & Point & Print Policy Restrictions
pub fn fix_point_and_print_709() -> OperationResult {
    let script = r#"
        $pnpPath = "HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\Printers\PointAndPrint"
        if (-not (Test-Path $pnpPath)) { New-Item -Path $pnpPath -Force | Out-Null }
        Set-ItemProperty -Path $pnpPath -Name "RestrictDriverInstallationToAdministrators" -Value 0 -Type DWord -Force
        Set-ItemProperty -Path $pnpPath -Name "CopyFilesPolicy" -Value 1 -Type DWord -Force
        Restart-Service -Name Spooler -Force
        "Point and Print 0x709 policies applied and Spooler restarted."
    "#;

    let (ok, stdout, stderr) = run_powershell(script);
    OperationResult {
        success: ok,
        message: if ok { stdout } else { stderr },
    }
}

/// Force restart Print Spooler and purge locked queue files
pub fn restart_and_purge_spooler() -> OperationResult {
    let script = r#"
        Stop-Service -Name Spooler -Force -ErrorAction SilentlyContinue
        $spoolDir = "$env:WINDIR\System32\spool\PRINTERS"
        if (Test-Path $spoolDir) {
            Get-ChildItem -Path $spoolDir -Recurse -Force -ErrorAction SilentlyContinue | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue
        }
        Start-Service -Name Spooler
        "Spool queue purged and Print Spooler successfully restarted."
    "#;

    let (ok, stdout, stderr) = run_powershell(script);
    OperationResult {
        success: ok,
        message: if ok { stdout } else { stderr },
    }
}
