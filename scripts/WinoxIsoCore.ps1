# ═══════════════════════════════════════════════════════════════════
# ⚡ WinoxIT Enterprise — Unattended Windows ISO & WIM Image Engine
# 100% Offline · Katılımsız Kurulum · TPM/SecureBoot Bypass · DISM
# ═══════════════════════════════════════════════════════════════════

param(
    [string]$Action = "generate-xml", # generate-xml, mount-wim, unmount-wim, inject-drivers, inject-winoxit, build-iso
    [string]$IsoWorkDir = "",
    [string]$WimPath = "",
    [int]$WimIndex = 1,
    [string]$MountDir = "",
    [string]$DriversDir = "",
    [string]$OutputIso = "",
    [string]$Username = "WinoxAdmin",
    [string]$Password = ""
)

$desktop = [Environment]::GetFolderPath('Desktop')
$defaultWorkDir = Join-Path $desktop "WinoxIT_ISO_Workspace"

if ([string]::IsNullOrWhiteSpace($IsoWorkDir)) {
    $IsoWorkDir = $defaultWorkDir
}

if (-not (Test-Path $IsoWorkDir)) {
    New-Item -ItemType Directory -Path $IsoWorkDir -Force | Out-Null
}

function Generate-AutounattendXml {
    param(
        [string]$User = "SysForgeAdmin",
        [string]$Pass = "",
        [string]$OutputPath = ""
    )

    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        $OutputPath = Join-Path $IsoWorkDir "autounattend.xml"
    }

    $passNode = if ([string]::IsNullOrWhiteSpace($Pass)) {
        "<Password><Value></Value><PlainText>true</PlainText></Password>"
    } else {
        "<Password><Value>$Pass</Value><PlainText>true</PlainText></Password>"
    }

    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<unattend xmlns="urn:schemas-microsoft-com:unattend">
    <!-- SysForge Studio Enterprise Unattended Answer File -->
    <!-- Windows 11 TPM, SecureBoot, RAM & CPU Check Bypass Enabled -->
    
    <settings pass="windowsPE">
        <component name="Microsoft-Windows-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
            <RunSynchronous>
                <!-- Windows 11 Donanım Kontrolü Bypass Anahtarları -->
                <RunSynchronousCommand wcm:action="add">
                    <Order>1</Order>
                    <Path>reg add HKLM\SYSTEM\Setup\LabConfig /v BypassTPMCheck /t REG_DWORD /d 1 /f</Path>
                    <Description>Bypass TPM 2.0 Check</Description>
                </RunSynchronousCommand>
                <RunSynchronousCommand wcm:action="add">
                    <Order>2</Order>
                    <Path>reg add HKLM\SYSTEM\Setup\LabConfig /v BypassSecureBootCheck /t REG_DWORD /d 1 /f</Path>
                    <Description>Bypass SecureBoot Check</Description>
                </RunSynchronousCommand>
                <RunSynchronousCommand wcm:action="add">
                    <Order>3</Order>
                    <Path>reg add HKLM\SYSTEM\Setup\LabConfig /v BypassRAMCheck /t REG_DWORD /d 1 /f</Path>
                    <Description>Bypass RAM Check</Description>
                </RunSynchronousCommand>
                <RunSynchronousCommand wcm:action="add">
                    <Order>4</Order>
                    <Path>reg add HKLM\SYSTEM\Setup\LabConfig /v BypassCPUCheck /t REG_DWORD /d 1 /f</Path>
                    <Description>Bypass CPU Check</Description>
                </RunSynchronousCommand>
                <RunSynchronousCommand wcm:action="add">
                    <Order>5</Order>
                    <Path>reg add HKLM\SYSTEM\Setup\LabConfig /v BypassStorageCheck /t REG_DWORD /d 1 /f</Path>
                    <Description>Bypass Storage Check</Description>
                </RunSynchronousCommand>
            </RunSynchronous>
            <UserData>
                <AcceptEula>true</AcceptEula>
            </UserData>
        </component>
        <component name="Microsoft-Windows-International-Core-WinPE" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
            <SetupUILanguage>
                <UILanguage>tr-TR</UILanguage>
            </SetupUILanguage>
            <InputLocale>041f:0000041f</InputLocale>
            <SystemLocale>tr-TR</SystemLocale>
            <UILanguage>tr-TR</UILanguage>
            <UserLocale>tr-TR</UserLocale>
        </component>
    </settings>

    <settings pass="specialize">
        <component name="Microsoft-Windows-Shell-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
            <TimeZone>Turkey Standard Time</TimeZone>
        </component>
    </settings>

    <settings pass="oobeSystem">
        <component name="Microsoft-Windows-Shell-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
            <OOBE>
                <HideEULAPage>true</HideEULAPage>
                <HideLocalAccountScreen>true</HideLocalAccountScreen>
                <HideOnlineAccountScreens>true</HideOnlineAccountScreens>
                <HideWirelessSetupInOOBE>false</HideWirelessSetupInOOBE>
                <NetworkLocation>Work</NetworkLocation>
                <ProtectYourPC>3</ProtectYourPC>
            </OOBE>
            <UserAccounts>
                <LocalAccounts>
                    <LocalAccount wcm:action="add">
                        <Name>$User</Name>
                        <DisplayName>$User</DisplayName>
                        <Group>Administrators</Group>
                        $passNode
                    </LocalAccount>
                </LocalAccounts>
            </UserAccounts>
            <AutoLogon>
                $passNode
                <Enabled>true</Enabled>
                <LogonCount>1</LogonCount>
                <Username>$User</Username>
            </AutoLogon>
        </component>
        <component name="Microsoft-Windows-International-Core" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
            <InputLocale>041f:0000041f</InputLocale>
            <SystemLocale>tr-TR</SystemLocale>
            <UILanguage>tr-TR</UILanguage>
            <UserLocale>tr-TR</UserLocale>
        </component>
    </settings>
