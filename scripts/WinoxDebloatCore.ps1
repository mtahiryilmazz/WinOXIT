# ═══════════════════════════════════════════════════════════════════
# SysForge Studio — Native Enterprise Debloat, Privacy & Performance Core
# 100% Offline · Zero External Dependencies · WinUtil & Optimizer Parity
# ═══════════════════════════════════════════════════════════════════

param(
    [string]$Action = "status",
    [switch]$ApplyAllRecommended,
    [switch]$RevertAll
)

function Log-Debloat($msg, $color="Cyan") {
    $ts = Get-Date -Format 'HH:mm:ss'
    Write-Host "[$ts] [SysForge-Debloat] $msg" -ForegroundColor $color
}

# --- 1. Windows 11 AI & Copilot & Recall Shield ---
function Disable-WindowsAI {
    Log-Debloat "Disabling Windows 11 Copilot, Recall AI and Cloud Snapshots..."
    
    # Disable Windows Copilot
    $paths = @(
        "HKCU:\Software\Policies\Microsoft\Windows\WindowsCopilot",
        "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot"
    )
    foreach ($p in $paths) {
        if (-not (Test-Path $p)) { New-Item -Path $p -Force | Out-Null }
        Set-ItemProperty -Path $p -Name "TurnOffWindowsCopilot" -Value 1 -Type DWord -Force
    }

    # Disable Windows Recall AI Analysis
    $aiPath = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsAI"
    if (-not (Test-Path $aiPath)) { New-Item -Path $aiPath -Force | Out-Null }
    Set-ItemProperty -Path $aiPath -Name "DisableAIDataAnalysis" -Value 1 -Type DWord -Force
    Set-ItemProperty -Path $aiPath -Name "DisableClickToDo" -Value 1 -Type DWord -Force

    # Disable Edge & App AI telemetry
    $edgePolicy = "HKLM:\SOFTWARE\Policies\Microsoft\Edge"
    if (-not (Test-Path $edgePolicy)) { New-Item -Path $edgePolicy -Force | Out-Null }
    Set-ItemProperty -Path $edgePolicy -Name "CopilotPageContext" -Value 0 -Type DWord -Force

    Log-Debloat "Windows AI (Copilot, Recall, Edge AI) successfully disabled." "Green"
}

# --- 2. Telemetry, Cortana & Bing Search ---
function Disable-TelemetryAndSearch {
    Log-Debloat "Stopping Telemetry services and disabling Bing Web Search..."

    # Stop and Disable DiagTrack & dmwappushservice
    $services = @("DiagTrack", "dmwappushservice")
    foreach ($s in $services) {
        Stop-Service -Name $s -Force -ErrorAction SilentlyContinue
        Set-Service -Name $s -StartupType Disabled -ErrorAction SilentlyContinue
    }

    # Registry Telemetry Policies
    $dataCol = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection"
    if (-not (Test-Path $dataCol)) { New-Item -Path $dataCol -Force | Out-Null }
    Set-ItemProperty -Path $dataCol -Name "AllowTelemetry" -Value 0 -Type DWord -Force
    Set-ItemProperty -Path $dataCol -Name "MaxTelemetryAllowed" -Value 0 -Type DWord -Force

    # Disable Cortana & Bing in Start Menu
    $searchPolicy = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search"
    if (-not (Test-Path $searchPolicy)) { New-Item -Path $searchPolicy -Force | Out-Null }
    Set-ItemProperty -Path $searchPolicy -Name "AllowCortana" -Value 0 -Type DWord -Force
    Set-ItemProperty -Path $searchPolicy -Name "DisableWebSearch" -Value 1 -Type DWord -Force
    Set-ItemProperty -Path $searchPolicy -Name "ConnectedSearchUseWeb" -Value 0 -Type DWord -Force

    $explorerSearch = "HKCU:\Software\Policies\Microsoft\Windows\Explorer"
    if (-not (Test-Path $explorerSearch)) { New-Item -Path $explorerSearch -Force | Out-Null }
    Set-ItemProperty -Path $explorerSearch -Name "DisableSearchBoxSuggestions" -Value 1 -Type DWord -Force

    # Disable Advertising ID
    $adv = "HKCU:\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo"
    if (-not (Test-Path $adv)) { New-Item -Path $adv -Force | Out-Null }
    Set-ItemProperty -Path $adv -Name "Enabled" -Value 0 -Type DWord -Force

    Log-Debloat "Telemetry and Search suggestions disabled." "Green"
}

