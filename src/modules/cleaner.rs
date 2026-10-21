use super::run_powershell;
use super::printer::OperationResult;

/// Clean user %TEMP% and system Windows\Temp directories
pub fn clean_temp_files() -> OperationResult {
    let script = r#"
        $cleared = 0
        $targets = @($env:TEMP, "$env:WINDIR\Temp")
        foreach ($t in $targets) {
            if (Test-Path $t) {
                Get-ChildItem -Path $t -Recurse -Force -ErrorAction SilentlyContinue | 
                Remove-Item -Force -Recurse -ErrorAction SilentlyContinue
                $cleared++
            }
        }
        "Cleaned $cleared temporary directories successfully."
    "#;

    let (ok, stdout, stderr) = run_powershell(script);
    OperationResult {
        success: ok,
        message: if ok { stdout } else { stderr },
    }
}

/// Purge Windows Prefetch directory
pub fn clean_prefetch() -> OperationResult {
    let script = r#"
        $prefetch = "$env:WINDIR\Prefetch"
        if (Test-Path $prefetch) {
            Get-ChildItem -Path $prefetch -Force -ErrorAction SilentlyContinue | 
            Remove-Item -Force -Recurse -ErrorAction SilentlyContinue
        }
        "Prefetch files cleared."
    "#;

    let (ok, stdout, stderr) = run_powershell(script);
    OperationResult {
        success: ok,
        message: if ok { stdout } else { stderr },
    }
}

/// Clear Windows Event Viewer application and system logs
pub fn clear_event_logs() -> OperationResult {
    let script = r#"
        Get-WinEvent -ListLog * -Force -ErrorAction SilentlyContinue | 
        Where-Object { $_.RecordCount -gt 0 } | 
        ForEach-Object { [System.Diagnostics.Eventing.Reader.EventLogSession]::GlobalSession.ClearLog($_.LogName) }
        "Windows Event Logs cleared."
    "#;

    let (ok, stdout, stderr) = run_powershell(script);
    OperationResult {
        success: ok,
        message: if ok { stdout } else { stderr },
    }
}

/// Purge SoftwareDistribution update download cache
pub fn clean_update_cache() -> OperationResult {
    let script = r#"
        Stop-Service -Name wuauserv -Force -ErrorAction SilentlyContinue
        Stop-Service -Name bits -Force -ErrorAction SilentlyContinue
        $sd = "$env:WINDIR\SoftwareDistribution\Download"
        if (Test-Path $sd) {
            Get-ChildItem -Path $sd -Recurse -Force -ErrorAction SilentlyContinue | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue
        }
        Start-Service -Name bits -ErrorAction SilentlyContinue
        Start-Service -Name wuauserv -ErrorAction SilentlyContinue
        "Windows Update download cache purged and services restarted."
    "#;

    let (ok, stdout, stderr) = run_powershell(script);
    OperationResult {
        success: ok,
        message: if ok { stdout } else { stderr },
    }
}
