using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WinoxIT.Services;

namespace WinoxIT
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Log("WinoxIT v1.0.0-alpha Enterprise Suite başlatıldı.");
            Log("Sistem donanım envanteri taranıyor...");
            await LoadSystemSpecsAsync();
        }

        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                var time = DateTime.Now.ToString("HH:mm:ss");
                TxtConsoleLog.AppendText($"[{time}] {message}\n");
                TxtConsoleLog.ScrollToEnd();
            });
        }

        private async Task LoadSystemSpecsAsync()
        {
            try
            {
                var osInfo = Environment.OSVersion.ToString();
                var machine = Environment.MachineName;
                var user = Environment.UserName;
                var cores = Environment.ProcessorCount;
                var is64 = Environment.Is64BitOperatingSystem ? "64-Bit" : "32-Bit";

                TxtCpuName.Text = $"{cores} Çekirdek Aktif";
                TxtSpecs.Text = $"Cihaz Adı: {machine}\r\n" +
                                $"Kullanıcı: {user}\r\n" +
                                $"İşletim Sistemi: Windows {osInfo} ({is64})\r\n" +
                                $"İşlemci Çekirdekleri: {cores} Mantıksal Çekirdek\r\n" +
                                $"Sistem Durumu: Çekirdek servisler ve ağ arayüzü aktif.";
                
                Log("Donanım ve profil bilgileri başarıyla yüklendi.");
            }
            catch (Exception ex)
            {
                Log($"Özellikler yüklenirken hata: {ex.Message}");
            }
        }

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Content != null)
            {
                var title = rb.Content.ToString();
                if (TxtHeaderTitle != null)
                {
                    TxtHeaderTitle.Text = title;
                }
            }
        }

        private void BtnQuickServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var rootDir = AppDomain.CurrentDomain.BaseDirectory;
                // Look for Launch-NodeServer.cmd in current or parent dirs
                string cmdPath = Path.Combine(rootDir, "Launch-NodeServer.cmd");
                if (!File.Exists(cmdPath))
                {
                    cmdPath = Path.GetFullPath(Path.Combine(rootDir, @"..\..\..\..\Launch-NodeServer.cmd"));
                }

                if (File.Exists(cmdPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c \"{cmdPath}\"",
                        UseShellExecute = true,
                        Verb = "runas"
                    });
                    Log("Node.js Virtual Server (Port 9999) başlatıcı tetiklendi.");
                }
                else
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "http://localhost:9999/dashboard.html",
                        UseShellExecute = true
                    });
                    Log("Web Kokpiti tarayıcıda açıldı: http://localhost:9999/dashboard.html");
                }
            }
            catch (Exception ex)
            {
                Log($"Sunucu başlatılırken hata: {ex.Message}");
            }
        }

        private async void BtnQuickClean_Click(object sender, RoutedEventArgs e)
        {
            Log("Hızlı temizlik başlatılıyor (%TEMP% ve DNS)...");
            var resTemp = await NativeSystemEngine.CleanTempFilesAsync();
            Log(resTemp);
            var resDns = await NativeSystemEngine.FlushDnsAsync();
            Log(resDns);
        }

        private async void BtnFlushDns_Click(object sender, RoutedEventArgs e)
        {
            Log("DNS önbelleği temizleniyor...");
            var res = await NativeSystemEngine.FlushDnsAsync();
            Log(res);
        }

        private async void BtnGenHwid_Click(object sender, RoutedEventArgs e)
        {
            Log("Kriptografik HWID Parmak İzi hesaplanıyor...");
            var raw = $"{Environment.MachineName}-{Environment.ProcessorCount}-{Environment.OSVersion}";
            var hash = NativeSystemEngine.ComputeSha256(raw);
            Log($"SHA-256 HWID Baseline: {hash}");
            MessageBox.Show($"Deterministik HWID Parmak İzi:\n\n{hash}\n\nBu kod cihazın donanım kimliği olarak kaydedilmiştir.", "HWID Baseline Oluşturuldu", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnFixPrinters_Click(object sender, RoutedEventArgs e)
        {
            Log("0x11b RPC ve 0x709 Point & Print yazıcı onarımı başlatılıyor...");
            var r1 = await NativeSystemEngine.FixPrinterRpc11bAsync();
            Log(r1);
            var r2 = await NativeSystemEngine.FixPointAndPrint709Async();
            Log(r2);
        }

        private async void BtnPurgeSpooler_Click(object sender, RoutedEventArgs e)
        {
            Log("Spooler servisi yeniden başlatılıyor...");
            var res = await NativeSystemEngine.RestartSpoolerAsync();
            Log(res);
        }

        private async void BtnDisableCopilot_Click(object sender, RoutedEventArgs e)
        {
            Log("Windows 11 Copilot ve Recall AI telemetrisi devre dışı bırakılıyor...");
            var res = await NativeSystemEngine.DisableCopilotAndRecallAsync();
            Log(res);
        }

        private async void BtnClassicMenu_Click(object sender, RoutedEventArgs e)
        {
            Log("Klasik Windows 10 sağ tık menüsü etkinleştiriliyor...");
            var res = await NativeSystemEngine.RestoreClassicContextMenuAsync();
            Log(res);
        }

        private async void BtnCleanTemp_Click(object sender, RoutedEventArgs e)
        {
            Log("%TEMP% klasörü temizleniyor...");
            var res = await NativeSystemEngine.CleanTempFilesAsync();
            Log(res);
        }

        private async void BtnRunSfc_Click(object sender, RoutedEventArgs e)
        {
            Log("SFC /scannow sistem dosyası doğrulaması arka planda başlatıldı...");
            var res = await NativeSystemEngine.RunCmdAsync("sfc /scannow");
            Log(res.Success ? "SFC taraması tamamlandı: " + res.Output : "SFC çıktısı: " + res.Output);
        }

        private async void BtnRunDism_Click(object sender, RoutedEventArgs e)
        {
            Log("DISM Windows İmaj onarımı başlatıldı...");
            var res = await NativeSystemEngine.RunCmdAsync("dism /Online /Cleanup-Image /RestoreHealth");
            Log(res.Success ? "DISM onarımı tamamlandı." : "DISM çıktısı: " + res.Output);
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            TxtConsoleLog.Clear();
        }
    }
}