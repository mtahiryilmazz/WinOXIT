using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

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
                    var bytes = Encoding.Unicode.GetBytes(script);
                    var encoded = Convert.ToBase64String(bytes);
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
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
        // 1. HARDWARE DRIFT & DETERMINISTIC SHA-256 FINGERPRINT (PURE NATIVE C#)
        // =========================================================================

        public static (string Cpu, string Mobo, string Disk, string Mac, string Gpu, string Hash, string Json) GetHardwareInfoNative()
        {
            string cpu = "Bilinmiyor";
            string mobo = "Bilinmiyor";
            string disk = "Bilinmiyor";
            string mac = "Bilinmiyor";
            string gpu = "Bilinmiyor";
            var rams = new List<string>();
            var disks = new List<string>();
            var nics = new List<string>();
            var gpus = new List<string>();
            int cores = Environment.ProcessorCount;

            // 1. CPU
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT ProcessorId, Name, NumberOfLogicalProcessors FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var pid = obj["ProcessorId"]?.ToString()?.Trim();
                    var name = obj["Name"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(pid)) cpu = pid;
                    else if (!string.IsNullOrWhiteSpace(name)) cpu = name;

                    if (obj["NumberOfLogicalProcessors"] != null && int.TryParse(obj["NumberOfLogicalProcessors"]?.ToString(), out int c))
                    {
                        cores = c;
                    }
                    break;
                }
            }
            catch { }

            // 2. Motherboard
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product, SerialNumber FROM Win32_BaseBoard");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var sn = obj["SerialNumber"]?.ToString()?.Trim();
                    var mfg = obj["Manufacturer"]?.ToString()?.Trim();
                    var prod = obj["Product"]?.ToString()?.Trim();

                    if (!string.IsNullOrWhiteSpace(sn) && sn != "Default string" && sn != "None" && sn != "To be filled by O.E.M.")
                    {
                        mobo = $"{mfg} {prod} (SN: {sn})".Trim();
                    }
                    else
                    {
                        mobo = $"{mfg} {prod}".Trim();
                    }
                    break;
                }
            }
            catch { }

            // 3. Disk
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Model, SerialNumber, Size, InterfaceType FROM Win32_DiskDrive");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var sn = obj["SerialNumber"]?.ToString()?.Trim();
                    var model = obj["Model"]?.ToString()?.Trim();
                    var sizeObj = obj["Size"];
                    long sizeGb = 0;
                    if (sizeObj != null && long.TryParse(sizeObj.ToString(), out long bytes))
                    {
                        sizeGb = bytes / (1024 * 1024 * 1024);
                    }
                    var iface = obj["InterfaceType"]?.ToString()?.Trim() ?? "Disk";

                    if (disk == "Bilinmiyor")
                    {
                        disk = !string.IsNullOrWhiteSpace(sn) ? $"{sn} ({model})" : model ?? "Disk";
                    }

                    disks.Add($"{model} | SN: {sn} | {sizeGb}GB ({iface})");
                }
            }
            catch { }

            // 4. Physical Network MAC
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up && 
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        !ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                        !ni.Description.Contains("Pseudo", StringComparison.OrdinalIgnoreCase))
                    {
                        var rawMac = ni.GetPhysicalAddress().ToString();
                        if (!string.IsNullOrWhiteSpace(rawMac) && rawMac.Length == 12)
                        {
                            mac = string.Join(":", Enumerable.Range(0, 6).Select(i => rawMac.Substring(i * 2, 2)));
                            nics.Add($"{ni.Name} ({ni.Description}) | MAC: {mac}");
                            break;
                        }
                    }
                }
                if (mac == "Bilinmiyor")
                {
                    foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        var rawMac = ni.GetPhysicalAddress().ToString();
                        if (!string.IsNullOrWhiteSpace(rawMac) && rawMac.Length == 12)
                        {
                            mac = string.Join(":", Enumerable.Range(0, 6).Select(i => rawMac.Substring(i * 2, 2)));
                            nics.Add($"{ni.Name} | MAC: {mac}");
                            break;
                        }
                    }
                }
            }
            catch { }

            // 5. GPU
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, PNPDeviceID FROM Win32_VideoController");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString()?.Trim();
                    var pnp = obj["PNPDeviceID"]?.ToString()?.Trim();
                    var drv = obj["DriverVersion"]?.ToString()?.Trim();

                    if (gpu == "Bilinmiyor")
                    {
                        gpu = !string.IsNullOrWhiteSpace(pnp) ? pnp : name ?? "GPU";
                    }

                    gpus.Add($"{name} (Sürücü: {drv})");
                }
            }
            catch { }

            // 6. RAM
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT BankLabel, Capacity, Manufacturer, PartNumber, SerialNumber, Speed FROM Win32_PhysicalMemory");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var bank = obj["BankLabel"]?.ToString()?.Trim() ?? "Bank";
                    var mfg = obj["Manufacturer"]?.ToString()?.Trim() ?? "";
                    var part = obj["PartNumber"]?.ToString()?.Trim() ?? "";
                    var sn = obj["SerialNumber"]?.ToString()?.Trim() ?? "";
                    var capObj = obj["Capacity"];
                    long capGb = 0;
                    if (capObj != null && long.TryParse(capObj.ToString(), out long b)) capGb = b / (1024 * 1024 * 1024);
                    var speed = obj["Speed"]?.ToString()?.Trim() ?? "";

                    rams.Add($"{bank}: {mfg} {part} | SN: {sn} | {capGb}GB @{speed}MHz");
                }
            }
            catch { }

            var profileObj = new
            {
                MachineName = Environment.MachineName,
                Motherboard = mobo,
                ProcessorId = cpu,
                Cores = cores,
                PrimaryDisk = disk,
                MacAddress = mac,
                Gpu = gpu,
                Rams = rams,
                Disks = disks,
                Nics = nics,
                Gpus = gpus
            };

            var json = JsonSerializer.Serialize(profileObj, new JsonSerializerOptions { WriteIndented = true });
            var hash = ComputeSha256(json);

            return (cpu, mobo, disk, mac, gpu, hash, json);
        }

        public static Task<(string Hash, string Json)> GetHardwareFingerprintDetailsAsync()
        {
            return Task.Run(() =>
            {
                var info = GetHardwareInfoNative();
                return (info.Hash, info.Json);
            });
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

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern int SendARP(int destIp, int srcIp, byte[] macAddr, ref uint physicalAddrLen);

        public static List<LocalNetworkAdapterItem> GetLocalNetworkAdapters()
        {
            var list = new List<LocalNetworkAdapterItem>();
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in interfaces)
                {
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;

                    var ipProps = ni.GetIPProperties();
                    var unicast = ipProps.UnicastAddresses
                        .FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork && 
                                            !IPAddress.IsLoopback(u.Address));

                    if (unicast == null) continue;

                    var ip = unicast.Address.ToString();
                    var mask = unicast.IPv4Mask?.ToString() ?? "255.255.255.0";
                    var gateway = ipProps.GatewayAddresses
                        .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "-";

                    var rawMac = ni.GetPhysicalAddress().ToString();
                    string formattedMac = "-";
                    if (!string.IsNullOrWhiteSpace(rawMac) && rawMac.Length == 12)
                    {
                        formattedMac = string.Join(":", Enumerable.Range(0, 6).Select(i => rawMac.Substring(i * 2, 2)));
                    }

                    string speedStr = "Bilinmiyor";
                    if (ni.Speed > 0)
                    {
                        if (ni.Speed >= 1_000_000_000)
                            speedStr = $"{ni.Speed / 1_000_000_000.0:0.##} Gbps";
                        else
                            speedStr = $"{ni.Speed / 1_000_000.0:0.##} Mbps";
                    }

                    string typeStr = ni.NetworkInterfaceType switch
                    {
                        NetworkInterfaceType.Ethernet => "Ethernet (Kablolu)",
                        NetworkInterfaceType.Wireless80211 => "Wi-Fi (Kablosuz)",
                        NetworkInterfaceType.GigabitEthernet => "Gigabit Ethernet",
                        _ => ni.NetworkInterfaceType.ToString()
                    };

                    // Compute subnet range
                    string rangeStart = "192.168.1.1";
                    string rangeEnd = "192.168.1.254";
                    var octets = ip.Split('.');
                    if (octets.Length == 4)
                    {
                        rangeStart = $"{octets[0]}.{octets[1]}.{octets[2]}.1";
                        rangeEnd = $"{octets[0]}.{octets[1]}.{octets[2]}.254";
                    }

                    list.Add(new LocalNetworkAdapterItem
                    {
                        Id = ni.Id,
                        Name = ni.Name,
                        Description = ni.Description,
                        InterfaceType = typeStr,
                        IpAddress = ip,
                        SubnetMask = mask,
                        Gateway = gateway,
                        MacAddress = formattedMac,
                        Speed = speedStr,
                        SubnetRangeStart = rangeStart,
                        SubnetRangeEnd = rangeEnd
                    });
                }
            }
            catch { }

            return list;
        }

        public static async Task<List<LanHostNode>> ScanSubnetRangeAsync(
            string startIpStr, 
            string endIpStr, 
            IProgress<(int Current, int Total, LanHostNode? Found)>? progress = null, 
            CancellationToken ct = default)
        {
            var results = new List<LanHostNode>();

            if (!IPAddress.TryParse(startIpStr, out var startIp) || !IPAddress.TryParse(endIpStr, out var endIp))
            {
                return results;
            }

            var startBytes = startIp.GetAddressBytes();
            var endBytes = endIp.GetAddressBytes();
            if (startBytes.Length != 4 || endBytes.Length != 4) return results;

            int startNum = (startBytes[0] << 24) | (startBytes[1] << 16) | (startBytes[2] << 8) | startBytes[3];
            int endNum = (endBytes[0] << 24) | (endBytes[1] << 16) | (endBytes[2] << 8) | endBytes[3];
            if (startNum > endNum) (startNum, endNum) = (endNum, startNum);

            int total = endNum - startNum + 1;
            if (total > 1024) total = 1024; // safety cap

            var ipList = new List<string>();
            for (int i = 0; i < total; i++)
            {
                int current = startNum + i;
                byte[] b = {
                    (byte)((current >> 24) & 0xFF),
                    (byte)((current >> 16) & 0xFF),
                    (byte)((current >> 8) & 0xFF),
                    (byte)(current & 0xFF)
                };
                ipList.Add(new IPAddress(b).ToString());
            }

            int scannedCount = 0;
            // 50 concurrent workers: SendARP is 0.5ms so scanning 254 IPs takes only ~1.5s
            using var semaphore = new SemaphoreSlim(50);

            var tasks = ipList.Select(async ip =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    if (ct.IsCancellationRequested) return;

                    var node = await FastProbeHostAsync(ip, ct);
                    lock (results)
                    {
                        results.Add(node);
                    }

                    int count = Interlocked.Increment(ref scannedCount);
                    progress?.Report((count, total, node));
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            return results.OrderBy(r => IpToLong(r.IPAddress)).ToList();

            static long IpToLong(string ip)
            {
                if (IPAddress.TryParse(ip, out var addr))
                {
                    var bytes = addr.GetAddressBytes();
                    if (bytes.Length == 4)
                        return ((long)bytes[0] << 24) | ((long)bytes[1] << 16) | ((long)bytes[2] << 8) | bytes[3];
                }
                return 0;
            }
        }

        private static async Task<LanHostNode> FastProbeHostAsync(string ipStr, CancellationToken ct)
        {
            if (RustCoreBridge.IsAvailable())
            {
                try
                {
                    var rustNode = await Task.Run(() => RustCoreBridge.ProbeHostFast(ipStr), ct);
                    if (rustNode != null) return rustNode;
                }
                catch { }
            }

            var node = new LanHostNode
            {
                IPAddress = ipStr,
                Hostname = "-",
                NetBiosName = "-",
                Workgroup = "-",
                Vendor = "-",
                OpenPorts = "-",
                MACAddress = "-",
                Status = "⚪ Boşta (Kullanılabilir)",
                StatusCode = "Idle",
                Latency = "-",
                DeviceType = "Serbest IP",
                IsOnline = false
            };

            if (!IPAddress.TryParse(ipStr, out var ipAddr)) return node;

            // STAGE 1: Win32 Layer-2 SendARP (sub-millisecond speed)
            bool arpSuccess = false;
            string mac = "";

            try
            {
                byte[] macBytes = new byte[6];
                uint len = (uint)macBytes.Length;
                int dest = BitConverter.ToInt32(ipAddr.GetAddressBytes(), 0);
                int arpRes = SendARP(dest, 0, macBytes, ref len);
                if (arpRes == 0 && len == 6)
                {
                    arpSuccess = true;
                    mac = string.Join(":", macBytes.Select(b => b.ToString("X2")));
                }
            }
            catch { }

            // Ping fallback if ARP failed (e.g. routed across different interface)
            bool pingSuccess = false;
            long pingMs = 0;
            if (!arpSuccess)
            {
                try
                {
                    using var pinger = new Ping();
                    var reply = await pinger.SendPingAsync(ipAddr, 120);
                    if (reply != null && reply.Status == IPStatus.Success)
                    {
                        pingSuccess = true;
                        pingMs = reply.RoundtripTime;
                    }
                }
                catch { }
            }
            else
            {
                // Quick ping to measure latency for online host
                try
                {
                    using var pinger = new Ping();
                    var reply = await pinger.SendPingAsync(ipAddr, 100);
                    if (reply != null && reply.Status == IPStatus.Success)
                    {
                        pingSuccess = true;
                        pingMs = reply.RoundtripTime;
                    }
                }
                catch { }
            }

            // If neither answered, host is idle. RETURN IMMEDIATELY (no NetBIOS/port delay)!
            if (!arpSuccess && !pingSuccess)
            {
                return node;
            }

            // HOST IS ONLINE
            node.IsOnline = true;
            node.MACAddress = !string.IsNullOrEmpty(mac) ? mac : "ARP Gizli/Yönlendirildi";

            if (pingSuccess)
            {
                node.Status = "🟢 Açık (Online)";
                node.StatusCode = "Online";
                node.Latency = $"{pingMs} ms";
            }
            else
            {
                node.Status = "🟡 Korumalı (Firewall)";
                node.StatusCode = "Firewall";
                node.Latency = "ARP Yanıtı (ICMP Kapalı)";
            }

            // STAGE 2: OUI MAC Vendor Lookup
            string vendor = LookupMacVendor(node.MACAddress);
            node.Vendor = vendor;

            // STAGE 3: Run NetBIOS (UDP 137), TCP Port Prober, and Reverse DNS concurrently
            var netbiosTask = QueryNetBiosAsync(ipAddr, 140);
            var portsTask = ProbeCommonPortsAsync(ipAddr, 75);
            var dnsTask = ResolveHostnameFastAsync(ipAddr, 150);

            await Task.WhenAll(netbiosTask, portsTask, dnsTask);

            var (nbName, workgroup) = await netbiosTask;
            var openPortsList = await portsTask;
            var hostname = await dnsTask;

            if (!string.IsNullOrWhiteSpace(nbName))
            {
                node.NetBiosName = nbName;
                node.Hostname = nbName;
            }
            else if (!string.IsNullOrWhiteSpace(hostname))
            {
                node.Hostname = hostname;
            }

            if (!string.IsNullOrWhiteSpace(workgroup))
            {
                node.Workgroup = workgroup;
            }

            node.HasSmb = openPortsList.Contains(445) || openPortsList.Contains(139);
            node.HasRdp = openPortsList.Contains(3389);
            node.HasHttp = openPortsList.Contains(80) || openPortsList.Contains(443);
            node.HasSsh = openPortsList.Contains(22);

            if (openPortsList.Count > 0)
            {
                node.OpenPorts = string.Join(", ", openPortsList);
            }
            else
            {
                node.OpenPorts = "-";
            }

            // STAGE 4: Detailed Device Type Classification
            node.DeviceType = ClassifyDeviceType(ipAddr, node.MACAddress, vendor, node.Hostname, openPortsList, node.HasSmb, node.HasRdp);

            return node;
        }

        private static async Task<(string Name, string Workgroup)> QueryNetBiosAsync(IPAddress ip, int timeoutMs)
        {
            try
            {
                using var udp = new UdpClient();
                udp.Client.SendTimeout = timeoutMs;
                udp.Client.ReceiveTimeout = timeoutMs;

                byte[] query = new byte[]
                {
                    0x80, 0x94, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x20, 0x43, 0x4B, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41,
                    0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41,
                    0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x00, 0x00, 0x21,
                    0x00, 0x01
                };

                var endpoint = new IPEndPoint(ip, 137);
                await udp.SendAsync(query, query.Length, endpoint);

                var receiveTask = udp.ReceiveAsync();
                var completed = await Task.WhenAny(receiveTask, Task.Delay(timeoutMs));
                if (completed == receiveTask && !receiveTask.IsFaulted)
                {
                    var res = await receiveTask;
                    var data = res.Buffer;
                    if (data.Length > 56)
                    {
                        int numNames = data[56];
                        string compName = "";
                        string workgroup = "";

                        int offset = 57;
                        for (int i = 0; i < numNames && offset + 18 <= data.Length; i++)
                        {
                            string rawName = Encoding.ASCII.GetString(data, offset, 15).Trim();
                            byte type = data[offset + 15];
                            short flags = (short)((data[offset + 16] << 8) | data[offset + 17]);
                            bool isGroup = (flags & 0x0080) != 0;

                            if (isGroup && string.IsNullOrEmpty(workgroup))
                            {
                                workgroup = rawName;
                            }
                            else if (!isGroup && string.IsNullOrEmpty(compName) && type == 0x00)
                            {
                                compName = rawName;
                            }
                            offset += 18;
                        }

                        return (compName, workgroup);
                    }
                }
            }
            catch { }
            return ("", "");
        }

        private static async Task<List<int>> ProbeCommonPortsAsync(IPAddress ip, int timeoutMs)
        {
            var open = new List<int>();
            int[] testPorts = { 445, 3389, 80, 443, 22, 5060, 9100 };

            var tasks = testPorts.Select(async port =>
            {
                try
                {
                    using var client = new TcpClient();
                    using var cts = new CancellationTokenSource(timeoutMs);
                    await client.ConnectAsync(ip, port, cts.Token);
                    if (client.Connected)
                    {
                        lock (open) { open.Add(port); }
                    }
                }
                catch { }
            });

            await Task.WhenAll(tasks);
            return open.OrderBy(x => x).ToList();
        }

        private static async Task<string> ResolveHostnameFastAsync(IPAddress ip, int timeoutMs)
        {
            try
            {
                var dnsTask = Dns.GetHostEntryAsync(ip);
                var completed = await Task.WhenAny(dnsTask, Task.Delay(timeoutMs));
                if (completed == dnsTask && !dnsTask.IsFaulted)
                {
                    var entry = await dnsTask;
                    if (!string.IsNullOrWhiteSpace(entry.HostName))
                        return entry.HostName;
                }
            }
            catch { }
            return "";
        }

        private static string LookupMacVendor(string mac)
        {
            if (string.IsNullOrWhiteSpace(mac) || mac.Length < 8) return "Bilinmiyor";
            var prefix = mac.Substring(0, 8).ToUpperInvariant();

            // Yealink / Grandstream / Polycom (IP Phone)
            if (prefix.StartsWith("00:15:65") || prefix.StartsWith("80:5E:C0") || prefix.StartsWith("00:26:8B")) return "Yealink";
            if (prefix.StartsWith("00:0B:82")) return "Grandstream";
            if (prefix.StartsWith("00:04:F2") || prefix.StartsWith("00:10:94") || prefix.StartsWith("48:25:67")) return "Polycom";

            // Firewalls
            if (prefix.StartsWith("00:09:0F") || prefix.StartsWith("70:4C:A5") || prefix.StartsWith("90:6C:AC")) return "Fortinet";
            if (prefix.StartsWith("00:1B:17") || prefix.StartsWith("08:30:6B") || prefix.StartsWith("D4:1D:71")) return "Palo Alto Networks";
            if (prefix.StartsWith("00:1A:8C") || prefix.StartsWith("7C:5A:1C")) return "Sophos";

            // Routers / Switches
            if (prefix.StartsWith("00:00:0C") || prefix.StartsWith("00:01:42") || prefix.StartsWith("F4:4B:2A") || prefix.StartsWith("70:69:79")) return "Cisco";
            if (prefix.StartsWith("48:8F:5A") || prefix.StartsWith("64:D1:54") || prefix.StartsWith("CC:2D:E0") || prefix.StartsWith("D4:CA:6D")) return "MikroTik";
            if (prefix.StartsWith("00:27:22") || prefix.StartsWith("74:83:C2") || prefix.StartsWith("F0:9F:C2")) return "Ubiquiti UniFi";
            if (prefix.StartsWith("50:3E:AA") || prefix.StartsWith("74:56:3C") || prefix.StartsWith("F4:EC:38") || prefix.StartsWith("AC:84:C6") || prefix.StartsWith("C0:06:C3")) return "TP-Link";
            if (prefix.StartsWith("EC:43:F6") || prefix.StartsWith("28:28:5D")) return "Keenetic";
            if (prefix.StartsWith("00:13:49") || prefix.StartsWith("50:67:AE")) return "Zyxel";
            if (prefix.StartsWith("00:09:5B") || prefix.StartsWith("A0:04:60") || prefix.StartsWith("C4:04:15")) return "Netgear";
            if (prefix.StartsWith("00:15:F2") || prefix.StartsWith("18:31:BF") || prefix.StartsWith("AC:22:0B")) return "ASUS";

            // Servers & Enterprise
            if (prefix.StartsWith("00:14:22") || prefix.StartsWith("14:FE:B5") || prefix.StartsWith("B8:2A:72") || prefix.StartsWith("F8:BC:12")) return "Dell Technologies";
            if (prefix.StartsWith("00:01:E6") || prefix.StartsWith("28:92:4A") || prefix.StartsWith("3C:D9:2B") || prefix.StartsWith("C4:34:6B")) return "HPE / HP";
            if (prefix.StartsWith("00:25:90") || prefix.StartsWith("00:30:48") || prefix.StartsWith("AC:1F:6B")) return "Supermicro";

            // Printers
            if (prefix.StartsWith("00:00:85") || prefix.StartsWith("70:8A:09") || prefix.StartsWith("AC:64:17")) return "Canon";
            if (prefix.StartsWith("00:00:48") || prefix.StartsWith("44:D2:44") || prefix.StartsWith("AC:18:26")) return "Epson";
            if (prefix.StartsWith("00:80:77") || prefix.StartsWith("30:05:5C") || prefix.StartsWith("80:69:1A")) return "Brother";
            if (prefix.StartsWith("00:00:AA") || prefix.StartsWith("00:08:74")) return "Xerox";
            if (prefix.StartsWith("00:00:87") || prefix.StartsWith("40:B0:34")) return "Kyocera";

            // Storage / NAS
            if (prefix.StartsWith("00:11:32")) return "Synology NAS";
            if (prefix.StartsWith("00:08:9B") || prefix.StartsWith("24:5E:BE")) return "QNAP NAS";

            // Hypervisors & VMs
            if (prefix.StartsWith("00:05:69") || prefix.StartsWith("00:0C:29") || prefix.StartsWith("00:50:56")) return "VMware";
            if (prefix.StartsWith("08:00:27")) return "Oracle VirtualBox";

            // Mobile & Smart Devices
            if (prefix.StartsWith("F4:0F:24") || prefix.StartsWith("BC:52:B7") || prefix.StartsWith("3C:D0:F8") || prefix.StartsWith("A4:C3:61") || prefix.StartsWith("88:66:5A")) return "Apple";
            if (prefix.StartsWith("08:D4:2B") || prefix.StartsWith("34:23:87") || prefix.StartsWith("84:25:DB") || prefix.StartsWith("FC:03:9F")) return "Samsung";
            if (prefix.StartsWith("14:F6:5A") || prefix.StartsWith("38:A4:ED") || prefix.StartsWith("AC:C1:EE")) return "Xiaomi";
            if (prefix.StartsWith("00:1E:10") || prefix.StartsWith("48:46:FB") || prefix.StartsWith("E8:CD:2D")) return "Huawei";

            // IoT & Embedded
            if (prefix.StartsWith("18:FE:34") || prefix.StartsWith("24:0A:C4") || prefix.StartsWith("30:AE:A4") || prefix.StartsWith("AC:67:B2")) return "Espressif (ESP)";
            if (prefix.StartsWith("B8:27:EB") || prefix.StartsWith("DC:A6:32") || prefix.StartsWith("E4:5F:01")) return "Raspberry Pi";

            // PC Chips
            if (prefix.StartsWith("00:02:B3") || prefix.StartsWith("00:15:17") || prefix.StartsWith("00:1E:67")) return "Intel";
            if (prefix.StartsWith("00:E0:4C") || prefix.StartsWith("52:54:00")) return "Realtek";

            return "Ağ Arayüzü (" + prefix + ")";
        }

        private static string ClassifyDeviceType(IPAddress ip, string mac, string vendor, string hostname, List<int> openPorts, bool hasSmb, bool hasRdp)
        {
            var hostLower = hostname.ToLowerInvariant();
            var lastOctet = ip.GetAddressBytes()[3];
            bool isGateway = lastOctet == 1 || lastOctet == 254;

            // 1. IP Telefon (VoIP)
            if (vendor.Contains("Yealink") || vendor.Contains("Grandstream") || vendor.Contains("Polycom") || 
                openPorts.Contains(5060) || hostLower.Contains("phone") || hostLower.Contains("sip") || hostLower.Contains("voip"))
            {
                return $"📞 IP Telefon (VoIP - {vendor})";
            }

            // 2. Ağ Yazıcısı / Fotokopi
            if (openPorts.Contains(9100) || openPorts.Contains(515) || openPorts.Contains(631) ||
                vendor.Contains("Canon") || vendor.Contains("Epson") || vendor.Contains("Brother") || vendor.Contains("Xerox") || vendor.Contains("Kyocera") ||
                hostLower.Contains("printer") || hostLower.Contains("yazici") || hostLower.Contains("canon") || hostLower.Contains("hp-print"))
            {
                return $"🖨️ Ağ Yazıcısı ({vendor})";
            }

            // 3. Donanım Güvenlik Duvarı (Firewall / UTM)
            if (vendor.Contains("Fortinet") || vendor.Contains("Palo Alto") || vendor.Contains("Sophos") || hostLower.Contains("firewall") || hostLower.Contains("utm"))
            {
                return $"🛡️ Donanım Güvenlik Duvarı ({vendor})";
            }

            // 4. NAS Depolama Sunucusu
            if (vendor.Contains("Synology") || vendor.Contains("QNAP") || hostLower.Contains("nas") || hostLower.Contains("truenas"))
            {
                return $"💾 NAS Depolama Sunucusu ({vendor})";
            }

            // 5. Sanal Makine / Hypervisor
            if (vendor.Contains("VMware") || vendor.Contains("VirtualBox") || hostLower.Contains("vmware") || hostLower.Contains("proxmox") || hostLower.Contains("esxi"))
            {
                return $"📦 Sanal Makine / VM ({vendor})";
            }

            // 6. Router / Modem / Ağ Geçidi
            if (isGateway || vendor.Contains("Keenetic") || vendor.Contains("ASUS") || vendor.Contains("Netgear") || vendor.Contains("Zyxel") || vendor.Contains("TP-Link") && (openPorts.Contains(80) || openPorts.Contains(443)))
            {
                if (isGateway || openPorts.Contains(80) || openPorts.Contains(443))
                    return $"🌐 Router / Modem / Gateway ({vendor})";
                else
                    return $"🔀 Dağıtıcı / Switch ({vendor})";
            }

            // 7. Yönetilebilir Ağ Switch
            if (vendor.Contains("Cisco") || vendor.Contains("MikroTik") || vendor.Contains("Ubiquiti") || hostLower.Contains("switch") || hostLower.Contains("sw-"))
            {
                return $"🔀 Yönetilebilir Switch ({vendor})";
            }

            // 8. Akıllı Telefon / Tablet
            if (vendor.Contains("Apple") || vendor.Contains("Samsung") || vendor.Contains("Xiaomi") || vendor.Contains("Huawei"))
            {
                if (!hasSmb && !hasRdp && !openPorts.Contains(22) && !openPorts.Contains(80))
                    return $"📱 Akıllı Telefon / Mobil ({vendor})";
            }

            // 9. IoT / Akıllı Cihaz
            if (vendor.Contains("Espressif") || vendor.Contains("Raspberry") || hostLower.Contains("esp") || hostLower.Contains("raspberry") || hostLower.Contains("smart"))
            {
                return $"⚡ IoT / Akıllı Cihaz ({vendor})";
            }

            // 10. Sunucu (Server)
            if (vendor.Contains("Supermicro") || vendor.Contains("Dell") && (openPorts.Contains(22) || openPorts.Contains(3389) || openPorts.Contains(443)) ||
                hostLower.Contains("server") || hostLower.Contains("dc-") || hostLower.Contains("srv") || hostLower.Contains("sunucu"))
            {
                return $"🖥️ Sunucu / Server ({vendor})";
            }

            // 11. Windows Bilgisayar (PC)
            if (hasSmb || hasRdp || hostLower.Contains("desktop") || hostLower.Contains("laptop") || hostLower.Contains("pc"))
            {
                string tag = hasRdp ? "RDP+SMB" : "SMB Aktif";
                return $"💻 Windows Bilgisayar ({tag} - {vendor})";
            }

            // 12. Linux / Unix
            if (openPorts.Contains(22))
            {
                return $"🐧 Linux / Unix Cihaz ({vendor})";
            }

            // Default
            return $"📡 Ağ Cihazı ({vendor})";
        }

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

        // =========================================================================
        // 8. WINDOWS BAŞLANGIÇ YÖNETİCİSİ (ETKİNLEŞTİR / DEVRE DIŞI BIRAK / KALDIR)
        // =========================================================================

        private const string DisabledStartupRegKey = @"Software\WinoxIT\DisabledStartup";

        public static Task<List<StartupItemModel>> GetStartupItemsAsync()
        {
            return Task.Run(() =>
            {
                var list = new List<StartupItemModel>();

                // 1. HKCU\Software\Microsoft\Windows\CurrentVersion\Run
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                    if (key != null)
                    {
                        foreach (var name in key.GetValueNames())
                        {
                            var cmd = key.GetValue(name)?.ToString() ?? "";
                            list.Add(new StartupItemModel
                            {
                                Name = name,
                                Command = cmd,
                                Location = @"HKCU\...\CurrentVersion\Run",
                                Status = "🟢 Etkin",
                                IsEnabled = true,
                                Scope = "HKCU"
                            });
                        }
                    }
                }
                catch { }

                // 2. HKLM\Software\Microsoft\Windows\CurrentVersion\Run
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                    if (key != null)
                    {
                        foreach (var name in key.GetValueNames())
                        {
                            var cmd = key.GetValue(name)?.ToString() ?? "";
                            list.Add(new StartupItemModel
                            {
                                Name = name,
                                Command = cmd,
                                Location = @"HKLM\...\CurrentVersion\Run",
                                Status = "🟢 Etkin",
                                IsEnabled = true,
                                Scope = "HKLM"
                            });
                        }
                    }
                }
                catch { }

                // 3. HKLM WOW6432Node
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run");
                    if (key != null)
                    {
                        foreach (var name in key.GetValueNames())
                        {
                            var cmd = key.GetValue(name)?.ToString() ?? "";
                            if (!list.Any(x => x.Name == name))
                            {
                                list.Add(new StartupItemModel
                                {
                                    Name = name,
                                    Command = cmd,
                                    Location = @"HKLM\WOW6432Node\...\Run",
                                    Status = "🟢 Etkin",
                                    IsEnabled = true,
                                    Scope = "HKLM"
                                });
                            }
                        }
                    }
                }
                catch { }

                // 4. Disabled items in HKCU\Software\WinoxIT\DisabledStartup
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(DisabledStartupRegKey);
                    if (key != null)
                    {
                        foreach (var name in key.GetValueNames())
                        {
                            var raw = key.GetValue(name)?.ToString() ?? "";
                            string scope = "HKCU";
                            string cmd = raw;
                            if (raw.Contains("|||"))
                            {
                                var parts = raw.Split(new[] { "|||" }, 2, StringSplitOptions.None);
                                scope = parts[0];
                                cmd = parts[1];
                            }

                            list.Add(new StartupItemModel
                            {
                                Name = name,
                                Command = cmd,
                                Location = scope == "HKLM" ? @"HKLM (Devre Dışı)" : @"HKCU (Devre Dışı)",
                                Status = "⚪ Devre Dışı",
                                IsEnabled = false,
                                Scope = scope
                            });
                        }
                    }
                }
                catch { }

                return list;
            });
        }

        public static Task<(bool Success, string Message)> DisableStartupItemAsync(StartupItemModel item)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (!item.IsEnabled) return (true, "Öğe zaten devre dışı.");

                    // 1. Save to DisabledStartup
                    using (var dKey = Registry.CurrentUser.CreateSubKey(DisabledStartupRegKey))
                    {
                        if (dKey == null) return (false, "WinoxIT devre dışı kayıt defteri anahtarı açılamadı.");
                        dKey.SetValue(item.Name, $"{item.Scope}|||{item.Command}");
                    }

                    // 2. Remove from active Run key
                    if (item.Scope == "HKLM")
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                        key?.DeleteValue(item.Name, false);
                    }
                    else
                    {
                        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                        key?.DeleteValue(item.Name, false);
                    }

                    return (true, $"'{item.Name}' başlangıç öğesi başarıyla devre dışı bırakıldı.");
                }
                catch (Exception ex)
                {
                    return (false, $"Hata: {ex.Message}");
                }
            });
        }

        public static Task<(bool Success, string Message)> EnableStartupItemAsync(StartupItemModel item)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (item.IsEnabled) return (true, "Öğe zaten etkin.");

                    // 1. Put back to active Run key
                    if (item.Scope == "HKLM")
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                        if (key != null)
                        {
                            key.SetValue(item.Name, item.Command);
                        }
                        else
                        {
                            // fallback to HKCU if not admin
                            using var uKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                            uKey?.SetValue(item.Name, item.Command);
                        }
                    }
                    else
                    {
                        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                        key?.SetValue(item.Name, item.Command);
                    }

                    // 2. Remove from DisabledStartup
                    using (var dKey = Registry.CurrentUser.OpenSubKey(DisabledStartupRegKey, true))
                    {
                        dKey?.DeleteValue(item.Name, false);
                    }

                    return (true, $"'{item.Name}' başlangıç öğesi başarıyla yeniden etkinleştirildi.");
                }
                catch (Exception ex)
                {
                    return (false, $"Hata: {ex.Message}");
                }
            });
        }

        public static Task<(bool Success, string Message)> RemoveStartupItemAsync(StartupItemModel item)
        {
            return Task.Run(() =>
            {
                try
                {
                    // Remove from Run keys
                    try
                    {
                        using var uKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                        uKey?.DeleteValue(item.Name, false);
                    }
                    catch { }

                    try
                    {
                        using var mKey = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                        mKey?.DeleteValue(item.Name, false);
                    }
                    catch { }

                    // Remove from Disabled store
                    try
                    {
                        using var dKey = Registry.CurrentUser.OpenSubKey(DisabledStartupRegKey, true);
                        dKey?.DeleteValue(item.Name, false);
                    }
                    catch { }

                    return (true, $"'{item.Name}' başlangıç kaydı sistemden tamamen silindi.");
                }
                catch (Exception ex)
                {
                    return (false, $"Hata: {ex.Message}");
                }
            });
        }
    }

    public class LocalNetworkAdapterItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string InterfaceType { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string SubnetMask { get; set; } = string.Empty;
        public string Gateway { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public string Speed { get; set; } = string.Empty;
        public string SubnetRangeStart { get; set; } = string.Empty;
        public string SubnetRangeEnd { get; set; } = string.Empty;
        public string DisplayText => $"[{InterfaceType}] {Name} ({IpAddress}) - {Speed}";
    }

    public class LanHostNode
    {
        public string IPAddress { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public string MACAddress { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string StatusCode { get; set; } = string.Empty; // Online, Firewall, Idle
        public string Latency { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
        public string Vendor { get; set; } = string.Empty;
        public string NetBiosName { get; set; } = string.Empty;
        public string Workgroup { get; set; } = string.Empty;
        public string OpenPorts { get; set; } = string.Empty;
        public bool HasSmb { get; set; }
        public bool HasRdp { get; set; }
        public bool HasHttp { get; set; }
        public bool HasSsh { get; set; }
        public bool IsOnline { get; set; }
    }

    public class StartupItemModel
    {
        public string Name { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Status { get; set; } = "🟢 Etkin";
        public bool IsEnabled { get; set; } = true;
        public string Scope { get; set; } = "HKCU";
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