# --- 3. Windows 11 UI & Ergonomics (Classic Context Menu, End Task) ---
function Optimize-Windows11UI {
    Log-Debloat "Applying Windows 11 UI Ergonomics tweaks..."

    # Classic Context Menu (Windows 10 style)
    $clsid = "HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32"
    New-Item -Path $clsid -Value "" -Force | Out-Null

    # Enable "End Task" on taskbar right-click
    $devSettings = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings"
    if (-not (Test-Path $devSettings)) { New-Item -Path $devSettings -Force | Out-Null }
    Set-ItemProperty -Path $devSettings -Name "TaskbarEndTask" -Value 1 -Type DWord -Force

    # Show Known File Extensions & Hidden Files
    $advExp = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"
    Set-ItemProperty -Path $advExp -Name "HideFileExt" -Value 0 -Type DWord -Force
    Set-ItemProperty -Path $advExp -Name "Hidden" -Value 1 -Type DWord -Force

    # Open Explorer to "This PC" (Value 1) instead of Quick Access (Value 2)
    Set-ItemProperty -Path $advExp -Name "LaunchTo" -Value 1 -Type DWord -Force

    # Restart Explorer to apply UI tweaks
    Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
    Start-Process explorer

    Log-Debloat "Classic Context Menu, End Task, and Explorer tweaks applied." "Green"
}

# --- 4. Gaming & Low-Latency Performance Tweaks (Optimizer / CTT Parity) ---
function Optimize-LowLatencyGaming {
    Log-Debloat "Applying Low-Latency System & Network Tweaks..."

    # Network Throttling Index & System Responsiveness
    $mm = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"
    if (-not (Test-Path $mm)) { New-Item -Path $mm -Force | Out-Null }
    Set-ItemProperty -Path $mm -Name "NetworkThrottlingIndex" -Value 0xFFFFFFFF -Type DWord -Force
    Set-ItemProperty -Path $mm -Name "SystemResponsiveness" -Value 0 -Type DWord -Force

    # Game Gaming Responsiveness
    $gaming = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"
    if (-not (Test-Path $gaming)) { New-Item -Path $gaming -Force | Out-Null }
    Set-ItemProperty -Path $gaming -Name "GPU Priority" -Value 8 -Type DWord -Force
    Set-ItemProperty -Path $gaming -Name "Priority" -Value 6 -Type DWord -Force
    Set-ItemProperty -Path $gaming -Name "Scheduling Category" -Value "High" -Type String -Force

    # Disable Game Bar & Game DVR background captures
    $dvr = "HKCU:\System\GameConfigStore"
    if (-not (Test-Path $dvr)) { New-Item -Path $dvr -Force | Out-Null }
    Set-ItemProperty -Path $dvr -Name "GameDVR_Enabled" -Value 0 -Type DWord -Force
    Set-ItemProperty -Path $dvr -Name "GameDVR_FSEBehaviorMode" -Value 2 -Type DWord -Force

    # Unlock Ultimate Performance Power Plan
    powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61 | Out-Null

    Log-Debloat "Low-latency gaming optimizations and Ultimate Performance plan configured." "Green"
}

# --- 5. Hosts Telemetry Blocker (Optimizer Parity) ---
function Apply-HostsTelemetryBlocker {
    Log-Debloat "Injecting telemetry block rules into Windows Hosts file..."
    $hostsPath = "$env:WINDIR\System32\drivers\etc\hosts"
    $backupPath = "$env:WINDIR\System32\drivers\etc\hosts.sysforge.bak"
    
    if (-not (Test-Path $backupPath)) {
        Copy-Item -Path $hostsPath -Destination $backupPath -Force
    }

    $blockedDomains = @(
        "v10.events.data.microsoft.com",
        "v20.events.data.microsoft.com",
        "telemetry.microsoft.com",
        "watson.telemetry.microsoft.com",
        "telecommand.telemetry.microsoft.com",
        "feedback.windows.com",
        "sqm.telemetry.microsoft.com",
        "browser.pipe.aria.microsoft.com",
        "copilot.microsoft.com",
        "activity.windows.com"
    )

    $currentHosts = Get-Content $hostsPath -Raw -ErrorAction SilentlyContinue
    $sb = [System.Text.StringBuilder]::new($currentHosts)
    
    $sb.AppendLine("`r`n# === SysForge Studio Telemetry Shield ===") | Out-Null
    foreach ($d in $blockedDomains) {
        if ($currentHosts -notmatch [regex]::Escape($d)) {
            $sb.AppendLine("0.0.0.0 $d") | Out-Null
        }
    }
    Set-Content -Path $hostsPath -Value $sb.ToString() -Force
    ipconfig /flushdns | Out-Null
    Log-Debloat "Hosts telemetry blocker rules applied and DNS flushed." "Green"
}

