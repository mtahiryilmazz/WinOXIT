using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace WinoxIT.Services
{
    public class NativeSystemEngine
    {
        public static async Task<(bool Success, string Output)> RunPowerShellAsync(string script, int timeoutMs = 60000)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; {script}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };

                    using var p = Process.Start(psi);
                    if (p == null) return (false, "Failed to start powershell process.");

                    var stdout = p.StandardOutput.ReadToEnd();
                    var stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit(timeoutMs);

                    if (p.ExitCode == 0)
                    {
                        return (true, stdout.Trim());
                    }
                    else
                    {
                        var msg = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                        return (false, msg.Trim());
                    }
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            });
        }

        public static async Task<(bool Success, string Output)> RunCmdAsync(string command, int timeoutMs = 60000)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c {command}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var p = Process.Start(psi);
                    if (p == null) return (false, "Failed to start cmd.");
                    var stdout = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(timeoutMs);
                    return (p.ExitCode == 0, stdout.Trim());
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            });
        }

        public static string ComputeSha256(string rawData)
        {
            using var sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
            var builder = new StringBuilder();
            foreach (var b in bytes)
            {
                builder.Append(b.ToString("x2"));
            }
            return builder.ToString();
        }

        // =========================================================================
        // 1. HARDWARE DRIFT & DETERMINISTIC SHA-256 FINGERPRINT
        // =========================================================================

        public static async Task<(string Hash, string Json)> GetHardwareFingerprintDetailsAsync()
        {
            var script = @"
                $bb = Get-CimInstance Win32_BaseBoard -ErrorAction SilentlyContinue | Select-Object Manufacturer, Product, SerialNumber
                $cpu = Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue | Select-Object Name, ProcessorId, NumberOfCores, NumberOfLogicalProcessors
                $rams = Get-CimInstance Win32_PhysicalMemory -ErrorAction SilentlyContinue | Select-Object BankLabel, Capacity, Manufacturer, PartNumber, SerialNumber, Speed
                $disks = Get-CimInstance Win32_DiskDrive -ErrorAction SilentlyContinue | Select-Object Model, SerialNumber, Size, InterfaceType
                $nics = Get-CimInstance Win32_NetworkAdapter -ErrorAction SilentlyContinue | Where-Object { $_.PhysicalAdapter -eq $true } | Select-Object Name, MACAddress
                $gpus = Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue | Select-Object Name, DriverVersion

                [PSCustomObject]@{
                    MachineName = $env:COMPUTERNAME
                    Motherboard = ""$($bb.Manufacturer) $($bb.Product)"".Trim()
                    MotherboardSerial = ""$($bb.SerialNumber)"".Trim()
                    Processor = ""$($cpu.Name)"".Trim()
                    ProcessorId = ""$($cpu.ProcessorId)"".Trim()
                    Cores = [int]$cpu.NumberOfLogicalProcessors
                    Rams = @($rams | ForEach-Object { ""$($_.BankLabel): $($_.Manufacturer) $($_.PartNumber) | SN: $($_.SerialNumber) | $([math]::Round($_.Capacity/1GB))GB @$($_.Speed)MHz"" })
                    Disks = @($disks | ForEach-Object { ""$($_.Model) | SN: $($_.SerialNumber) | $([math]::Round($_.Size/1GB))GB ($($_.InterfaceType))"" })
                    Nics = @($nics | ForEach-Object { ""$($_.Name) | MAC: $($_.MACAddress)"" })
                    Gpus = @($gpus | ForEach-Object { ""$($_.Name) (Sürücü: $($_.DriverVersion))"" })
                } | ConvertTo-Json -Depth 4
            ";

            var res = await RunPowerShellAsync(script);
            var json = res.Success ? res.Output : "{}";
            var hash = ComputeSha256(json);
            return (hash, json);
        }

        public static async Task<(bool Matched, string DiffLog)> CompareHardwareBaselineAsync(string baselineJson)
        {
            var (currHash, currJson) = await GetHardwareFingerprintDetailsAsync();

            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════════════");
            sb.AppendLine(" 🛡️ WINOXIT KRİPTOGRAFİK DONANIM DEĞİŞİM & DRIFT RAPORU");
            sb.AppendLine("═══════════════════════════════════════════════════════════");
            sb.AppendLine($"Cihaz Adı: {Environment.MachineName}");
            sb.AppendLine($"Rapor Tarihi: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
            sb.AppendLine();

            try
            {
                using var baseDoc = JsonDocument.Parse(baselineJson);
                using var currDoc = JsonDocument.Parse(currJson);

                var baseRoot = baseDoc.RootElement;
                var currRoot = currDoc.RootElement;

                var baseHash = ComputeSha256(baselineJson);
                sb.AppendLine($"Baseline İmzası (SHA-256) : {baseHash}");
                sb.AppendLine($"Anlık İmza      (SHA-256) : {currHash}");
                sb.AppendLine();

                var driftDetected = false;

                // 1. Anakart
                var baseMb = baseRoot.TryGetProperty("Motherboard", out var bm) ? bm.GetString() : "";
                var baseMbSn = baseRoot.TryGetProperty("MotherboardSerial", out var bms) ? bms.GetString() : "";
                var currMb = currRoot.TryGetProperty("Motherboard", out var cm) ? cm.GetString() : "";
                var currMbSn = currRoot.TryGetProperty("MotherboardSerial", out var cms) ? cms.GetString() : "";

                if (baseMbSn != currMbSn || baseMb != currMb)
                {
                    driftDetected = true;
                    sb.AppendLine($"❌ [ALARM] ANAKART DEĞİŞTİRİLDİ!");
                    sb.AppendLine($"   Referans : {baseMb} (SN: {baseMbSn})");
                    sb.AppendLine($"   Anlık    : {currMb} (SN: {currMbSn})");
                }
                else
                {
                    sb.AppendLine($"✅ [OK] Anakart Orijinal: {currMb} (SN: {currMbSn})");
                }

                // 2. İşlemci
                var baseCpu = baseRoot.TryGetProperty("Processor", out var bc) ? bc.GetString() : "";
                var baseCpuId = baseRoot.TryGetProperty("ProcessorId", out var bci) ? bci.GetString() : "";
                var currCpu = currRoot.TryGetProperty("Processor", out var cc) ? cc.GetString() : "";
                var currCpuId = currRoot.TryGetProperty("ProcessorId", out var cci) ? cci.GetString() : "";

                if (baseCpuId != currCpuId)
                {
                    driftDetected = true;
                    sb.AppendLine($"❌ [ALARM] İŞLEMCİ DEĞİŞTİRİLDİ!");
                    sb.AppendLine($"   Referans : {baseCpu} (ID: {baseCpuId})");
                    sb.AppendLine($"   Anlık    : {currCpu} (ID: {currCpuId})");
                }
                else
                {
                    sb.AppendLine($"✅ [OK] İşlemci Orijinal: {currCpu}");
                }

                // 3. RAM Modülleri
                var baseRams = new List<string>();
                var currRams = new List<string>();
                if (baseRoot.TryGetProperty("Rams", out var brArray))
                {
                    foreach (var elem in brArray.EnumerateArray()) baseRams.Add(elem.GetString() ?? "");
                }
                if (currRoot.TryGetProperty("Rams", out var crArray))
                {
                    foreach (var elem in crArray.EnumerateArray()) currRams.Add(elem.GetString() ?? "");
                }

                if (baseRams.Count != currRams.Count || string.Join(";", baseRams) != string.Join(";", currRams))
                {
                    driftDetected = true;
                    sb.AppendLine($"❌ [ALARM] RAM BELLEK DEĞİŞİKLİĞİ VEYA EKSİLMESİ TESPİT EDİLDİ!");
                    sb.AppendLine($"   Referans Modül Sayısı: {baseRams.Count} adet");
                    foreach (var r in baseRams) sb.AppendLine($"     - [Referans] {r}");
                    sb.AppendLine($"   Anlık Modül Sayısı   : {currRams.Count} adet");
                    foreach (var r in currRams) sb.AppendLine($"     - [Anlık]    {r}");
                }
                else
                {
                    sb.AppendLine($"✅ [OK] RAM Bellekler Orijinal ({currRams.Count} Modül Slotu Doğrulandı)");
                }

                // 4. Diskler
                var baseDisks = new List<string>();
                var currDisks = new List<string>();
                if (baseRoot.TryGetProperty("Disks", out var bdArray))
                {
                    foreach (var elem in bdArray.EnumerateArray()) baseDisks.Add(elem.GetString() ?? "");
                }
                if (currRoot.TryGetProperty("Disks", out var cdArray))
                {
                    foreach (var elem in cdArray.EnumerateArray()) currDisks.Add(elem.GetString() ?? "");
                }

                if (string.Join(";", baseDisks) != string.Join(";", currDisks))
                {
                    driftDetected = true;
                    sb.AppendLine($"❌ [ALARM] DEPOLAMA (SSD/HDD) DEĞİŞİKLİĞİ TESPİT EDİLDİ!");
                    sb.AppendLine("   Referans Diskler:");
                    foreach (var d in baseDisks) sb.AppendLine($"     - {d}");
                    sb.AppendLine("   Anlık Diskler:");
                    foreach (var d in currDisks) sb.AppendLine($"     - {d}");
                }
                else
                {
                    sb.AppendLine($"✅ [OK] Depolama Birimleri Orijinal ({currDisks.Count} Disk Sürücüsü)");
                }

                sb.AppendLine();
                sb.AppendLine("═══════════════════════════════════════════════════════════");
                if (driftDetected)
                {
                    sb.AppendLine("SONUÇ: 🔴 ALARM - Yetkisiz donanım değişikliği veya parça eksilmesi tespit edildi!");
                    return (false, sb.ToString());
                }
                else
                {
                    sb.AppendLine("SONUÇ: 🟢 TEMİZ - Tüm donanım bileşenleri referans kayıtla %100 eşleşiyor.");
                    return (true, sb.ToString());
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Kıyaslama hatası: {ex.Message}");
                return (false, sb.ToString());
            }
        }

        // =========================================================================
        // 2. UNIVERSAL OFFLINE NIC & DRIVER ENGINE
        // =========================================================================

        public static string FindNicDriverDirectory()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 5; i++)
            {
                var candidate = Path.Combine(baseDir, "drivers", "universal_nic");
                if (Directory.Exists(candidate)) return candidate;
                var parent = Directory.GetParent(baseDir);
                if (parent == null) break;
                baseDir = parent.FullName;
            }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "drivers", "universal_nic");
        }

        public static async Task<string> InstallOfflineNicDriversAsync()
        {
            var dir = FindNicDriverDirectory();
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch { }
            }

            var infFiles = Directory.GetFiles(dir, "*.inf", SearchOption.AllDirectories);
            if (infFiles.Length == 0)
            {
                return $"[BİLGİ] Ağ sürücü dizini hazır: {dir}\r\n" +
                       "Dizinde henüz ham .INF sürücü dosyası bulunmuyor.\r\n" +
                       "Intel I219/I225/AX200 ve Realtek RTL8111/RTL8125 sürücü paketlerinizi bu klasöre bırakıp tekrar çalıştırabilirsiniz.\r\n\r\n" +
                       "PnPUtil çalıştırma komutu: pnputil.exe /add-driver \"drivers\\universal_nic\\*.inf\" /subdirs /install";
            }

            var cmd = $"pnputil.exe /add-driver \"{dir}\\*.inf\" /subdirs /install";
            var res = await RunCmdAsync(cmd, 120000);
            return res.Success ? $"PnPUtil Enjeksiyonu Başarılı:\r\n{res.Output}" : $"PnPUtil Çıktısı:\r\n{res.Output}";
        }

        public static async Task<string> ScanMissingDriversAsync()
        {
            var script = @"
                $problemDevices = Get-CimInstance Win32_PnPEntity | Where-Object { $_.ConfigManagerErrorCode -ne 0 } | Select-Object Name, DeviceID, ConfigManagerErrorCode
                if ($problemDevices) {
                    $out = @('=== SÜRÜCÜSÜ EKSİK VEYA SORUNLU AYGITLAR ===')
                    foreach ($d in $problemDevices) {
                        $out += ""⚠️ [Hata Kodu $($d.ConfigManagerErrorCode)] $($d.Name)""
                        $out += ""   Hardware ID: $($d.DeviceID)""
                    }
                    $out -join ""`r`n""
                } else {
                    ""[OK] Tebrikler! Sistemde sürücüsü eksik veya ünlemli (Code 28, 10 vb.) aygıt bulunamadı. Tüm donanımlar düzgün çalışıyor.""
                }
            ";
            var res = await RunPowerShellAsync(script);
            return res.Success ? res.Output : $"Tarama hatası: {res.Output}";
        }

        // =========================================================================
        // 3. OEM HARDWARE DETECTION & UPDATE
        // =========================================================================

        public static async Task<(string Mfg, string Model, string Serial, string Board, string Summary)> DetectOemHardwareAsync()
        {
            var script = @"
                $cs = Get-CimInstance Win32_ComputerSystem
                $bb = Get-CimInstance Win32_BaseBoard
                $bios = Get-CimInstance Win32_BIOS
                $sku = $cs.SystemSKUNumber
                $lic = (Get-CimInstance SoftwareLicensingService -ErrorAction SilentlyContinue).OA3xOriginalProductKey

                $mfg = $cs.Manufacturer
                if (-not $mfg) { $mfg = $bb.Manufacturer }
                $model = $cs.Model
                if (-not $model) { $model = $bb.Product }
                $serial = $bios.SerialNumber
                $board = ""$($bb.Manufacturer) $($bb.Product)""

                ""$mfg|||$model|||$serial|||$board|||$($bios.SMBIOSBIOSVersion)|||$sku|||$lic""
            ";

            var res = await RunPowerShellAsync(script);
            if (res.Success && !string.IsNullOrWhiteSpace(res.Output))
            {
                var parts = res.Output.Split(new[] { "|||" }, StringSplitOptions.None);
                if (parts.Length >= 7)
                {
                    var mfg = parts[0].Trim();
                    var model = parts[1].Trim();
                    var serial = parts[2].Trim();
                    var board = parts[3].Trim();
                    var biosVer = parts[4].Trim();
                    var sku = parts[5].Trim();
                    var lic = parts[6].Trim();

                    var lower = mfg.ToLowerInvariant();
                    string supportUrl = "";
                    string oemFamily = "Genel Anakart";

                    if (lower.Contains("dell"))
                    {
                        oemFamily = "Dell Business";
                        supportUrl = $"https://www.dell.com/support/home/tr-tr/product-support/servicetag/{serial}/drivers";
                    }
                    else if (lower.Contains("lenovo"))
                    {
                        oemFamily = "Lenovo Think/Idea";
                        supportUrl = $"https://pcsupport.lenovo.com/products/search?query={serial}";
                    }
                    else if (lower.Contains("hp") || lower.Contains("hewlett"))
                    {
                        oemFamily = "HP (Hewlett-Packard)";
                        supportUrl = $"https://support.hp.com/tr-tr/drivers/search?q={Uri.EscapeDataString(model)}";
                    }
                    else if (lower.Contains("asus"))
                    {
                        oemFamily = "ASUS";
                        supportUrl = "https://www.asus.com/tr/support/download-center/";
                    }
                    else if (lower.Contains("msi") || lower.Contains("micro-star"))
                    {
                        oemFamily = "MSI";
                        supportUrl = "https://tr.msi.com/support/download";
                    }

                    var sb = new StringBuilder();
                    sb.AppendLine($"Üretici (Manufacturer) : {mfg}");
                    sb.AppendLine($"Model / Ürün Adı       : {model}");
                    sb.AppendLine($"Seri No / Service Tag   : {serial}");
                    sb.AppendLine($"Anakart                 : {board}");
                    sb.AppendLine($"BIOS Sürümü            : {biosVer}");
                    if (!string.IsNullOrWhiteSpace(sku)) sb.AppendLine($"Sistem SKU             : {sku}");
                    sb.AppendLine($"OEM Aile               : {oemFamily}");
                    if (!string.IsNullOrWhiteSpace(lic)) sb.AppendLine($"MSDM OEM Lisansı       : {lic}");
                    if (!string.IsNullOrWhiteSpace(supportUrl)) sb.AppendLine($"Resmi Destek Portalı   : {supportUrl}");

                    return (mfg, model, serial, board, sb.ToString().TrimEnd());
                }
            }

            return ("Bilinmiyor", "Bilinmiyor", "Bilinmiyor", "Bilinmiyor", "Donanım tespiti yapılamadı.");
        }

        public static async Task<string> TriggerOemUpdateAsync(string mfg)
        {
            var lower = mfg.ToLowerInvariant();

            if (lower.Contains("dell"))
            {
                var candidates = new[]
                {
                    @"C:\Program Files\Dell\CommandUpdate\dcu-cli.exe",
                    @"C:\Program Files (x86)\Dell\CommandUpdate\dcu-cli.exe"
                };
                string? exe = null;
                foreach (var c in candidates)
                {
                    if (File.Exists(c)) { exe = c; break; }
                }

                if (exe != null)
                {
                    var cmd = $"\"{exe}\" /scan";
                    var r = await RunCmdAsync(cmd, 180000);
                    return $"Dell Command | Update Tarama Çıktısı:\r\n{r.Output}";
                }
                else
                {
                    return "Dell Command | Update (dcu-cli.exe) sistemde bulunamadı.\r\n" +
                           "Winget ile kurmak için: winget install --id Dell.CommandUpdate --silent\r\n" +
                           "Resmi Dell destek sayfası tarayıcınızda açılıyor...";
                }
            }
            else if (lower.Contains("lenovo"))
            {
                var candidates = new[]
                {
                    @"C:\Program Files (x86)\Lenovo\System Update\tvsu.exe",
                    @"C:\Program Files\Lenovo\System Update\tvsu.exe"
                };
                string? exe = null;
                foreach (var c in candidates)
                {
                    if (File.Exists(c)) { exe = c; break; }
                }

                if (exe != null)
                {
                    var cmd = $"\"{exe}\" /CM -search A -action LIST";
                    var r = await RunCmdAsync(cmd, 180000);
                    return $"Lenovo System Update Çıktısı:\r\n{r.Output}";
                }
                else
                {
                    return "Lenovo System Update (tvsu.exe) sistemde bulunamadı.\r\n" +
                           "Winget ile kurmak için: winget install --id Lenovo.SystemUpdate --silent\r\n" +
                           "Resmi Lenovo destek sayfası tarayıcınızda açılıyor...";
                }
            }
            else if (lower.Contains("hp") || lower.Contains("hewlett"))
            {
                return "HP Image Assistant (HPIA) kurumsal orkestratörü:\r\n" +
                       "Winget ile kurmak için: winget install --id HP.SupportAssistant --silent\r\n" +
                       "Resmi HP destek sayfası tarayıcınızda açılıyor...";
            }
            else
            {
                return $"{mfg} için üretici resmi destek sayfası açıldı. Güncel sürücüleri resmi üretici web sayfasından indirebilirsiniz.";
            }
        }

        // =========================================================================
        // 4. WINGET APPDEPLOY ENGINE
        // =========================================================================

        public static async Task<(bool Available, string Version)> CheckWingetAsync()
        {
            var res = await RunCmdAsync("winget --version");
            if (res.Success && !string.IsNullOrWhiteSpace(res.Output))
            {
                return (true, res.Output.Trim());
            }
            return (false, "");
        }

        public static async Task<string> InstallWingetPackageAsync(string packageId, string appName)
        {
            var cmd = $"winget install --id {packageId} -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";
            var res = await RunCmdAsync(cmd, 300000);
            if (res.Success)
            {
                return $"✅ [OK] {appName} ({packageId}) başarıyla kuruldu.";
            }
            else
            {
                return $"⚠️ [UYARI] {appName} ({packageId}) kurulum sonucu: {res.Output}";
            }
        }

        // =========================================================================
        // 5. EXISTING QUICK OPERATIONS
        // =========================================================================

        public static async Task<string> FlushDnsAsync()
        {
            var res = await RunPowerShellAsync("ipconfig /flushdns");
            return res.Success ? "DNS Önbelleği başarıyla temizlendi." : $"Hata: {res.Output}";
        }

        public static async Task<string> CleanTempFilesAsync()
        {
            var res = await RunPowerShellAsync("Remove-Item \"$env:TEMP\\*\" -Recurse -Force -ErrorAction SilentlyContinue; \"Geçici dosyalar temizlendi.\"");
            return res.Success ? "Geçici dosyalar (%TEMP%) temizlendi." : $"Hata: {res.Output}";
        }

        public static async Task<string> RestartSpoolerAsync()
        {
            var res = await RunPowerShellAsync("Stop-Service Spooler -Force; Start-Service Spooler; \"Yazdırma Biriktiricisi yeniden başlatıldı.\"");
            return res.Success ? "Yazdırma Biriktiricisi (Spooler) yeniden başlatıldı." : $"Hata: {res.Output}";
        }

        public static async Task<string> FixPrinterRpc11bAsync()
        {
            var script = @"
                Set-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Print' -Name 'RpcAuthnLevelExemption' -Value 0 -Type DWord -Force;
                Restart-Service -Name Spooler -Force;
                '0x11b RPC muafiyeti uygulandı ve Spooler yeniden başlatıldı.'
            ";
            var res = await RunPowerShellAsync(script);
            return res.Success ? "0x0000011b RPC Ağ Yazıcı Hatası Onarıldı." : $"Hata: {res.Output}";
        }

        public static async Task<string> FixPointAndPrint709Async()
        {
            var script = @"
                $p = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\Printers\PointAndPrint';
                if (-not (Test-Path $p)) { New-Item -Path $p -Force | Out-Null };
                Set-ItemProperty -Path $p -Name 'RestrictDriverInstallationToAdministrators' -Value 0 -Type DWord -Force;
                Set-ItemProperty -Path $p -Name 'CopyFilesPolicy' -Value 1 -Type DWord -Force;
                Restart-Service -Name Spooler -Force;
                'Point and Print 0x709 kuralları uygulandı.'
            ";
            var res = await RunPowerShellAsync(script);
            return res.Success ? "0x00000709 Point & Print Yazıcı Hatası Onarıldı." : $"Hata: {res.Output}";
        }

        public static async Task<string> DisableCopilotAndRecallAsync()
        {
            var script = @"
                $hkcu = 'HKCU:\Software\Policies\Microsoft\Windows\WindowsCopilot';
                $hklm = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot';
                if (-not (Test-Path $hkcu)) { New-Item -Path $hkcu -Force | Out-Null };
                if (-not (Test-Path $hklm)) { New-Item -Path $hklm -Force | Out-Null };
                Set-ItemProperty -Path $hkcu -Name 'TurnOffWindowsCopilot' -Value 1 -Type DWord -Force;
                Set-ItemProperty -Path $hklm -Name 'TurnOffWindowsCopilot' -Value 1 -Type DWord -Force;

                $ai = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsAI';
                if (-not (Test-Path $ai)) { New-Item -Path $ai -Force | Out-Null };
                Set-ItemProperty -Path $ai -Name 'DisableAIDataAnalysis' -Value 1 -Type DWord -Force;
                'Copilot ve Recall başarıyla devre dışı bırakıldı.'
            ";
            var res = await RunPowerShellAsync(script);
            return res.Success ? "Windows 11 Copilot & Recall AI başarıyla kapatıldı." : $"Hata: {res.Output}";
        }

        public static async Task<string> RestoreClassicContextMenuAsync()
        {
            var script = @"
                $key = 'HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32';
                New-Item -Path $key -Value '' -Force | Out-Null;
                Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue;
                Start-Process explorer;
                'Klasik sağ tık menüsü geri getirildi.'
            ";
            var res = await RunPowerShellAsync(script);
            return res.Success ? "Klasik Windows 10 Sağ Tık Menüsü etkinleştirildi." : $"Hata: {res.Output}";
        }
    }
}
