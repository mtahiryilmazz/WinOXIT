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

        public static async Task<List<InstalledDriverItem>> GetInstalledDriversAsync()
        {
            var script = @"
                Get-WindowsDriver -Online -All -ErrorAction SilentlyContinue | Where-Object { $_.Driver -like 'oem*.inf' } | 
                Select-Object Driver, ProviderName, ClassName, Version, Date | ConvertTo-Json -Compress
            ";
            var res = await RunPowerShellAsync(script);
            var list = new List<InstalledDriverItem>();
            if (res.Success && !string.IsNullOrWhiteSpace(res.Output))
            {
                try
                {
                    using var doc = JsonDocument.Parse(res.Output);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in doc.RootElement.EnumerateArray())
                        {
                            list.Add(new InstalledDriverItem
                            {
                                PublishedName = el.TryGetProperty("Driver", out var pD) ? pD.GetString() ?? "" : "",
                                ProviderName = el.TryGetProperty("ProviderName", out var pP) ? pP.GetString() ?? "" : "",
                                ClassName = el.TryGetProperty("ClassName", out var pC) ? pC.GetString() ?? "" : "",
                                Version = el.TryGetProperty("Version", out var pV) ? pV.GetString() ?? "" : "",
                                Date = el.TryGetProperty("Date", out var pDt) ? pDt.GetString() ?? "" : ""
                            });
                        }
                    }
                    else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        var el = doc.RootElement;
                        list.Add(new InstalledDriverItem
                        {
                            PublishedName = el.TryGetProperty("Driver", out var pD) ? pD.GetString() ?? "" : "",
                            ProviderName = el.TryGetProperty("ProviderName", out var pP) ? pP.GetString() ?? "" : "",
                            ClassName = el.TryGetProperty("ClassName", out var pC) ? pC.GetString() ?? "" : "",
                            Version = el.TryGetProperty("Version", out var pV) ? pV.GetString() ?? "" : "",
                            Date = el.TryGetProperty("Date", out var pDt) ? pDt.GetString() ?? "" : ""
                        });
                    }
                }
                catch { }
            }
            return list;
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

        public static async Task<string> EnableCopilotAndRecallAsync()
        {
            var script = @"
                $hkcu = 'HKCU:\Software\Policies\Microsoft\Windows\WindowsCopilot';
                $hklm = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot';
                if (Test-Path $hkcu) { Remove-ItemProperty -Path $hkcu -Name 'TurnOffWindowsCopilot' -ErrorAction SilentlyContinue };
                if (Test-Path $hklm) { Remove-ItemProperty -Path $hklm -Name 'TurnOffWindowsCopilot' -ErrorAction SilentlyContinue };

                $ai = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsAI';
                if (Test-Path $ai) { Remove-ItemProperty -Path $ai -Name 'DisableAIDataAnalysis' -ErrorAction SilentlyContinue };
                'Copilot ve Recall varsayılan durumuna geri getirildi.'
            ";
            var res = await RunPowerShellAsync(script);
            return res.Success ? "Windows 11 Copilot ve Recall varsayılan durumuna geri getirildi." : $"Hata: {res.Output}";
        }

        // =========================================================================
        // REVERSIBLE WINDOWS UTILITIES & CONTEXT MENU (SORDUM / NILESOFT STANDARD)
        // =========================================================================

        public static async Task<string> RestoreClassicContextMenuAsync()
        {
            var script = @"
                $key = 'HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32';
                if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null };
                Set-ItemProperty -Path $key -Name '(Default)' -Value '' -Force;
                Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue;
                Start-Process explorer;
                'Klasik Windows 10 sağ tık menüsü başarıyla uygulandı.'
            ";
            var res = await RunPowerShellAsync(script);
            return res.Success ? "Klasik Windows 10 sağ tık menüsü etkinleştirildi." : $"Hata: {res.Output}";
        }

        public static async Task<string> RevertModernContextMenuAsync()
        {
            var script = @"
                $key = 'HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}';
                if (Test-Path $key) { Remove-Item -Path $key -Recurse -Force -ErrorAction SilentlyContinue };
                Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue;
                Start-Process explorer;
                'Windows 11 modern sağ tık menüsüne geri dönüldü.'
            ";
            var res = await RunPowerShellAsync(script);
            return res.Success ? "Windows 11 varsayılan modern sağ tık menüsü geri yüklendi." : $"Hata: {res.Output}";
        }

        public static async Task<string> EnableClassicPhotoViewerAsync()
        {
            var script = @"
                $pvKey = 'HKCU:\Software\Classes\Applications\photoviewer.dll\shell\open\command';
                if (-not (Test-Path $pvKey)) { New-Item -Path $pvKey -Force | Out-Null };
                Set-ItemProperty -Path $pvKey -Name '(Default)' -Value 'rundll32.exe ""%ProgramFiles%\Windows Photo Viewer\PhotoViewer.dll"", ImageView_Fullscreen %1' -Force;

                $exts = @('.jpg','.jpeg','.png','.bmp','.gif','.ico','.tiff','.tif');
                foreach ($ext in $exts) {
                    $extKey = ""HKCU:\Software\Classes\$ext"";
                    if (-not (Test-Path $extKey)) { New-Item -Path $extKey -Force | Out-Null };
                    Set-ItemProperty -Path $extKey -Name '(Default)' -Value 'PhotoViewer.FileAssoc.Tiff' -Force;
                }
                'Klasik Windows Fotoğraf Görüntüleyici başarıyla etkinleştirildi.'
            ";
            var res = await RunPowerShellAsync(script);
            return res.Success ? "Klasik Windows Fotoğraf Görüntüleyici tüm görsel formatları için etkinleştirildi." : $"Hata: {res.Output}";
        }

        public static async Task<string> RevertDefaultPhotoViewerAsync()
        {
            var script = @"
                $exts = @('.jpg','.jpeg','.png','.bmp','.gif','.ico','.tiff','.tif');
                foreach ($ext in $exts) {
                    $extKey = ""HKCU:\Software\Classes\$ext"";
                    if (Test-Path $extKey) { Remove-Item -Path $extKey -Force -ErrorAction SilentlyContinue };
                }
                $pvApp = 'HKCU:\Software\Classes\Applications\photoviewer.dll';
                if (Test-Path $pvApp) { Remove-Item -Path $pvApp -Recurse -Force -ErrorAction SilentlyContinue };
                'Windows varsayılan Fotoğraflar uygulaması ilişkilendirmelerine geri dönüldü.'
            ";
            var res = await RunPowerShellAsync(script);
            return res.Success ? "Windows varsayılan Fotoğraflar uygulamasına geri dönüldü." : $"Hata: {res.Output}";
        }

        public static async Task<string> EnableTakeOwnershipMenuAsync()
        {
            var script = @"
                $fileRunas = 'HKCU:\Software\Classes\*\shell\runas';
                New-Item -Path ""$fileRunas\command"" -Force | Out-Null;
                Set-ItemProperty -Path $fileRunas -Name '(Default)' -Value 'Sahipliği Al (Take Ownership)' -Force;
                Set-ItemProperty -Path $fileRunas -Name 'NoWorkingDirectory' -Value '' -Force;
                Set-ItemProperty -Path ""$fileRunas\command"" -Name '(Default)' -Value 'cmd.exe /c takeown /f ""%1"" && icacls ""%1"" /grant *S-1-3-4:F /c /l' -Force;
                Set-ItemProperty -Path ""$fileRunas\command"" -Name 'IsolatedCommand' -Value 'cmd.exe /c takeown /f ""%1"" && icacls ""%1"" /grant *S-1-3-4:F /c /l' -Force;

                $dirRunas = 'HKCU:\Software\Classes\Directory\shell\runas';
                New-Item -Path ""$dirRunas\command"" -Force | Out-Null;
                Set-ItemProperty -Path $dirRunas -Name '(Default)' -Value 'Sahipliği Al (Take Ownership)' -Force;
                Set-ItemProperty -Path $dirRunas -Name 'NoWorkingDirectory' -Value '' -Force;
                Set-ItemProperty -Path ""$dirRunas\command"" -Name '(Default)' -Value 'cmd.exe /c takeown /f ""%1"" /r /d y && icacls ""%1"" /grant *S-1-3-4:F /t /c /l' -Force;
                Set-ItemProperty -Path ""$dirRunas\command"" -Name 'IsolatedCommand' -Value 'cmd.exe /c takeown /f ""%1"" /r /d y && icacls ""%1"" /grant *S-1-3-4:F /t /c /l' -Force;

                'Dosya ve klasör sağ tık menüsüne Sahipliği Al eklendi.'
            ";
            var res = await RunPowerShellAsync(script);
            return res.Success ? "Sağ tık 'Sahipliği Al (Take Ownership)' menüsü başarıyla eklendi." : $"Hata: {res.Output}";
        }

        public static async Task<string> DisableTakeOwnershipMenuAsync()
        {
            var script = @"
                Remove-Item -Path 'HKCU:\Software\Classes\*\shell\runas' -Recurse -Force -ErrorAction SilentlyContinue;
                Remove-Item -Path 'HKCU:\Software\Classes\Directory\shell\runas' -Recurse -Force -ErrorAction SilentlyContinue;
                'Sahipliği Al sağ tık menüsünden kaldırıldı.'
            ";
            var res = await RunPowerShellAsync(script);
            return res.Success ? "Sağ tık 'Sahipliği Al' menüsü kaldırıldı ve varsayılana dönüldü." : $"Hata: {res.Output}";
        }

        public static async Task<string> EnableAdminShellContextMenuAsync()
        {
            var script = @"
                $cmdKey = 'HKCU:\Software\Classes\Directory\Background\shell\WinoxOpenCmdAdmin';
                New-Item -Path ""$cmdKey\command"" -Force | Out-Null;
                Set-ItemProperty -Path $cmdKey -Name '(Default)' -Value 'Yönetici Komut İstemi (CMD)' -Force;
                Set-ItemProperty -Path $cmdKey -Name 'Icon' -Value 'cmd.exe' -Force;
                Set-ItemProperty -Path ""$cmdKey\command"" -Name '(Default)' -Value 'cmd.exe /s /k pushd ""%V""' -Force;

                $psKey = 'HKCU:\Software\Classes\Directory\Background\shell\WinoxOpenPsAdmin';
                New-Item -Path ""$psKey\command"" -Force | Out-Null;
                Set-ItemProperty -Path $psKey -Name '(Default)' -Value 'Yönetici PowerShell' -Force;
                Set-ItemProperty -Path $psKey -Name 'Icon' -Value 'powershell.exe' -Force;
                Set-ItemProperty -Path ""$psKey\command"" -Name '(Default)' -Value 'powershell.exe -NoExit -Command Set-Location -LiteralPath ''%V''' -Force;

                'Yönetici CMD ve PowerShell sağ tık arka plan menüsüne eklendi.'
            ";
            var res = await RunPowerShellAsync(script);
            return res.Success ? "Sağ tık 'Yönetici CMD' ve 'Yönetici PowerShell' kısayolları eklendi." : $"Hata: {res.Output}";
        }

        public static async Task<string> DisableAdminShellContextMenuAsync()
        {
            var script = @"
                Remove-Item -Path 'HKCU:\Software\Classes\Directory\Background\shell\WinoxOpenCmdAdmin' -Recurse -Force -ErrorAction SilentlyContinue;
                Remove-Item -Path 'HKCU:\Software\Classes\Directory\Background\shell\WinoxOpenPsAdmin' -Recurse -Force -ErrorAction SilentlyContinue;
                'Yönetici CMD ve PowerShell sağ tık menüsünden kaldırıldı.'
            ";
            var res = await RunPowerShellAsync(script);
            return res.Success ? "Yönetici terminal kısayolları sağ tık menüsünden kaldırıldı." : $"Hata: {res.Output}";
        }

        public static async Task<string> EnableSpoolerRestartMenuAsync()
        {
            var script = @"
                $spKey = 'HKCU:\Software\Classes\DesktopBackground\Shell\WinoxRestartSpooler';
                New-Item -Path ""$spKey\command"" -Force | Out-Null;
                Set-ItemProperty -Path $spKey -Name '(Default)' -Value '🖨️ Yazıcı Biriktiricisini Sıfırla / Başlat' -Force;
                Set-ItemProperty -Path $spKey -Name 'Icon' -Value 'shell32.dll,-16858' -Force;
                Set-ItemProperty -Path ""$spKey\command"" -Name '(Default)' -Value 'cmd.exe /c net stop spooler & net start spooler' -Force;
                'Masaüstü sağ tık menüsüne Yazıcı Sıfırla eklendi.'
            ";
            var res = await RunPowerShellAsync(script);
            return res.Success ? "Masaüstü sağ tık 'Yazıcı Biriktiricisini Sıfırla' kısayolu eklendi." : $"Hata: {res.Output}";
        }

        public static async Task<string> DisableSpoolerRestartMenuAsync()
        {
            var script = @"
                Remove-Item -Path 'HKCU:\Software\Classes\DesktopBackground\Shell\WinoxRestartSpooler' -Recurse -Force -ErrorAction SilentlyContinue;
                'Yazıcı Sıfırla sağ tık menüsünden kaldırıldı.'
            ";
            var res = await RunPowerShellAsync(script);
            return res.Success ? "Yazıcı biriktirici kısayolu masaüstü sağ tık menüsünden kaldırıldı." : $"Hata: {res.Output}";
        }

        public static async Task<string> EnablePowerMenuContextMenuAsync()
        {
            var script = @"
                $pKey = 'HKCU:\Software\Classes\DesktopBackground\Shell\WinoxPowerMenu';
                New-Item -Path $pKey -Force | Out-Null;
                Set-ItemProperty -Path $pKey -Name 'MUIVerb' -Value '⚡ Gelişmiş Güç Menüsü' -Force;
                Set-ItemProperty -Path $pKey -Name 'SubCommands' -Value 'WinoxRestart;WinoxSafeMode;WinoxUefi;WinoxShutdown' -Force;
                Set-ItemProperty -Path $pKey -Name 'Icon' -Value 'shell32.dll,-28' -Force;

                $cmdRoot = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell';
                New-Item -Path ""$cmdRoot\WinoxRestart\command"" -Force | Out-Null;
                Set-ItemProperty -Path ""$cmdRoot\WinoxRestart"" -Name '(Default)' -Value '🔄 Yeniden Başlat' -Force;
                Set-ItemProperty -Path ""$cmdRoot\WinoxRestart\command"" -Name '(Default)' -Value 'shutdown /r /t 0' -Force;

                New-Item -Path ""$cmdRoot\WinoxUefi\command"" -Force | Out-Null;
                Set-ItemProperty -Path ""$cmdRoot\WinoxUefi"" -Name '(Default)' -Value '🛠️ UEFI / BIOS Ekranına Başlat' -Force;
                Set-ItemProperty -Path ""$cmdRoot\WinoxUefi\command"" -Name '(Default)' -Value 'shutdown /r /fw /t 0' -Force;

                New-Item -Path ""$cmdRoot\WinoxSafeMode\command"" -Force | Out-Null;
                Set-ItemProperty -Path ""$cmdRoot\WinoxSafeMode"" -Name '(Default)' -Value '🛡️ Gelişmiş Kurtarma Menüsü' -Force;
                Set-ItemProperty -Path ""$cmdRoot\WinoxSafeMode\command"" -Name '(Default)' -Value 'shutdown /r /o /t 0' -Force;

                New-Item -Path ""$cmdRoot\WinoxShutdown\command"" -Force | Out-Null;
                Set-ItemProperty -Path ""$cmdRoot\WinoxShutdown"" -Name '(Default)' -Value '🛑 Bilgisayarı Kapat' -Force;
                Set-ItemProperty -Path ""$cmdRoot\WinoxShutdown\command"" -Name '(Default)' -Value 'shutdown /s /t 0' -Force;

                'Masaüstü sağ tık menüsüne Gelişmiş Güç Menüsü eklendi.'
            ";
            var res = await RunPowerShellAsync(script);
            return res.Success ? "Masaüstü sağ tık 'Gelişmiş Güç Menüsü' başarıyla eklendi." : $"Hata: {res.Output}";
        }

        public static async Task<string> DisablePowerMenuContextMenuAsync()
        {
            var script = @"
                Remove-Item -Path 'HKCU:\Software\Classes\DesktopBackground\Shell\WinoxPowerMenu' -Recurse -Force -ErrorAction SilentlyContinue;
                $cmdRoot = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell';
                Remove-Item -Path ""$cmdRoot\WinoxRestart"" -Recurse -Force -ErrorAction SilentlyContinue;
                Remove-Item -Path ""$cmdRoot\WinoxUefi"" -Recurse -Force -ErrorAction SilentlyContinue;
                Remove-Item -Path ""$cmdRoot\WinoxSafeMode"" -Recurse -Force -ErrorAction SilentlyContinue;
                Remove-Item -Path ""$cmdRoot\WinoxShutdown"" -Recurse -Force -ErrorAction SilentlyContinue;
                'Gelişmiş Güç Menüsü sağ tık menüsünden kaldırıldı.'
            ";
            var res = await RunPowerShellAsync(script);
            return res.Success ? "Gelişmiş Güç Menüsü masaüstü sağ tık menüsünden kaldırıldı." : $"Hata: {res.Output}";
        }

        public static async Task<string> ShowHiddenFilesAndExtensionsAsync()
        {
            var script = @"
                $adv = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced';
                Set-ItemProperty -Path $adv -Name 'Hidden' -Value 1 -Type DWord -Force;
                Set-ItemProperty -Path $adv -Name 'HideFileExt' -Value 0 -Type DWord -Force;
                Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue;
                Start-Process explorer;
                'Gizli dosyalar ve dosya uzantıları görünür yapıldı.'
            ";
            var res = await RunPowerShellAsync(script);
            return res.Success ? "Gizli dosyalar ve dosya uzantıları (.exe, .dll, .txt) görünür yapıldı." : $"Hata: {res.Output}";
        }

        public static async Task<string> RestoreHiddenFilesAndExtensionsAsync()
        {
            var script = @"
                $adv = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced';
                Set-ItemProperty -Path $adv -Name 'Hidden' -Value 2 -Type DWord -Force;
                Set-ItemProperty -Path $adv -Name 'HideFileExt' -Value 1 -Type DWord -Force;
                Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue;
                Start-Process explorer;
                'Dosya uzantıları ve gizli dosyalar varsayılan ayarlara döndürüldü.'
            ";
            var res = await RunPowerShellAsync(script);
            return res.Success ? "Dosya uzantıları ve gizli dosyalar varsayılan Windows ayarlarına döndürüldü." : $"Hata: {res.Output}";
        }

        public static async Task<string> EnableBingSearchAsync()
        {
            var script = @"
                $key = 'HKCU:\Software\Policies\Microsoft\Windows\Explorer';
                if (Test-Path $key) { Remove-ItemProperty -Path $key -Name 'DisableSearchBoxSuggestions' -ErrorAction SilentlyContinue };
                'Başlat menüsü Bing arama önerileri varsayılana döndürüldü.'
            ";
            var res = await RunPowerShellAsync(script);
            return res.Success ? "Başlat menüsü Bing arama önerileri varsayılana döndürüldü." : $"Hata: {res.Output}";
        }

        public static async Task<string> EnableTelemetryAsync()
        {
            var script = @"
                Set-Service DiagTrack -StartupType Automatic -ErrorAction SilentlyContinue;
                Start-Service DiagTrack -ErrorAction SilentlyContinue;
                'Windows telemetri servisleri varsayılan çalışma durumuna döndürüldü.'
            ";
            var res = await RunPowerShellAsync(script);
            return res.Success ? "Windows telemetri servisleri varsayılan çalışma durumuna döndürüldü." : $"Hata: {res.Output}";
        }

        // =========================================================================
        // 6. SYSFORGE TRANSFERRED: KATILIMSIZ (UNATTENDED) ISO & WIM İMAJ MOTORU
        // =========================================================================

        public static async Task<(bool Success, string Xml, string SavedPath)> GenerateUnattendedXmlAsync(
            string username = "WinoxAdmin",
            string password = "",
            bool bypassTpm = true,
            bool bypassSecureBoot = true,
            bool bypassRam = true,
            bool bypassCpu = true,
            string targetPath = "")
        {
            return await Task.Run(() =>
            {
                try
                {
                    var passElement = string.IsNullOrWhiteSpace(password)
                        ? "<Password><Value></Value><PlainText>true</PlainText></Password>"
                        : $"<Password><Value>{password}</Value><PlainText>true</PlainText></Password>";

                    var labCommands = new StringBuilder();
                    int order = 1;

                    if (bypassTpm)
                    {
                        labCommands.AppendLine($@"                <RunSynchronousCommand wcm:action=""add"">
                    <Order>{order++}</Order>
                    <Path>reg add HKLM\SYSTEM\Setup\LabConfig /v BypassTPMCheck /t REG_DWORD /d 1 /f</Path>
                    <Description>Bypass TPM 2.0 Check</Description>
                </RunSynchronousCommand>");
                    }
                    if (bypassSecureBoot)
                    {
                        labCommands.AppendLine($@"                <RunSynchronousCommand wcm:action=""add"">
                    <Order>{order++}</Order>
                    <Path>reg add HKLM\SYSTEM\Setup\LabConfig /v BypassSecureBootCheck /t REG_DWORD /d 1 /f</Path>
                    <Description>Bypass SecureBoot Check</Description>
                </RunSynchronousCommand>");
                    }
                    if (bypassRam)
                    {
                        labCommands.AppendLine($@"                <RunSynchronousCommand wcm:action=""add"">
                    <Order>{order++}</Order>
                    <Path>reg add HKLM\SYSTEM\Setup\LabConfig /v BypassRAMCheck /t REG_DWORD /d 1 /f</Path>
                    <Description>Bypass RAM Check</Description>
                </RunSynchronousCommand>");
                    }
                    if (bypassCpu)
                    {
                        labCommands.AppendLine($@"                <RunSynchronousCommand wcm:action=""add"">
                    <Order>{order++}</Order>
                    <Path>reg add HKLM\SYSTEM\Setup\LabConfig /v BypassCPUCheck /t REG_DWORD /d 1 /f</Path>
                    <Description>Bypass CPU Check</Description>
                </RunSynchronousCommand>");
                    }

                    // Always bypass storage check for VM / legacy drives
                    labCommands.AppendLine($@"                <RunSynchronousCommand wcm:action=""add"">
                    <Order>{order++}</Order>
                    <Path>reg add HKLM\SYSTEM\Setup\LabConfig /v BypassStorageCheck /t REG_DWORD /d 1 /f</Path>
                    <Description>Bypass Storage Check</Description>
                </RunSynchronousCommand>");

                    var xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<unattend xmlns=""urn:schemas-microsoft-com:unattend"">
    <!-- ⚡ WinoxIT Enterprise Unattended Answer File (autounattend.xml) -->
    <!-- Windows 11 TPM, SecureBoot, RAM & CPU Check Bypass Active -->
    <settings pass=""windowsPE"">
        <component name=""Microsoft-Windows-Setup"" processorArchitecture=""amd64"" publicKeyToken=""31bf3856ad364e35"" language=""neutral"" versionScope=""nonSxS"" xmlns:wcm=""http://schemas.microsoft.com/WMIConfig/2002/State"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
            <RunSynchronous>
{labCommands}
            </RunSynchronous>
            <UserData>
                <AcceptEula>true</AcceptEula>
            </UserData>
        </component>
        <component name=""Microsoft-Windows-International-Core-WinPE"" processorArchitecture=""amd64"" publicKeyToken=""31bf3856ad364e35"" language=""neutral"" versionScope=""nonSxS"" xmlns:wcm=""http://schemas.microsoft.com/WMIConfig/2002/State"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
            <SetupUILanguage>
                <UILanguage>tr-TR</UILanguage>
            </SetupUILanguage>
            <InputLocale>041f:0000041f</InputLocale>
            <SystemLocale>tr-TR</SystemLocale>
            <UILanguage>tr-TR</UILanguage>
            <UserLocale>tr-TR</UserLocale>
        </component>
    </settings>
    <settings pass=""specialize"">
        <component name=""Microsoft-Windows-Shell-Setup"" processorArchitecture=""amd64"" publicKeyToken=""31bf3856ad364e35"" language=""neutral"" versionScope=""nonSxS"" xmlns:wcm=""http://schemas.microsoft.com/WMIConfig/2002/State"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
            <TimeZone>Turkey Standard Time</TimeZone>
        </component>
    </settings>
    <settings pass=""oobeSystem"">
        <component name=""Microsoft-Windows-Shell-Setup"" processorArchitecture=""amd64"" publicKeyToken=""31bf3856ad364e35"" language=""neutral"" versionScope=""nonSxS"" xmlns:wcm=""http://schemas.microsoft.com/WMIConfig/2002/State"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
            <OOBE>
                <HideEULAPage>true</HideEULAPage>
                <HideLocalAccountScreen>true</HideLocalAccountScreen>
                <HideOnlineAccountScreens>true</HideOnlineAccountScreens>
                <HideWirelessSetupInOOBE>true</HideWirelessSetupInOOBE>
                <ProtectYourPC>3</ProtectYourPC>
            </OOBE>
            <UserAccounts>
                <LocalAccounts>
                    <LocalAccount wcm:action=""add"">
                        {passElement}
                        <Description>WinoxIT Local Administrator</Description>
                        <DisplayName>{username}</DisplayName>
                        <Group>Administrators</Group>
                        <Name>{username}</Name>
                    </LocalAccount>
                </LocalAccounts>
            </UserAccounts>
            <AutoLogon>
                {passElement}
                <Enabled>true</Enabled>
                <LogonCount>1</LogonCount>
                <Username>{username}</Username>
            </AutoLogon>
        </component>
    </settings>
</unattend>";

                    string saveFile = targetPath;
                    if (string.IsNullOrWhiteSpace(saveFile))
                    {
                        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                        var workDir = Path.Combine(desktop, "WinoxIT_ISO_Workspace");
                        if (!Directory.Exists(workDir)) Directory.CreateDirectory(workDir);
                        saveFile = Path.Combine(workDir, "autounattend.xml");
                    }

                    File.WriteAllText(saveFile, xml, Encoding.UTF8);
                    return (true, xml, saveFile);
                }
                catch (Exception ex)
                {
                    return (false, ex.Message, string.Empty);
                }
            });
        }

        public static async Task<(bool Success, string Output)> MountWimAsync(string wimPath, int index, string mountDir)
        {
            if (!File.Exists(wimPath)) return (false, $"WIM dosyası bulunamadı: {wimPath}");
            if (!Directory.Exists(mountDir)) Directory.CreateDirectory(mountDir);

            var cmd = $"dism /Mount-Image /ImageFile:\"{wimPath}\" /Index:{index} /MountDir:\"{mountDir}\"";
            return await RunCmdAsync(cmd, 180000);
        }

        public static async Task<(bool Success, string Output)> UnmountWimAsync(string mountDir, bool commit)
        {
            var flag = commit ? "/Commit" : "/Discard";
            var cmd = $"dism /Unmount-Image /MountDir:\"{mountDir}\" {flag}";
            return await RunCmdAsync(cmd, 180000);
        }

        public static async Task<(bool Success, string Output)> InjectDriversToWimAsync(string mountDir, string driversDir)
        {
            var cmd = $"dism /Image:\"{mountDir}\" /Add-Driver /Driver:\"{driversDir}\" /Recurse /ForceUnsigned";
            return await RunCmdAsync(cmd, 180000);
        }

        // =========================================================================
        // 7. SYSFORGE TRANSFERRED: LAN FİLO DAĞITIMI & PUSH AGENT
        // =========================================================================

        public static async Task<List<LanFleetNode>> DiscoverLanNodesAsync()
        {
            var script = @"
                Get-NetNeighbor -AddressFamily IPv4 -ErrorAction SilentlyContinue | 
                Where-Object { $_.State -ne 'Permanent' -and $_.IPAddress -notlike '224.*' -and $_.IPAddress -notlike '239.*' -and $_.IPAddress -notlike '255.*' -and $_.IPAddress -notlike '127.*' } | 
                Select-Object IPAddress, LinkLayerAddress, State | ConvertTo-Json -Compress
            ";

            var res = await RunPowerShellAsync(script, 10000);
            var list = new List<LanFleetNode>();

            if (res.Success && !string.IsNullOrWhiteSpace(res.Output))
            {
                try
                {
                    using var doc = JsonDocument.Parse(res.Output);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in doc.RootElement.EnumerateArray())
                        {
                            list.Add(ParseNode(el));
                        }
                    }
                    else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        list.Add(ParseNode(doc.RootElement));
                    }
                }
                catch { }
            }

            return list;

            static LanFleetNode ParseNode(JsonElement el)
            {
                var ip = el.TryGetProperty("IPAddress", out var pIp) ? pIp.GetString() ?? "" : "";
                var mac = el.TryGetProperty("LinkLayerAddress", out var pMac) ? pMac.GetString() ?? "" : "";
                var state = el.TryGetProperty("State", out var pState) ? pState.GetString() ?? "" : "";
                return new LanFleetNode { IPAddress = ip, MACAddress = mac, State = state, IsReachable = state == "Reachable" };
            }
        }

        public static async Task<(bool Reachable, string Output)> PingLanHostAsync(string ip)
        {
            var script = $"Test-Connection -ComputerName '{ip}' -Count 2 -Quiet";
            var res = await RunPowerShellAsync(script, 8000);
            var reachable = res.Success && res.Output.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
            return (reachable, reachable ? $"Cihaz ({ip}) ağda çevrimiçi ve yanıt veriyor." : $"Cihaz ({ip}) çevrimdışı veya ICMP kapalı.");
        }

        public static async Task<(bool Success, string Output)> CreateRestorePointAsync(string description = "WinoxIT Guvenli Sistem Noktasi")
        {
            var script = $@"
                try {{
                    Enable-ComputerRestore -Drive 'C:\' -ErrorAction SilentlyContinue
                    Checkpoint-Computer -Description '{description}' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop
                    'Sistem geri yükleme noktası başarıyla oluşturuldu.'
                }} catch {{
                    $err = $_.Exception.Message
                    if ($err -match '0x80042306' -or $err -match 'disabled' -or $err -match 'devre dışı') {{
                        'Sistem Koruması (System Restore) C: sürücüsü için devre dışı bırakılmış. Lütfen sysdm.cpl üzerinden Sistem Korumasını etkinleştiriniz.'
                    }} else {{
                        'Hata: ' + $err
                    }}
                }}";
            return await RunPowerShellAsync(script, 90000);
        }
    }

    public class LanFleetNode
    {
        public string IPAddress { get; set; } = string.Empty;
        public string MACAddress { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public bool IsReachable { get; set; }
    }

    public class InstalledDriverItem
    {
        public string PublishedName { get; set; } = string.Empty;
        public string ProviderName { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
    }
}
