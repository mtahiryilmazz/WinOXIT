using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace WinoxIT.Services
{
    public class NativeSystemEngine
    {
        public static async Task<(bool Success, string Output)> RunPowerShellAsync(string script, int timeoutMs = 30000)
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

        public static async Task<(bool Success, string Output)> RunCmdAsync(string command)
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
                    p.WaitForExit(30000);
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

        // --- Quick Operations ---

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
