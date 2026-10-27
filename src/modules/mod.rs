pub mod hardware;
pub mod printer;
pub mod diagnostics;
pub mod cleaner;
pub mod debloat;
pub mod network;

use std::process::Command;

/// Run a PowerShell command securely and return (success, stdout, stderr)
pub fn run_powershell(cmd: &str) -> (bool, String, String) {
    let output = Command::new("powershell.exe")
        .args(&[
            "-NoProfile",
            "-ExecutionPolicy", "Bypass",
            "-Command",
            &format!("[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; {}", cmd)
        ])
        .output();

    match output {
        Ok(out) => {
            let stdout = String::from_utf8_lossy(&out.stdout).trim().to_string();
            let stderr = String::from_utf8_lossy(&out.stderr).trim().to_string();
            (out.status.success(), stdout, stderr)
        }
        Err(e) => (false, String::new(), e.to_string()),
    }
}

/// Run a native Windows command (cmd.exe /c)
pub fn run_cmd(cmd: &str) -> (bool, String, String) {
    let output = Command::new("cmd.exe")
        .args(&["/c", cmd])
        .output();

    match output {
        Ok(out) => {
            let stdout = String::from_utf8_lossy(&out.stdout).trim().to_string();
            let stderr = String::from_utf8_lossy(&out.stderr).trim().to_string();
            (out.status.success(), stdout, stderr)
        }
        Err(e) => (false, String::new(), e.to_string()),
    }
}