</unattend>
"@

    Set-Content -Path $OutputPath -Value $xml.Trim() -Encoding UTF8
    Write-Output @{
        Status = "OK"
        Message = "autounattend.xml başarıyla oluşturuldu."
        Path = $OutputPath
        Bypass = @("TPM 2.0", "SecureBoot", "RAM 4GB", "CPU", "MS Account (BypassNRO)")
        Account = $User
    } | ConvertTo-Json
}

switch ($Action.ToLower()) {
    "generate-xml" {
        Generate-AutounattendXml -User $Username -Pass $Password
    }

    "mount-wim" {
        if ([string]::IsNullOrWhiteSpace($WimPath) -or -not (Test-Path $WimPath)) {
            Write-Error "Geçerli bir WIM/ESD dosya yolu belirtilmedi."
            exit 1
        }
        if ([string]::IsNullOrWhiteSpace($MountDir)) {
            $MountDir = Join-Path $IsoWorkDir "Mount"
        }
        if (-not (Test-Path $MountDir)) {
            New-Item -ItemType Directory -Path $MountDir -Force | Out-Null
        }

        & dism.exe /Mount-Wim /WimFile:"$WimPath" /Index:$WimIndex /MountDir:"$MountDir"
        Write-Output @{
            Status = "OK"
            Action = "Mount"
            WimPath = $WimPath
            Index = $WimIndex
            MountDir = $MountDir
        } | ConvertTo-Json
    }

    "unmount-wim" {
        if ([string]::IsNullOrWhiteSpace($MountDir) -or -not (Test-Path $MountDir)) {
            Write-Error "Geçerli bir Mount dizini bulunamadı."
            exit 1
        }
        & dism.exe /Unmount-Wim /MountDir:"$MountDir" /Commit
        Write-Output @{
            Status = "OK"
            Action = "Unmount & Commit"
            MountDir = $MountDir
        } | ConvertTo-Json
    }

    "inject-drivers" {
        if ([string]::IsNullOrWhiteSpace($MountDir) -or -not (Test-Path $MountDir)) {
            Write-Error "Mount dizini bulunamadı."
            exit 1
        }
        if ([string]::IsNullOrWhiteSpace($DriversDir) -or -not (Test-Path $DriversDir)) {
            Write-Error "Sürücü dizini bulunamadı."
            exit 1
        }

        & dism.exe /Image:"$MountDir" /Add-Driver /Driver:"$DriversDir" /Recurse
        Write-Output @{
            Status = "OK"
            Action = "Inject Drivers"
            MountDir = $MountDir
            DriversDir = $DriversDir
        } | ConvertTo-Json
    }

    "inject-sysforge" {
        if ([string]::IsNullOrWhiteSpace($MountDir) -or -not (Test-Path $MountDir)) {
            Write-Error "Mount dizini bulunamadı."
            exit 1
        }

        $scriptsDir = Join-Path $MountDir "Windows\Setup\Scripts"
        if (-not (Test-Path $scriptsDir)) {
            New-Item -ItemType Directory -Path $scriptsDir -Force | Out-Null
        }

        $setupCompleteCmd = Join-Path $scriptsDir "SetupComplete.cmd"
        $cmdContent = @"
@echo off
REM SysForge Studio Unattended First-Boot Post-Setup
reg add "HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32" /f /ve
reg add "HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot" /v TurnOffWindowsCopilot /t REG_DWORD /d 1 /f
reg add "HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI" /v DisableAIDataAnalysis /t REG_DWORD /d 1 /f
exit 0
"@
        Set-Content -Path $setupCompleteCmd -Value $cmdContent.Trim() -Encoding ASCII

        Write-Output @{
            Status = "OK"
            Action = "Inject SysForge Post-Setup"
            SetupComplete = $setupCompleteCmd
        } | ConvertTo-Json
    }

    default {
        Generate-AutounattendXml -User $Username -Pass $Password
    }
}
