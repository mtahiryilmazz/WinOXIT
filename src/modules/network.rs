use serde::{Deserialize, Serialize};
use super::run_powershell;
use super::printer::OperationResult;

#[derive(Debug, Serialize, Deserialize, Clone)]
pub struct NetworkAdapter {
    pub name: String,
    pub ip_address: String,
    pub prefix_length: u32,
}

/// Retrieve all active network adapters and IP addresses
pub fn get_adapters() -> Vec<NetworkAdapter> {
    let script = r#"
        Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike "127.*" } |
        Select-Object InterfaceAlias, IPAddress, PrefixLength |
        ForEach-Object {
            @{
                name = [string]$_.InterfaceAlias
                ip_address = [string]$_.IPAddress
                prefix_length = [int]$_.PrefixLength
            }
        } | ConvertTo-Json -Compress
    "#;

    let (_, stdout, _) = run_powershell(script);
    if stdout.starts_with('[') {
        serde_json::from_str(&stdout).unwrap_or_default()
    } else if stdout.starts_with('{') {
        serde_json::from_str::<NetworkAdapter>(&stdout).map(|a| vec![a]).unwrap_or_default()
    } else {
        vec![]
    }
}

/// Flush local Windows DNS resolver cache
pub fn flush_dns() -> OperationResult {
    let (ok, stdout, stderr) = run_powershell("ipconfig /flushdns");
    OperationResult {
        success: ok,
        message: if ok { stdout } else { stderr },
    }
}

/// Reset Windows Winsock catalog
pub fn reset_winsock() -> OperationResult {
    let (ok, stdout, stderr) = run_powershell("netsh winsock reset");
    OperationResult {
        success: ok,
        message: if ok { stdout } else { stderr },
    }
}

/// Rebuild TCP/IP network stack
pub fn reset_tcpip() -> OperationResult {
    let (ok, stdout, stderr) = run_powershell("netsh int ip reset");
    OperationResult {
        success: ok,
        message: if ok { stdout } else { stderr },
    }
}

/// Run ping latency test against target host
pub fn ping_test(host: &str) -> OperationResult {
    let cmd = format!("Test-Connection -ComputerName '{}' -Count 2 -Quiet", host);
    let (ok, stdout, _) = run_powershell(&cmd);
    let reachable = stdout.trim().eq_ignore_ascii_case("True");
    OperationResult {
        success: reachable,
        message: if reachable { format!("Host '{}' is online and responding.", host) } else { format!("Host '{}' is unreachable.", host) },
    }
}