# --- 6. Windows Update 2-Year Feature Delay (CTT WinUtil Parity) ---
function Apply-WindowsUpdateFeatureDelay {
    Log-Debloat "Configuring Windows Update for Security-Only (2-Year Feature Delay)..."
    $wuPath = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate"
    if (-not (Test-Path $wuPath)) { New-Item -Path $wuPath -Force | Out-Null }
    
    # Branch readiness for business / delay feature updates (days)
    Set-ItemProperty -Path $wuPath -Name "DeferFeatureUpdates" -Value 1 -Type DWord -Force
    Set-ItemProperty -Path $wuPath -Name "DeferFeatureUpdatesPeriodInDays" -Value 730 -Type DWord -Force # 2 Years
    Set-ItemProperty -Path $wuPath -Name "DeferQualityUpdates" -Value 0 -Type DWord -Force # Keep security patches instant!
    
    Log-Debloat "Windows Update configured to delay breaking feature updates for 2 years while receiving instant security patches." "Green"
}

# --- 7. Microsoft OneDrive Clean Removal & Restoration ---
function Remove-OneDrive {
    Log-Debloat "Terminating and cleanly uninstalling Microsoft OneDrive..."
    
    # 1. Kill OneDrive processes
    $processes = @("OneDrive", "OneDriveStandaloneUpdater")
    foreach ($p in $processes) {
        Stop-Process -Name $p -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Milliseconds 800

    # 2. Locate and invoke setup uninstaller
    $uninstaller = $null
    $candidates = @(
        "$env:SystemRoot\SysWOW64\OneDriveSetup.exe",
        "$env:SystemRoot\System32\OneDriveSetup.exe",
        "$env:LOCALAPPDATA\Microsoft\OneDrive\Update\OneDriveSetup.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { $uninstaller = $c; break }
    }

    if ($uninstaller) {
        Log-Debloat "Running OneDrive uninstaller: $uninstaller /uninstall"
        Start-Process -FilePath $uninstaller -ArgumentList "/uninstall" -Wait -WindowStyle Hidden -ErrorAction SilentlyContinue
    }

    # 3. Clean Run key and Explorer sidebar namespace
    Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "OneDrive" -ErrorAction SilentlyContinue
    $onedriveClsid = "HKCR:\CLSID\{018D5C66-4533-4307-9B53-224DE2ED1FE6}"
    if (Test-Path $onedriveClsid) {
        Set-ItemProperty -Path $onedriveClsid -Name "System.IsPinnedToNameSpaceTree" -Value 0 -Type DWord -Force -ErrorAction SilentlyContinue
    }

    # 4. GPO Policy to prevent OneDrive usage for file storage
    $oneDrivePolicy = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\OneDrive"
    if (-not (Test-Path $oneDrivePolicy)) { New-Item -Path $oneDrivePolicy -Force | Out-Null }
    Set-ItemProperty -Path $oneDrivePolicy -Name "DisableFileSyncNGSC" -Value 1 -Type DWord -Force

    Log-Debloat "Microsoft OneDrive successfully removed and storage sync disabled." "Green"
}

function Restore-OneDrive {
    Log-Debloat "Re-enabling OneDrive policies and triggering installer..."
    $oneDrivePolicy = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\OneDrive"
    if (Test-Path $oneDrivePolicy) {
        Remove-ItemProperty -Path $oneDrivePolicy -Name "DisableFileSyncNGSC" -ErrorAction SilentlyContinue
    }
    
    $candidates = @(
        "$env:SystemRoot\SysWOW64\OneDriveSetup.exe",
        "$env:SystemRoot\System32\OneDriveSetup.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) {
            Start-Process -FilePath $c -ArgumentList "/silent" -WindowStyle Hidden -ErrorAction SilentlyContinue
            Log-Debloat "OneDrive installation started from system cache: $c" "Green"
            return
        }
    }
    Log-Debloat "OneDrive system cache not found. Please install via winget or Microsoft Store." "Yellow"
}

