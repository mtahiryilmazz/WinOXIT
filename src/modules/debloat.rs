use super::run_powershell;
use super::printer::OperationResult;

/// Disable Windows 11 Copilot integration
pub fn disable_copilot() -> OperationResult {
    let script = r#"
        $hkcu = "HKCU:\Software\Policies\Microsoft\Windows\WindowsCopilot"
        $hklm = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot"
        if (-not (Test-Path $hkcu)) { New-Item -Path $hkcu -Force | Out-Null }
        if (-not (Test-Path $hklm)) { New-Item -Path $hklm -Force | Out-Null }
        Set-ItemProperty -Path $hkcu -Name "TurnOffWindowsCopilot" -Value 1 -Type DWord -Force
        Set-ItemProperty -Path $hklm -Name "TurnOffWindowsCopilot" -Value 1 -Type DWord -Force
        "Windows 11 Copilot disabled via policy."
    "#;

    let (ok, stdout, stderr) = run_powershell(script);
    OperationResult {
        success: ok,
        message: if ok { stdout } else { stderr },
    }
}

/// Disable Windows 11 Recall AI data analysis and snapshot tracking
pub fn disable_recall() -> OperationResult {
    let script = r#"
        $hklm = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsAI"
        if (-not (Test-Path $hklm)) { New-Item -Path $hklm -Force | Out-Null }
        Set-ItemProperty -Path $hklm -Name "DisableAIDataAnalysis" -Value 1 -Type DWord -Force
        "Windows 11 Recall AI telemetry disabled."
    "#;

    let (ok, stdout, stderr) = run_powershell(script);
    OperationResult {
        success: ok,
        message: if ok { stdout } else { stderr },
    }
}

/// Disable Cortana and Bing search suggestions in Start Menu
pub fn disable_bing_and_cortana() -> OperationResult {
    let script = r#"
        $searchPolicy = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search"
        if (-not (Test-Path $searchPolicy)) { New-Item -Path $searchPolicy -Force | Out-Null }
        Set-ItemProperty -Path $searchPolicy -Name "AllowCortana" -Value 0 -Type DWord -Force
        Set-ItemProperty -Path $searchPolicy -Name "DisableWebSearch" -Value 1 -Type DWord -Force
        
        $explorerPolicy = "HKCU:\Software\Policies\Microsoft\Windows\Explorer"
        if (-not (Test-Path $explorerPolicy)) { New-Item -Path $explorerPolicy -Force | Out-Null }
        Set-ItemProperty -Path $explorerPolicy -Name "DisableSearchBoxSuggestions" -Value 1 -Type DWord -Force
        "Cortana and Bing Start Menu web search disabled."
    "#;

    let (ok, stdout, stderr) = run_powershell(script);
    OperationResult {
        success: ok,
        message: if ok { stdout } else { stderr },
    }
}

/// Disable DiagTrack (Connected User Experiences and Telemetry) service
pub fn disable_telemetry() -> OperationResult {
    let script = r#"
        Stop-Service -Name DiagTrack -Force -ErrorAction SilentlyContinue
        Set-Service -Name DiagTrack -StartupType Disabled -ErrorAction SilentlyContinue
        
        $dataCol = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection"
        if (-not (Test-Path $dataCol)) { New-Item -Path $dataCol -Force | Out-Null }
        Set-ItemProperty -Path $dataCol -Name "AllowTelemetry" -Value 0 -Type DWord -Force
        "Diagnostic telemetry and DiagTrack service disabled."
    "#;

    let (ok, stdout, stderr) = run_powershell(script);
    OperationResult {
        success: ok,
        message: if ok { stdout } else { stderr },
    }
}

/// Restore classic Windows 10 full context menu on Windows 11
pub fn restore_classic_context_menu() -> OperationResult {
    let script = r#"
        $key = "HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32"
        New-Item -Path $key -Value "" -Force | Out-Null
        Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
        Start-Process explorer
        "Classic Windows 10 Context Menu restored and Explorer restarted."
    "#;

    let (ok, stdout, stderr) = run_powershell(script);
    OperationResult {
        success: ok,
        message: if ok { stdout } else { stderr },
    }
}
