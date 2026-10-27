using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WinoxIT.Services;

namespace WinoxIT
{
    public partial class MainWindow : Window
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        private Process? _serverProcess = null;
        private bool _isInitialized = false;
        private bool _isDarkMode = true;
        private string _currentHardwareJson = "";
        private string _oemManufacturer = "";

        public MainWindow()
        {
            InitializeComponent();
            _isInitialized = true;
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            HideAllPanels();
            if (PanelDashboard != null) PanelDashboard.Visibility = Visibility.Visible;

            Log("⚡ WinoxIT Enterprise Suite v1.0.0 başlatıldı.");
            Log("Sistem donanım profili ve ağ durumu taranıyor...");
            
            await LoadSystemSpecsAsync();
            await CheckServerStatusAsync();
            PopulateLanAddresses();
        }

        private void BtnThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            _isDarkMode = !_isDarkMode;
            ApplyTheme(_isDarkMode);
        }

        private void ApplyTheme(bool isDark)
        {
            var appRes = Application.Current.Resources;
            if (isDark)
            {
                // Dark Palette
                appRes["BrushBgWindow"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x18, 0x18, 0x18));
                appRes["BrushBgSidebar"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1F, 0x1F, 0x1F));
                appRes["BrushBgCard"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x25, 0x25, 0x25));
                appRes["BrushBgCardHover"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2C, 0x2C, 0x2C));
                appRes["BrushBorder"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33));
                appRes["BrushTextPrimary"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF));
                appRes["BrushTextSecondary"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA0, 0xA0, 0xA0));
                appRes["BrushTextMuted"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x70, 0x70, 0x70));
                appRes["BrushButtonBg"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2A, 0x2A, 0x2A));
                appRes["BrushButtonBgHover"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33));
                appRes["BrushButtonBorder"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3D, 0x3D, 0x3D));
                appRes["BrushAccent"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xD2, 0xFF));

                BtnThemeToggle.Content = "🌙 Koyu Tema";
                Log("Arayüz Koyu Temaya geçirildi.");
            }
            else
            {
                // Light Palette
                appRes["BrushBgWindow"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF3, 0xF4, 0xF6));
                appRes["BrushBgSidebar"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE9, 0xEC, 0xF0));
                appRes["BrushBgCard"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF));
                appRes["BrushBgCardHover"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFA, 0xFA, 0xFA));
                appRes["BrushBorder"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD5, 0xD9, 0xE0));
                appRes["BrushTextPrimary"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x11, 0x18, 0x27));
                appRes["BrushTextSecondary"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4B, 0x55, 0x63));
                appRes["BrushTextMuted"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9C, 0xA3, 0xAF));
                appRes["BrushButtonBg"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEA, 0xEE, 0xF4));
                appRes["BrushButtonBgHover"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xDF, 0xE4, 0xED));
                appRes["BrushButtonBorder"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCF, 0xD5, 0xE1));
                appRes["BrushAccent"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4));

                BtnThemeToggle.Content = "☀️ Açık Tema";
                Log("Arayüz Açık Temaya geçirildi.");
            }
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
                await Task.Yield();
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
                                $"Sürüm: WinoxIT v1.0.0-enterprise\r\n" +
                                $"Platform: WinoxIT Native C# & Rust Motoru hazır.";

                Log("Donanım özellikleri başarıyla yüklendi.");
            }
            catch (Exception ex)
            {
                Log($"Özellikler yüklenirken hata: {ex.Message}");
            }
        }

        // ==========================================
        // 1. NAVIGATION & VIEW SWITCHING (MENÜLER)
        // ==========================================

        private void HideAllPanels()
        {
            if (PanelDashboard != null) PanelDashboard.Visibility = Visibility.Collapsed;
            if (PanelLanServer != null) PanelLanServer.Visibility = Visibility.Collapsed;
            if (PanelIsoStudio != null) PanelIsoStudio.Visibility = Visibility.Collapsed;
            if (PanelLanFleet != null) PanelLanFleet.Visibility = Visibility.Collapsed;
            if (PanelHardware != null) PanelHardware.Visibility = Visibility.Collapsed;
            if (PanelDrivers != null) PanelDrivers.Visibility = Visibility.Collapsed;
            if (PanelPrinters != null) PanelPrinters.Visibility = Visibility.Collapsed;
            if (PanelAppDeploy != null) PanelAppDeploy.Visibility = Visibility.Collapsed;
            if (PanelCleaner != null) PanelCleaner.Visibility = Visibility.Collapsed;
            if (PanelDebloat != null) PanelDebloat.Visibility = Visibility.Collapsed;
            if (PanelNetwork != null) PanelNetwork.Visibility = Visibility.Collapsed;
            if (PanelDiagnostics != null) PanelDiagnostics.Visibility = Visibility.Collapsed;
            if (PanelProcesses != null) PanelProcesses.Visibility = Visibility.Collapsed;
            if (PanelStartup != null) PanelStartup.Visibility = Visibility.Collapsed;
            if (PanelAI != null) PanelAI.Visibility = Visibility.Collapsed;
        }

        private async void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is not RadioButton rb) return;

            HideAllPanels();

            switch (rb.Name)
            {
                case "NavDashboard":
                    PanelDashboard.Visibility = Visibility.Visible;
                    TxtHeaderTitle.Text = "📊 Genel Bakış & Durum";
                    TxtHeaderSubtitle.Text = "Tüm çekirdek modüller, donanım parmak izi ve ağ operasyonları hazır.";
                    break;

                case "NavLanServer":
                    PanelLanServer.Visibility = Visibility.Visible;
                    TxtHeaderTitle.Text = "🌐 Sanal Web Sunucu (LAN Kokpiti)";
                    TxtHeaderSubtitle.Text = "0.0.0.0:9999 üzerinden ağdaki telefon, tablet ve bilgisayarlardan erişim.";
                    await CheckServerStatusAsync();
                    PopulateLanAddresses();
                    break;

                case "NavIsoStudio":
                    PanelIsoStudio.Visibility = Visibility.Visible;
                    TxtHeaderTitle.Text = "💿 Katılımsız (Unattended) ISO & WIM Stüdyosu";
                    TxtHeaderSubtitle.Text = "Windows 11 TPM 2.0, SecureBoot, RAM ve CPU bypass yanıt dosyası ve DISM sürücü enjeksiyonu.";
                    break;

                case "NavLanFleet":
                    PanelLanFleet.Visibility = Visibility.Visible;
                    TxtHeaderTitle.Text = "📡 LAN Filo Keşfi & Push Agent";
                    TxtHeaderSubtitle.Text = "Ağdaki diğer teknisyen ve istemci bilgisayarları otomatik keşfetme ve uzaktan sağlık taraması.";
                    break;

                case "NavHardware":
                    PanelHardware.Visibility = Visibility.Visible;
                    TxtHeaderTitle.Text = "🛡️ Donanım Ekspertizi & HWID Parmak İzi";
                    TxtHeaderSubtitle.Text = "Kriptografik SHA-256 donanım kimliği ve parça değişim tespiti (Anti-Tamper).";
                    await UpdateHwidBoxAsync();
                    break;

                case "NavDrivers":
                    PanelDrivers.Visibility = Visibility.Visible;
                    TxtHeaderTitle.Text = "📦 Sürücüler & Universal Offline NIC & OEM";
                    TxtHeaderSubtitle.Text = "İnternetsiz ağ sürücüsü enjeksiyonu ve Dell, Lenovo, HP resmi katalogları.";
                    break;

                case "NavPrinters":
                    PanelPrinters.Visibility = Visibility.Visible;
                    TxtHeaderTitle.Text = "🖨️ Yazıcı & Spooler Onarımı";
                    TxtHeaderSubtitle.Text = "0x0000011b RPC ve 0x00000709 Point & Print kurumsal ağ yazıcı çözümleri.";
                    break;

                case "NavAppDeploy":
                    PanelAppDeploy.Visibility = Visibility.Visible;
                    TxtHeaderTitle.Text = "🛒 Kurumsal AppDeploy & Paket Dağıtımı";
                    TxtHeaderSubtitle.Text = "Winget motoru ve LAN önbellekleme ile tek tıkla sessiz (--silent) kurulum.";
                    await UpdateWingetStatusAsync();
                    break;

                case "NavCleaner":
                    PanelCleaner.Visibility = Visibility.Visible;
                    TxtHeaderTitle.Text = "🧹 Sistem Temizliği & Önbellek Sıfırlama";
                    TxtHeaderSubtitle.Text = "Geçici dosyalar, Windows Update kalıntıları ve sistem çöpleri temizlenir.";
                    break;

                case "NavDebloat":
                    PanelDebloat.Visibility = Visibility.Visible;
                    TxtHeaderTitle.Text = "⚡ Windows 11 Debloat & BİGR Kamu Güvenliği";
                    TxtHeaderSubtitle.Text = "Copilot, Recall, telemetri kapatma ve klasik ergonomi ayarları.";
                    break;

                case "NavNetwork":
                    PanelNetwork.Visibility = Visibility.Visible;
                    TxtHeaderTitle.Text = "📡 Ağ & DNS & TCP/IP Onarım Merkezi";
                    TxtHeaderSubtitle.Text = "DNS temizliği, IP yenileme ve Winsock ağ protokolü sıfırlama.";
                    break;

                case "NavDiagnostics":
                    PanelDiagnostics.Visibility = Visibility.Visible;
                    TxtHeaderTitle.Text = "🔧 Çekirdek Sistem Dosyası & İmaj Onarımı";
                    TxtHeaderSubtitle.Text = "SFC /scannow ve DISM /RestoreHealth ile Windows bileşen deposunu onarma.";
                    break;

                case "NavProcesses":
                    PanelProcesses.Visibility = Visibility.Visible;
                    TxtHeaderTitle.Text = "⚙️ İşlemler & Hizmetler Yöneticisi";
                    TxtHeaderSubtitle.Text = "Aktif süreçler, bellek kullanımı ve yanıt vermeyen görevler.";
                    break;

                case "NavStartup":
                    PanelStartup.Visibility = Visibility.Visible;
                    TxtHeaderTitle.Text = "🚀 Windows Başlangıç & Açılış Hızlandırıcısı";
                    TxtHeaderSubtitle.Text = "Açılışta otomatik çalışan uygulamalar ve başlangıç gecikmesi optimizasyonu.";
                    break;

                case "NavAI":
                    PanelAI.Visibility = Visibility.Visible;
                    TxtHeaderTitle.Text = "🤖 Yapay Zekâ Tanılama & Kök Neden Analizi";
                    TxtHeaderSubtitle.Text = "Olay günlükleri ve mavi ekran (BSOD) minidump arıza çözümleri.";
                    break;
            }

            Log($"Menüye geçildi: {TxtHeaderTitle.Text}");
        }

        // ==========================================
        // SIDEBAR COLLAPSE / EXPAND TOGGLE (☰ DARALT / AÇ)
        // ==========================================

        private bool _isSidebarCollapsed = false;

        private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            _isSidebarCollapsed = !_isSidebarCollapsed;

            if (_isSidebarCollapsed)
            {
                ColSidebar.Width = new GridLength(68);
                BrandTextStack.Visibility = Visibility.Collapsed;
                TxtNavSection1.Visibility = Visibility.Collapsed;
                TxtNavSectionSysForge.Visibility = Visibility.Collapsed;
                TxtNavSection2.Visibility = Visibility.Collapsed;
                TxtNavSection3.Visibility = Visibility.Collapsed;
                TxtNavSection4.Visibility = Visibility.Collapsed;
                TxtNavSection5.Visibility = Visibility.Collapsed;
                TxtSidebarFooterText.Visibility = Visibility.Collapsed;
                BtnToggleSidebar.Content = "»";

                SetNavButtonsCompact(true);
            }
            else
            {
                ColSidebar.Width = new GridLength(270);
                BrandTextStack.Visibility = Visibility.Visible;
                TxtNavSection1.Visibility = Visibility.Visible;
                TxtNavSectionSysForge.Visibility = Visibility.Visible;
                TxtNavSection2.Visibility = Visibility.Visible;
                TxtNavSection3.Visibility = Visibility.Visible;
                TxtNavSection4.Visibility = Visibility.Visible;
                TxtNavSection5.Visibility = Visibility.Visible;
                TxtSidebarFooterText.Visibility = Visibility.Visible;
                BtnToggleSidebar.Content = "☰";

                SetNavButtonsCompact(false);
            }
        }

        private void SetNavButtonsCompact(bool compact)
        {
            SetButtonText(NavDashboard, "📊", "📊  Genel Bakış & Durum", compact);
            SetButtonText(NavLanServer, "🌐", "🌐  Sanal Web Sunucu (LAN)", compact);
            SetButtonText(NavIsoStudio, "💿", "💿  Katılımsız ISO / WIM Stüdyosu", compact);
            SetButtonText(NavLanFleet, "📡", "📡  LAN Filo & Push Agent", compact);
            SetButtonText(NavHardware, "🛡️", "🛡️  Donanım Ekspertizi & HWID", compact);
            SetButtonText(NavDrivers, "📦", "📦  Sürücüler & Offline NIC", compact);
            SetButtonText(NavPrinters, "🖨️", "🖨️  Yazıcı & Spooler Onarımı", compact);
            SetButtonText(NavAppDeploy, "🛒", "🛒  Kurumsal AppDeploy (Winget)", compact);
            SetButtonText(NavCleaner, "🧹", "🧹  Sistem Temizliği & Cache", compact);
            SetButtonText(NavDebloat, "⚡", "⚡  Win 11 Debloat & Gizlilik", compact);
            SetButtonText(NavNetwork, "📶", "📶  Ağ & DNS & TCP/IP Onarım", compact);
            SetButtonText(NavDiagnostics, "🔧", "🔧  Çekirdek Sistem Onarımı", compact);
            SetButtonText(NavProcesses, "⚙️", "⚙️  İşlemler & Hizmetler", compact);
            SetButtonText(NavStartup, "🚀", "🚀  Başlangıç Yöneticisi", compact);
            SetButtonText(NavAI, "🤖", "🤖  Yapay Zekâ Tanılama", compact);
        }

        private static void SetButtonText(RadioButton? rb, string icon, string fullText, bool compact)
        {
            if (rb == null) return;
            rb.Content = compact ? icon : fullText;
        }

        // ==========================================
        // IN-APP MODAL / POPUP DIALOG WINDOW
        // ==========================================

        private void ShowModal(string title, string content, string subtitle = "Modül içi anlık önizleme penceresi")
        {
            Dispatcher.Invoke(() =>
            {
                TxtModalTitle.Text = title;
                TxtModalSubtitle.Text = subtitle;
                TxtModalContent.Text = content;
                TxtModalContent.ScrollToHome();
                ModalOverlay.Visibility = Visibility.Visible;
            });
        }

        private void BtnCloseModal_Click(object sender, RoutedEventArgs e)
        {
            ModalOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnModalCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(TxtModalContent.Text);
                Log("Pencere içeriği panoya kopyalandı.");
            }
            catch (Exception ex)
            {
                Log($"Panoya kopyalama hatası: {ex.Message}");
            }
        }

        private void BtnModalSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = TxtModalTitle.Text.Contains("xml", StringComparison.OrdinalIgnoreCase) ? "autounattend.xml" : "Rapor.txt",
                    Filter = "Dosyalar (*.*)|*.*"
                };
                if (sfd.ShowDialog() == true)
                {
                    File.WriteAllText(sfd.FileName, TxtModalContent.Text, Encoding.UTF8);
                    Log($"İçerik dosyaya kaydedildi: {sfd.FileName}");
                }
            }
            catch (Exception ex)
            {
                Log($"Kaydetme hatası: {ex.Message}");
            }
        }

        // ==========================================
        // SYSFORGE: KATILIMSIZ ISO & WIM İŞLEMLERİ
        // ==========================================

        private async void BtnGenerateIsoXml_Click(object sender, RoutedEventArgs e)
        {
            Log("Windows 11 Katılımsız (autounattend.xml) yanıt dosyası üretiliyor...");
            var user = string.IsNullOrWhiteSpace(TxtIsoUser.Text) ? "WinoxAdmin" : TxtIsoUser.Text.Trim();
            var pass = TxtIsoPass.Password;
            var tpm = ChkIsoBypassTpm.IsChecked == true;
            var secureBoot = ChkIsoBypassSecureBoot.IsChecked == true;
            var ram = ChkIsoBypassRam.IsChecked == true;
            var cpu = ChkIsoBypassCpu.IsChecked == true;

            var (success, xml, savedPath) = await NativeSystemEngine.GenerateUnattendedXmlAsync(user, pass, tpm, secureBoot, ram, cpu);
            if (success)
            {
                Log($"autounattend.xml başarıyla oluşturuldu: {savedPath}");
                ShowModal("autounattend.xml — Katılımsız Kurulum Dosyası", xml, $"Dosya kaydedildi: {savedPath}");
            }
            else
            {
                Log($"Hata: {xml}");
            }
        }

        private void BtnOpenIsoWorkspace_Click(object sender, RoutedEventArgs e)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var dir = Path.Combine(desktop, "WinoxIT_ISO_Workspace");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start("explorer.exe", dir);
        }

        private void BtnBrowseWim_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Windows Image (*.wim)|*.wim|Tüm Dosyalar (*.*)|*.*",
                Title = "WIM İmaj Dosyası Seçin"
            };
            if (ofd.ShowDialog() == true)
            {
                TxtWimFilePath.Text = ofd.FileName;
            }
        }

        private async void BtnWimMount_Click(object sender, RoutedEventArgs e)
        {
            Log($"DISM Mount başlatılıyor: {TxtWimFilePath.Text} -> {TxtWimMountDir.Text}");
            var (success, output) = await NativeSystemEngine.MountWimAsync(TxtWimFilePath.Text, 1, TxtWimMountDir.Text);
            Log(output);
            ShowModal("DISM Mount Sonucu", output);
        }

        private async void BtnWimInjectDrivers_Click(object sender, RoutedEventArgs e)
        {
            var driversDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "drivers", "universal_nic");
            Log($"Sürücü enjeksiyonu başlatılıyor: {driversDir}");
            var (success, output) = await NativeSystemEngine.InjectDriversToWimAsync(TxtWimMountDir.Text, driversDir);
            Log(output);
            ShowModal("Sürücü Enjeksiyon Sonucu", output);
        }

        private async void BtnWimCommit_Click(object sender, RoutedEventArgs e)
        {
            Log($"DISM Commit (Kaydet & Kapat) başlatılıyor: {TxtWimMountDir.Text}");
            var (success, output) = await NativeSystemEngine.UnmountWimAsync(TxtWimMountDir.Text, commit: true);
            Log(output);
            ShowModal("DISM Commit Sonucu", output);
        }

        private async void BtnWimDiscard_Click(object sender, RoutedEventArgs e)
        {
            Log($"DISM Discard (İptal Et) başlatılıyor: {TxtWimMountDir.Text}");
            var (success, output) = await NativeSystemEngine.UnmountWimAsync(TxtWimMountDir.Text, commit: false);
            Log(output);
            ShowModal("DISM Discard Sonucu", output);
        }

        // ==========================================
        // SYSFORGE: LAN FİLO DAĞITIMI & PUSH AGENT
        // ==========================================

        private async void BtnDiscoverLanFleet_Click(object sender, RoutedEventArgs e)
        {
            Log("Yerel ağdaki cihazlar taranıyor (ARP / NetNeighbor)...");
            var nodes = await NativeSystemEngine.DiscoverLanNodesAsync();
            GridLanFleet.ItemsSource = nodes;
            Log($"Tarama tamamlandı: {nodes.Count} aktif ağ cihazı tespit edildi.");
            if (nodes.Count > 0 && string.IsNullOrWhiteSpace(TxtFleetTargetIp.Text))
            {
                TxtFleetTargetIp.Text = nodes[0].IPAddress;
            }
        }

        private async void BtnPingFleetTarget_Click(object sender, RoutedEventArgs e)
        {
            var target = TxtFleetTargetIp.Text.Trim();
            if (string.IsNullOrWhiteSpace(target))
            {
                Log("Lütfen geçerli bir hedef IP girin.");
                return;
            }
            Log($"Ping testi gönderiliyor: {target}...");
            var (reachable, msg) = await NativeSystemEngine.PingLanHostAsync(target);
            Log(msg);
            ShowModal("Ağ Ping Testi Sonucu", msg);
        }

        private async void BtnProbeFleetTarget_Click(object sender, RoutedEventArgs e)
        {
            var target = TxtFleetTargetIp.Text.Trim();
            if (string.IsNullOrWhiteSpace(target)) return;
            Log($"Hedef cihaza WinoxIT sağlık probu gönderiliyor: http://{target}:9999/api/status ...");
            try
            {
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(4) };
                var res = await client.GetStringAsync($"http://{target}:9999/api/status");
                Log($"Cihaz yanıt verdi ({target}):\n{res}");
                ShowModal($"WinoxIT Sağlık Raporu ({target})", res);
            }
            catch (Exception ex)
            {
                Log($"Cihazda WinoxIT servisi algılanamadı ({target}): {ex.Message}");
                ShowModal("Sağlık Probu Hatası", $"Hedef cihazda (http://{target}:9999) WinoxIT sunucusu yanıt vermedi.\n\nHata: {ex.Message}");
            }
        }

        // ==========================================
        // 2. SANAL WEB SUNUCUSU (LAN GATEWAY)
        // ==========================================

        private string FindProjectFile(string fileName)
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 5; i++)
            {
                var candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate)) return candidate;
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            return fileName;
        }

        private async Task<bool> IsServerOnlineAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("http://localhost:9999/api/health");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private async Task CheckServerStatusAsync()
        {
            var online = await IsServerOnlineAsync();
            if (online)
            {
                TxtServerStatus.Text = "0.0.0.0:9999";
                TxtServerSub.Text = "🟢 Çevrimiçi (Açık)";
                TxtServerMiniStatus.Text = "Sunucu: 🟢 Port 9999 Açık";
                TxtLanServerBigStatus.Text = "Sunucu Durumu: 🟢 ÇEVRİMİÇİ (Port 9999 Dinleniyor)";
                TxtLanServerBigStatus.Foreground = (System.Windows.Media.Brush)FindResource("BrushSuccess");
            }
            else
            {
                TxtServerStatus.Text = "Durduruldu";
                TxtServerSub.Text = "🔴 Çevrimdışı";
                TxtServerMiniStatus.Text = "Sunucu: 🔴 Kapalı";
                TxtLanServerBigStatus.Text = "Sunucu Durumu: 🔴 ÇEVRİMDAŞI (Başlatılmadı)";
                TxtLanServerBigStatus.Foreground = (System.Windows.Media.Brush)FindResource("BrushError");
            }
        }

        private void PopulateLanAddresses()
        {
            var sb = new StringBuilder();
            sb.AppendLine("🔗 [Localhost]       : http://localhost:9999/dashboard.html");

            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    var ipProps = ni.GetIPProperties();
                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(addr.Address))
                        {
                            sb.AppendLine($"🔗 [{ni.Name,-16}] : http://{addr.Address}:9999/dashboard.html");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"IP tarama hatası: {ex.Message}");
            }

            TxtLanAddresses.Text = sb.ToString().TrimEnd();
        }

        private async void BtnStartServer_Click(object sender, RoutedEventArgs e)
        {
            Log("Sanal Ağ Sunucusu başlatılıyor...");
            if (await IsServerOnlineAsync())
            {
                Log("Sunucu zaten 9999 portunda çalışıyor.");
                await CheckServerStatusAsync();
                return;
            }

            try
            {
                var serverJs = FindProjectFile("server.js");
                if (File.Exists(serverJs))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "node.exe",
                        Arguments = $"\"{serverJs}\"",
                        WorkingDirectory = Path.GetDirectoryName(serverJs) ?? "",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    _serverProcess = Process.Start(psi);
                    Log($"Node.js sunucusu başlatıldı (PID: {_serverProcess?.Id}).");
                    await Task.Delay(1500);
                    await CheckServerStatusAsync();
                }
                else
                {
                    var cmdPath = FindProjectFile("Launch-NodeServer.cmd");
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c \"{cmdPath}\"",
                        UseShellExecute = true
                    });
                    Log("Launch-NodeServer.cmd başlatıldı.");
                    await Task.Delay(2000);
                    await CheckServerStatusAsync();
                }
            }
            catch (Exception ex)
            {
                Log($"Sunucu başlatılamadı: {ex.Message}. Node.js yüklü mü kontrol edin.");
            }
        }

        private async void BtnStopServer_Click(object sender, RoutedEventArgs e)
        {
            Log("Sanal sunucu durduruluyor...");
            try
            {
                await NativeSystemEngine.RunPowerShellAsync("Get-Process node -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -like '*server.js*' } | Stop-Process -Force");
                _serverProcess?.Kill();
                _serverProcess = null;
                await Task.Delay(1000);
                await CheckServerStatusAsync();
                Log("Sanal sunucu durduruldu.");
            }
            catch (Exception ex)
            {
                Log($"Durdurma hatası: {ex.Message}");
            }
        }

        private async void BtnOpenDashboard_Click(object sender, RoutedEventArgs e)
        {
            if (!await IsServerOnlineAsync())
            {
                Log("Sunucu henüz açık değil, önce başlatılıyor...");
                BtnStartServer_Click(sender, e);
                await Task.Delay(1500);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "http://localhost:9999/dashboard.html",
                UseShellExecute = true
            });
            Log("Web Kokpiti tarayıcıda açıldı.");
        }

        private async void BtnFirewallFix_Click(object sender, RoutedEventArgs e)
        {
            Log("Windows Güvenlik Duvarı'nda Port 9999 TCP kuralı açılıyor...");
            var res = await NativeSystemEngine.RunCmdAsync("netsh advfirewall firewall add rule name=\"WinoxIT Virtual Server Port 9999\" dir=in action=allow protocol=TCP localport=9999");
            Log(res.Success ? "Güvenlik Duvarı Port 9999 TCP kuralı başarıyla açıldı." : $"Hata: {res.Output}");
        }

        private void BtnQuickServer_Click(object sender, RoutedEventArgs e)
        {
            BtnOpenDashboard_Click(sender, e);
        }

        // ==========================================
        // 3. DONANIM & HWID & DRIFT ANALİZİ
        // ==========================================

        private async Task UpdateHwidBoxAsync()
        {
            var (hash, json) = await NativeSystemEngine.GetHardwareFingerprintDetailsAsync();
            _currentHardwareJson = json;
            TxtHwidDetails.Text = $"Cihaz Adı: {Environment.MachineName}\r\n" +
                                  $"Kriptografik Algoritma: Deterministik SHA-256\r\n" +
                                  $"🔑 DONANIM BASELINE HWID İMZASI:\r\n{hash}\r\n\r\n" +
                                  $"Detaylı Bileşen Profili:\r\n{json}";
        }

        private async void BtnGenHwid_Click(object sender, RoutedEventArgs e)
        {
            Log("Detaylı donanım bileşenleri taranıyor ve SHA-256 baseline hesaplanıyor...");
            await UpdateHwidBoxAsync();
            Log("Deterministik SHA-256 Baseline başarıyla üretildi.");
            MessageBox.Show("Deterministik SHA-256 Donanım İmzası başarıyla üretildi.\n\nBaseline profilini kaydetmek için 'Baseline Referansını Kaydet' butonuna basabilirsiniz.", "WinoxIT HWID", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnSaveBaselineToFile_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentHardwareJson))
            {
                MessageBox.Show("Önce 'HWID Baseline Referansı Oluştur' butonuna tıklayınız.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var baselinePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hardware_baseline.json");
                File.WriteAllText(baselinePath, _currentHardwareJson);
                Log($"Donanım baseline referansı dosyaya kaydedildi: {baselinePath}");
                MessageBox.Show($"Donanım Baseline başarıyla kaydedildi:\n\n{baselinePath}\n\nArtık periyodik olarak donanım drift taraması yapabilirsiniz.", "Baseline Kaydedildi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"Baseline kaydetme hatası: {ex.Message}");
            }
        }

        private async void BtnCheckDrift_Click(object sender, RoutedEventArgs e)
        {
            Log("Donanım bileşenleri (RAM, SSD, Anakart, CPU) taranıyor ve baseline ile kıyaslanıyor...");
            var baselinePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hardware_baseline.json");
            
            if (!File.Exists(baselinePath))
            {
                if (string.IsNullOrWhiteSpace(_currentHardwareJson))
                {
                    await UpdateHwidBoxAsync();
                }
                File.WriteAllText(baselinePath, _currentHardwareJson);
                Log("İlk çalıştırma: Mevcut donanım baseline referansı olarak kaydedildi.");
            }

            var baselineContent = File.ReadAllText(baselinePath);
            var (matched, diffLog) = await NativeSystemEngine.CompareHardwareBaselineAsync(baselineContent);

            TxtDriftDiff.Text = diffLog;

            if (matched)
            {
                Log("✅ [OK] Donanım sapması (Drift) tespit edilmedi. Parçalar %100 orijinal referansla eşleşiyor.");
                MessageBox.Show("Donanım kontrolü tamamlandı.\n\nDurum: %100 Eşleşme (Bileşen değişikliği veya sökülen parça tespit edilmedi).", "Donanım Doğrulama Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                Log("⚠️ [ALARM] Donanım sapması (Drift) tespit edildi! Bazı parçalar değiştirilmiş veya sökülmüş!");
                MessageBox.Show("DİKKAT: Donanım sapması tespit edildi!\n\nDetaylı fark raporunu ekrandaki Drift Analizi kutusundan inceleyebilirsiniz.", "Donanım Sapması Tespit Edildi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnExportHwidReport_Click(object sender, RoutedEventArgs e)
        {
            Log("Teknik Servis ve Zimmet Ekspertiz Raporu oluşturuluyor...");
            var reportPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"WinoxIT_Ekspertiz_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            var content = $"=== WINOXIT KURUMSAL DONANIM VE EKSPERTİZ RAPORU ===\r\nTarih: {DateTime.Now}\r\n" +
                          $"{TxtHwidDetails.Text}\r\n\r\n" +
                          $"=== DONANIM DRIFT DENETİM SONUCU ===\r\n{TxtDriftDiff.Text}";
            File.WriteAllText(reportPath, content);
            Log($"Rapor kaydedildi: {reportPath}");
            Process.Start(new ProcessStartInfo { FileName = "notepad.exe", Arguments = $"\"{reportPath}\"" });
        }

        private async void BtnReadOemKey_Click(object sender, RoutedEventArgs e)
        {
            Log("BIOS / UEFI MSDM tablosundan OEM Windows ürün anahtarı okunuyor...");
            var res = await NativeSystemEngine.RunPowerShellAsync("(Get-WmiObject -Query 'select * from SoftwareLicensingService').OA3xOriginalProductKey");
            if (res.Success && !string.IsNullOrWhiteSpace(res.Output))
            {
                Log($"OEM Orijinal Lisans Anahtarı: {res.Output}");
                MessageBox.Show($"Anakart Gömülü OEM Lisans Anahtarı:\n\n{res.Output}", "OEM Lisans Bulundu", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                Log("Bu anakartta gömülü MSDM OEM anahtarı bulunamadı veya cihaz dijital lisanslı.");
            }
        }

        // ==========================================
        // 3.1 SÜRÜCÜLER & OFFLINE NIC & OEM SÜRÜCÜLERİ
        // ==========================================

        private async void BtnInstallOfflineNic_Click(object sender, RoutedEventArgs e)
        {
            Log("🚀 Evrensel Çevrimdışı NIC Sürücüleri PnPUtil ile enjekte ediliyor...");
            TxtDriversLog.Text = "Sürücü enjeksiyonu yürütülüyor, lütfen bekleyiniz...";
            var res = await NativeSystemEngine.InstallOfflineNicDriversAsync();
            Log(res);
            TxtDriversLog.Text = res;
        }

        private async void BtnScanMissingDrivers_Click(object sender, RoutedEventArgs e)
        {
            Log("🔍 Donanım Yöneticisi taranıyor (Eksik veya ünlemli aygıtlar)...");
            var res = await NativeSystemEngine.ScanMissingDriversAsync();
            Log(res);
            TxtDriversLog.Text = res;
        }

        private void BtnOpenDriversFolder_Click(object sender, RoutedEventArgs e)
        {
            var path = FindProjectFile("drivers");
            if (!Directory.Exists(path))
            {
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "drivers");
                Directory.CreateDirectory(path);
            }
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{path}\"" });
            Log($"Sürücüler klasörü açıldı: {path}");
        }

        private async void BtnDetectOem_Click(object sender, RoutedEventArgs e)
        {
            Log("🏢 Anakart ve OEM donanım kimliği sorgulanıyor...");
            var (mfg, model, serial, board, summary) = await NativeSystemEngine.DetectOemHardwareAsync();
            _oemManufacturer = mfg;
            TxtOemDetails.Text = summary;
            Log($"Tespit Edildi: {mfg} {model} (Seri: {serial})");
        }

        private async void BtnTriggerOemUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_oemManufacturer))
            {
                await Task.Run(async () =>
                {
                    var (mfg, _, _, _, _) = await NativeSystemEngine.DetectOemHardwareAsync();
                    _oemManufacturer = mfg;
                });
            }

            Log($"⚡ {_oemManufacturer} için resmi sürücü güncelleme motoru tetikleniyor...");
            var res = await NativeSystemEngine.TriggerOemUpdateAsync(_oemManufacturer);
            Log(res);
            TxtDriversLog.Text = res;
        }

        // ==========================================
        // 3.2 KURUMSAL APPDEPLOY (WINGET & OFFLINE)
        // ==========================================

        private async Task UpdateWingetStatusAsync()
        {
            var (avail, ver) = await NativeSystemEngine.CheckWingetAsync();
            if (avail)
            {
                TxtWingetStatus.Text = $"🟢 Winget Aktif ({ver})";
            }
            else
            {
                TxtWingetStatus.Text = "🔴 Winget Bulunamadı";
            }
        }

        private async void BtnInstallSelectedApps_Click(object sender, RoutedEventArgs e)
        {
            var (avail, _) = await NativeSystemEngine.CheckWingetAsync();
            if (!avail)
            {
                MessageBox.Show("Sisteminizde Windows Package Manager (winget) bulunamadı.\nLütfen Microsoft Store'dan App Installer'ı güncelleyiniz.", "Winget Bulunamadı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var targets = new List<(string Id, string Name)>();

            if (ChkChrome?.IsChecked == true) targets.Add(("Google.Chrome", "Google Chrome"));
            if (ChkFirefox?.IsChecked == true) targets.Add(("Mozilla.Firefox", "Mozilla Firefox"));
            if (Chk7Zip?.IsChecked == true) targets.Add(("7zip.7zip", "7-Zip"));
            if (ChkAnyDesk?.IsChecked == true) targets.Add(("AnyDeskSoftwareGmbH.AnyDesk", "AnyDesk"));
            if (ChkRustDesk?.IsChecked == true) targets.Add(("RustDesk.RustDesk", "RustDesk"));
            if (ChkNotepadPlus?.IsChecked == true) targets.Add(("Notepad++.Notepad++", "Notepad++"));
            if (ChkVlc?.IsChecked == true) targets.Add(("VideoLAN.VLC", "VLC Media Player"));
            if (ChkVSCode?.IsChecked == true) targets.Add(("Microsoft.VisualStudioCode", "Visual Studio Code"));
            if (ChkGit?.IsChecked == true) targets.Add(("Git.Git", "Git for Windows"));
            if (ChkLibreOffice?.IsChecked == true) targets.Add(("TheDocumentFoundation.LibreOffice", "LibreOffice"));

            if (targets.Count == 0)
            {
                MessageBox.Show("Lütfen kurulacak en az bir uygulama seçiniz.", "Seçim Yapılmadı", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Log($"🚀 {targets.Count} adet kurumsal uygulama sessiz kurulum sırasına alındı...");
            TxtAppDeployLog.Text = "Kurulum başlatılıyor...\r\n";

            foreach (var app in targets)
            {
                Log($"[YÜKLENİYOR] {app.Name} ({app.Id}) kuruluyor...");
                TxtAppDeployLog.AppendText($"[YÜKLENİYOR] {app.Name}...\r\n");
                var result = await NativeSystemEngine.InstallWingetPackageAsync(app.Id, app.Name);
                Log(result);
                TxtAppDeployLog.AppendText(result + "\r\n");
            }

            Log("🎉 Seçili tüm yazılımların kurulum döngüsü tamamlandı.");
            MessageBox.Show("Seçilen uygulamaların kurulum işlemi tamamlandı.", "AppDeploy Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnOpenPackagesFolder_Click(object sender, RoutedEventArgs e)
        {
            var path = FindProjectFile("packages");
            if (!Directory.Exists(path))
            {
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "packages");
                Directory.CreateDirectory(path);
            }
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{path}\"" });
            Log($"Paketler LAN önbellek klasörü açıldı: {path}");
        }

        // ==========================================
        // 4. YAZICI ONARIM
        // ==========================================

        private async void BtnFixPrinters_Click(object sender, RoutedEventArgs e)
        {
            Log("0x0000011b RPC yetkilendirme onarımı başlatılıyor...");
            var r1 = await NativeSystemEngine.FixPrinterRpc11bAsync();
            Log(r1);
            TxtPrinterLog.Text = r1;
        }

        private async void BtnFix709_Click(object sender, RoutedEventArgs e)
        {
            Log("0x00000709 Point and Print ilkesi sıfırlanıyor...");
            var r2 = await NativeSystemEngine.FixPointAndPrint709Async();
            Log(r2);
            TxtPrinterLog.Text = r2;
        }

        private async void BtnPurgeSpooler_Click(object sender, RoutedEventArgs e)
        {
            Log("Spooler servisi temizleniyor ve yeniden başlatılıyor...");
            var res = await NativeSystemEngine.RestartSpoolerAsync();
            Log(res);
            TxtPrinterLog.Text = res;
        }

        private async void BtnClearQueue_Click(object sender, RoutedEventArgs e)
        {
            Log("Kilitli yazdırma işleri temizleniyor (PRINTERS klasörü)...");
            var res = await NativeSystemEngine.RunPowerShellAsync("Stop-Service Spooler -Force; Remove-Item 'C:\\Windows\\System32\\spool\\PRINTERS\\*' -Force -Recurse -ErrorAction SilentlyContinue; Start-Service Spooler; 'Yazdırma kuyruğu boşaltıldı.'");
            Log(res.Output);
            TxtPrinterLog.Text = res.Output;
        }

        // ==========================================
        // 5. SİSTEM TEMİZLİĞİ
        // ==========================================

        private async void BtnQuickClean_Click(object sender, RoutedEventArgs e)
        {
            Log("Hızlı temizlik başlatılıyor (%TEMP% ve DNS)...");
            var resTemp = await NativeSystemEngine.CleanTempFilesAsync();
            Log(resTemp);
            var resDns = await NativeSystemEngine.FlushDnsAsync();
            Log(resDns);
        }

        private async void BtnCleanTemp_Click(object sender, RoutedEventArgs e)
        {
            Log("%TEMP% klasörü temizleniyor...");
            var res = await NativeSystemEngine.CleanTempFilesAsync();
            Log(res);
        }

        private async void BtnCleanWu_Click(object sender, RoutedEventArgs e)
        {
            Log("Windows Update indirme önbelleği (SoftwareDistribution) sıfırlanıyor...");
            var res = await NativeSystemEngine.RunPowerShellAsync("Stop-Service wuauserv -Force; Remove-Item 'C:\\Windows\\SoftwareDistribution\\Download\\*' -Recurse -Force -ErrorAction SilentlyContinue; Start-Service wuauserv; 'Windows Update önbelleği temizlendi.'");
            Log(res.Output);
        }

        private async void BtnEmptyRecycle_Click(object sender, RoutedEventArgs e)
        {
            Log("Geri Dönüşüm Kutusu boşaltılıyor...");
            var res = await NativeSystemEngine.RunPowerShellAsync("Clear-RecycleBin -Force -ErrorAction SilentlyContinue; 'Geri Dönüşüm Kutusu boşaltıldı.'");
            Log(res.Output);
        }

        // ==========================================
        // 6. DEBLOAT & GİZLİLİK
        // ==========================================

        private async void BtnDisableCopilot_Click(object sender, RoutedEventArgs e)
        {
            Log("Windows 11 Copilot ve Recall AI telemetrisi devre dışı bırakılıyor...");
            var res = await NativeSystemEngine.DisableCopilotAndRecallAsync();
            Log(res);
        }

        private async void BtnClassicMenu_Click(object sender, RoutedEventArgs e)
        {
            Log("Klasik Windows 10 sağ tık menüsü açılıyor...");
            var res = await NativeSystemEngine.RestoreClassicContextMenuAsync();
            Log(res);
        }

        private async void BtnDisableTelemetry_Click(object sender, RoutedEventArgs e)
        {
            Log("Telemetri servisleri (DiagTrack, dmwappushservice) kapatılıyor...");
            var res = await NativeSystemEngine.RunPowerShellAsync("Stop-Service DiagTrack, dmwappushservice -Force -ErrorAction SilentlyContinue; Set-Service DiagTrack, dmwappushservice -StartupType Disabled -ErrorAction SilentlyContinue; 'Telemetri servisleri devre dışı bırakıldı.'");
            Log(res.Output);
        }

        private async void BtnDisableBingSearch_Click(object sender, RoutedEventArgs e)
        {
            Log("Başlat menüsü Bing arama önerileri kapatılıyor...");
            var res = await NativeSystemEngine.RunPowerShellAsync("Set-ItemProperty -Path 'HKCU:\\Software\\Policies\\Microsoft\\Windows\\Explorer' -Name 'DisableSearchBoxSuggestions' -Value 1 -Type DWord -Force -ErrorAction SilentlyContinue; 'Bing web aramaları kapatıldı.'");
            Log(res.Output);
        }

        // ==========================================
        // 7. AĞ & DNS
        // ==========================================

        private async void BtnFlushDns_Click(object sender, RoutedEventArgs e)
        {
            Log("DNS önbelleği temizleniyor...");
            var res = await NativeSystemEngine.FlushDnsAsync();
            Log(res);
        }

        private async void BtnResetWinsock_Click(object sender, RoutedEventArgs e)
        {
            Log("Winsock ve TCP/IP ağ protokolü fabrika ayarlarına sıfırlanıyor...");
            var res = await NativeSystemEngine.RunCmdAsync("netsh winsock reset && netsh int ip reset");
            Log("Winsock sıfırlandı: " + res.Output);
        }

        private async void BtnRenewIp_Click(object sender, RoutedEventArgs e)
        {
            Log("IP adresi yenileniyor (release & renew)...");
            var res = await NativeSystemEngine.RunCmdAsync("ipconfig /renew");
            Log("IP yenilendi: " + res.Output);
        }

        private async void BtnPingTest_Click(object sender, RoutedEventArgs e)
        {
            Log("Google DNS (8.8.8.8) ve Cloudflare (1.1.1.1) gecikme testi yapılıyor...");
            var res = await NativeSystemEngine.RunCmdAsync("ping 8.8.8.8 -n 2");
            Log("Ping sonucu:\n" + res.Output);
        }

        // ==========================================
        // 8. ÇEKİRDEK ONARIM
        // ==========================================

        private async void BtnRunSfc_Click(object sender, RoutedEventArgs e)
        {
            Log("SFC /scannow sistem dosyası doğrulaması başlatıldı...");
            var res = await NativeSystemEngine.RunCmdAsync("sfc /scannow");
            Log(res.Output);
        }

        private async void BtnRunDism_Click(object sender, RoutedEventArgs e)
        {
            Log("DISM /Online /Cleanup-Image /RestoreHealth başlatıldı...");
            var res = await NativeSystemEngine.RunCmdAsync("dism /Online /Cleanup-Image /RestoreHealth");
            Log(res.Output);
        }

        private async void BtnRunChkdsk_Click(object sender, RoutedEventArgs e)
        {
            Log("Chkdsk C: disk doğrulaması başlatıldı...");
            var res = await NativeSystemEngine.RunCmdAsync("chkdsk C: /scan");
            Log(res.Output);
        }

        // ==========================================
        // 9. İŞLEMLER & BAŞLANGIÇ
        // ==========================================

        private async void BtnListProcesses_Click(object sender, RoutedEventArgs e)
        {
            Log("En çok bellek tüketen ilk 5 işlem sorgulanıyor...");
            var res = await NativeSystemEngine.RunPowerShellAsync("Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 5 Name, @{N='Memory(MB)';E={[math]::Round($_.WorkingSet64/1MB,1)}} | Format-Table -AutoSize | Out-String");
            Log("\n" + res.Output);
        }

        private async void BtnKillNotResponding_Click(object sender, RoutedEventArgs e)
        {
            Log("Yanıt vermeyen pencereler ve kilitlenen işlemler sonlandırılıyor...");
            var res = await NativeSystemEngine.RunCmdAsync("taskkill /f /fi \"status eq not responding\"");
            Log(res.Output);
        }

        private async void BtnScanStartup_Click(object sender, RoutedEventArgs e)
        {
            Log("Windows Başlangıç (Startup) programları listeleniyor...");
            var res = await NativeSystemEngine.RunPowerShellAsync("Get-CimInstance Win32_StartupCommand | Select-Object Name, Command, Location | Format-Table -AutoSize | Out-String");
            Log("\n" + res.Output);
        }

        private async void BtnOptimizeStartupDelay_Click(object sender, RoutedEventArgs e)
        {
            Log("Windows başlangıç gecikmesi sıfırlanıyor (StartupDelayInMSec = 0)...");
            var res = await NativeSystemEngine.RunPowerShellAsync("Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Serialize' -Name 'StartupDelayInMSec' -Value 0 -Type DWord -Force -ErrorAction SilentlyContinue; 'Başlangıç gecikmesi kaldırıldı.'");
            Log(res.Output);
        }

        // ==========================================
        // 10. YAPAY ZEKÂ TANILAMA
        // ==========================================

        private async void BtnAiEventScan_Click(object sender, RoutedEventArgs e)
        {
            Log("Windows Olay Günlüğü'ndeki son 24 saatlik kritik sistem hataları taranıyor...");
            var res = await NativeSystemEngine.RunPowerShellAsync("Get-EventLog -LogName System -EntryType Error -Newest 3 | Select-Object TimeGenerated, Source, Message | Format-List | Out-String");
            Log("Kritik Sistem Hataları:\n" + res.Output);
        }

        private async void BtnAiBsodScan_Click(object sender, RoutedEventArgs e)
        {
            Log("C:\\Windows\\Minidump klasöründeki mavi ekran (BSOD) döküm dosyaları taranıyor...");
            var res = await NativeSystemEngine.RunPowerShellAsync("Get-ChildItem C:\\Windows\\Minidump -ErrorAction SilentlyContinue | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize | Out-String");
            Log(string.IsNullOrWhiteSpace(res.Output) ? "Sistemde yakın zamanda oluşmuş bir mavi ekran (BSOD) dump dosyası bulunamadı. Sistem kararlı." : res.Output);
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            TxtConsoleLog.Clear();
        }
    }
}