# --- 8. Microsoft Edge Background & Telemetry Debloat ---
function Debloat-MicrosoftEdge {
    Log-Debloat "Applying Microsoft Edge background debloat and telemetry lockdown..."
    $edgePolicy = "HKLM:\SOFTWARE\Policies\Microsoft\Edge"
    if (-not (Test-Path $edgePolicy)) { New-Item -Path $edgePolicy -Force | Out-Null }

    # Disable background processes when Edge is closed
    Set-ItemProperty -Path $edgePolicy -Name "BackgroundModeEnabled" -Value 0 -Type DWord -Force
    # Disable Startup Boost
    Set-ItemProperty -Path $edgePolicy -Name "StartupBoostEnabled" -Value 0 -Type DWord -Force
    # Disable Shopping and telemetry features
    Set-ItemProperty -Path $edgePolicy -Name "EdgeShoppingAssistantEnabled" -Value 0 -Type DWord -Force
    Set-ItemProperty -Path $edgePolicy -Name "PersonalizationReportingEnabled" -Value 0 -Type DWord -Force
    Set-ItemProperty -Path $edgePolicy -Name "HubsSidebarEnabled" -Value 0 -Type DWord -Force
    Set-ItemProperty -Path $edgePolicy -Name "WebWidgetAllowed" -Value 0 -Type DWord -Force
    # Disable Edge Prelaunch / Tab Preload
    $edgeMain = "HKLM:\SOFTWARE\Policies\Microsoft\MicrosoftEdge\Main"
    if (-not (Test-Path $edgeMain)) { New-Item -Path $edgeMain -Force | Out-Null }
    Set-ItemProperty -Path $edgeMain -Name "AllowPrelaunch" -Value 0 -Type DWord -Force
    
    $tabPreload = "HKLM:\SOFTWARE\Policies\Microsoft\MicrosoftEdge\TabPreload"
    if (-not (Test-Path $tabPreload)) { New-Item -Path $tabPreload -Force | Out-Null }
    Set-ItemProperty -Path $tabPreload -Name "AllowTabPreloading" -Value 0 -Type DWord -Force

    # Kill running background Edge instances
    Stop-Process -Name "msedge" -Force -ErrorAction SilentlyContinue

    Log-Debloat "Microsoft Edge background mode, startup boost, and telemetry successfully disabled." "Green"
}

# Execution Dispatcher
switch ($Action.ToLower()) {
    "disable-ai" { Disable-WindowsAI }
    "disable-telemetry" { Disable-TelemetryAndSearch }
    "optimize-ui" { Optimize-Windows11UI }
    "optimize-gaming" { Optimize-LowLatencyGaming }
    "block-hosts" { Apply-HostsTelemetryBlocker }
    "wu-delay" { Apply-WindowsUpdateFeatureDelay }
    "remove-onedrive" { Remove-OneDrive }
    "restore-onedrive" { Restore-OneDrive }
    "debloat-edge" { Debloat-MicrosoftEdge }
    "apply-all" {
        Disable-WindowsAI
        Disable-TelemetryAndSearch
        Optimize-Windows11UI
        Optimize-LowLatencyGaming
        Apply-HostsTelemetryBlocker
        Apply-WindowsUpdateFeatureDelay
        Debloat-MicrosoftEdge
    }
    default {
        @{
            copilot_disabled = (Get-ItemProperty "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot" -Name "TurnOffWindowsCopilot" -ErrorAction SilentlyContinue) -ne $null
            recall_disabled = (Get-ItemProperty "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsAI" -Name "DisableAIDataAnalysis" -ErrorAction SilentlyContinue) -ne $null
            telemetry_disabled = (Get-ItemProperty "HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection" -Name "AllowTelemetry" -ErrorAction SilentlyContinue).AllowTelemetry -eq 0
            classic_menu_enabled = Test-Path "HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}"
            onedrive_disabled = (Get-ItemProperty "HKLM:\SOFTWARE\Policies\Microsoft\Windows\OneDrive" -Name "DisableFileSyncNGSC" -ErrorAction SilentlyContinue).DisableFileSyncNGSC -eq 1
            edge_boost_disabled = (Get-ItemProperty "HKLM:\SOFTWARE\Policies\Microsoft\Edge" -Name "StartupBoostEnabled" -ErrorAction SilentlyContinue).StartupBoostEnabled -eq 0
            status = "READY"
        } | ConvertTo-Json -Compress
    }
}
