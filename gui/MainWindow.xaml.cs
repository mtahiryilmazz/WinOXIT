using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinoxIT.Services;

namespace WinoxIT
{
    public partial class MainWindow : Window
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        private Process? _serverProcess = null;
        private bool _isInitialized = false;
        private bool _isDarkMode = true;
        private bool _isSidebarCollapsed = false;
        private string _currentHardwareJson = "";

        // LAN Network Discovery & Subnet Scanner State
        private List<LocalNetworkAdapterItem> _cachedAdapters = new();
        private List<LanHostNode> _currentLanHosts = new();
        private CancellationTokenSource? _subnetScanCts = null;
        private string _currentLanFilter = "All";

        private static void LogDiagnostic(string msg)
        {
            try
            {
                var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup.log");
                System.IO.File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\r\n");
            }
            catch { }
        }

        public MainWindow()
        {
            LogDiagnostic("MainWindow constructor start");
            try
            {
                InitializeComponent();
                LogDiagnostic("MainWindow InitializeComponent complete");
            }
            catch (Exception ex)
            {
                LogDiagnostic($"FATAL MainWindow InitializeComponent: {ex}");
                throw;
            }
            _isInitialized = true;
            Loaded += MainWindow_Loaded;
            LogDiagnostic("MainWindow constructor complete");
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LogDiagnostic("MainWindow_Loaded start");
            HideAllHubs();
            if (PanelHubDashboard != null) PanelHubDashboard.Visibility = Visibility.Visible;
            if (PanelDashboard != null) PanelDashboard.Visibility = Visibility.Visible;

            Log("⚡ WinoxIT Enterprise Suite v1.0.0 başlatıldı.");
            Log("Sistem donanım profili, ağ durumu ve yapay zekâ motoru hazır.");

            try
            {
                await LoadSystemSpecsAsync();
                await CheckServerStatusAsync();
                await UpdateWingetStatusAsync();
                await UpdateHwidBoxAsync();
            }
            catch (Exception ex)
            {
                LogDiagnostic($"ERROR MainWindow_Loaded async tasks: {ex}");
            }
        }

        // ==========================================
        // THEME MANAGEMENT (AÇIK / KOYU TEMA)
        // ==========================================

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
                appRes["BrushBgWindow"] = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18));
                appRes["BrushBgSidebar"] = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
                appRes["BrushBgCard"] = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25));
                appRes["BrushBgCardHover"] = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2C));
                appRes["BrushBorder"] = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
                appRes["BrushTextPrimary"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                appRes["BrushTextSecondary"] = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));
                appRes["BrushTextMuted"] = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70));
                appRes["BrushButtonBg"] = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                appRes["BrushButtonBgHover"] = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
                appRes["BrushButtonBorder"] = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D));
                appRes["BrushAccent"] = new SolidColorBrush(Color.FromRgb(0x00, 0x98, 0xFF));

                BtnThemeToggle.Content = "🌙 Koyu Tema";
                Log("Arayüz Koyu Temaya (Fluent Dark) geçirildi.");
            }
            else
            {
                appRes["BrushBgWindow"] = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
                appRes["BrushBgSidebar"] = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA));
                appRes["BrushBgCard"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                appRes["BrushBgCardHover"] = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));
                appRes["BrushBorder"] = new SolidColorBrush(Color.FromRgb(0xE5, 0xE5, 0xE5));
                appRes["BrushTextPrimary"] = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27));
                appRes["BrushTextSecondary"] = new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63));
                appRes["BrushTextMuted"] = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
                appRes["BrushButtonBg"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                appRes["BrushButtonBgHover"] = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6));
                appRes["BrushButtonBorder"] = new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB));
                appRes["BrushAccent"] = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));

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
                var cores = Environment.ProcessorCount;

                TxtCpuName.Text = $"{cores} Çekirdek Aktif";
                TxtHostName.Text = machine;
                TxtOsInfo.Text = $"Windows ({osInfo})";
                TxtUptime.Text = $"{TimeSpan.FromMilliseconds(Environment.TickCount64):dd\\.hh\\:mm} Aktif";

                // IP Address resolution
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                TxtIpAddress.Text = ip.Address.ToString();
                                break;
                            }
                        }
                    }
                }

                Log("Sistem donanım ve ağ metrikleri başarıyla yüklendi.");
            }
            catch (Exception ex)
            {
                Log($"Özellikler yüklenirken hata: {ex.Message}");
            }
        }

        // ==========================================
        // 5 CATEGORY HUBS NAVIGATION
        // ==========================================

        private void HideAllHubs()
        {
            if (PanelHubDashboard != null) PanelHubDashboard.Visibility = Visibility.Collapsed;
            if (PanelHubHardware != null) PanelHubHardware.Visibility = Visibility.Collapsed;
            if (PanelHubPostFormat != null) PanelHubPostFormat.Visibility = Visibility.Collapsed;
            if (PanelHubRepair != null) PanelHubRepair.Visibility = Visibility.Collapsed;
            if (PanelHubAI != null) PanelHubAI.Visibility = Visibility.Collapsed;
        }

        private async void NavHub_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is not RadioButton rb) return;

            HideAllHubs();

            switch (rb.Name)
            {
                case "NavHubDashboard":
                    PanelHubDashboard.Visibility = Visibility.Visible;
                    TxtHeaderTitle.Text = "📊 Kontrol Merkezi & Ağ Filosu";
                    TxtHeaderSubtitle.Text = "Tüm çekirdek modüller, donanım parmak izi, LAN web sunucusu ve ağ operasyonları.";
                    if (SubNavDashboard != null) SubNavDashboard.IsChecked = true;
                    ShowDashboardSubPanel(PanelDashboard);
                    break;

                case "NavHubHardware":
                    PanelHubHardware.Visibility = Visibility.Visible;
                    TxtHeaderTitle.Text = "🛡️ Donanım, Sürücüler & Yazıcı Onarımı";
                    TxtHeaderSubtitle.Text = "Kriptografik SHA-256 Anti-Tamper mührü, Universal NIC ve 0x11b/0x709 yazıcı onarımı.";
                    if (SubNavHardware != null) SubNavHardware.IsChecked = true;
                    ShowHardwareSubPanel(PanelHardware);
                    await UpdateHwidBoxAsync();
                    break;

                case "NavHubPostFormat":
                    PanelHubPostFormat.Visibility = Visibility.Visible;
                    TxtHeaderTitle.Text = "🚀 Format Sonrası Kurulum & İnce Ayarlar";
                    TxtHeaderSubtitle.Text = "27+ Katılımsız paket, Sordum/Nilesoft tarzı geri alınabilir sağ tuş araçları & debloat.";
                    if (SubNavAppDeploy != null) SubNavAppDeploy.IsChecked = true;
                    ShowPostFormatSubPanel(PanelAppDeploy);
                    await UpdateWingetStatusAsync();
                    break;

                case "NavHubRepair":
                    PanelHubRepair.Visibility = Visibility.Visible;
                    TxtHeaderTitle.Text = "🔧 Sistem Onarımı & Bakım Merkezi";
                    TxtHeaderSubtitle.Text = "Akıllı disk temizliği, TCP/IP ağ onarımı, SFC/DISM, Geri Yükleme Noktası & süreçler.";
                    if (SubNavCleaner != null) SubNavCleaner.IsChecked = true;
                    ShowRepairSubPanel(PanelCleaner);
                    break;

                case "NavHubAI":
                    PanelHubAI.Visibility = Visibility.Visible;
                    TxtHeaderTitle.Text = "🤖 Yapay Zekâ Teşhis & Web Çözüm Motoru";
                    TxtHeaderSubtitle.Text = "İlk Resmi Microsoft kaynakları, ardından teknoloji forumları & onaylı rozetli çözümler.";
                    if (SubNavAiAssistant != null) SubNavAiAssistant.IsChecked = true;
                    if (PanelAI != null) PanelAI.Visibility = Visibility.Visible;
                    break;
            }

            Log($"Merkeze geçildi: {TxtHeaderTitle.Text}");
        }

        // ==========================================
        // SUB-NAVIGATION CHECKED HANDLERS
        // ==========================================

        private void SubNavDashboard_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || sender is not RadioButton rb) return;

            if (rb == SubNavDashboard) ShowDashboardSubPanel(PanelDashboard);
            else if (rb == SubNavLanFleet)
            {
                ShowDashboardSubPanel(PanelLanFleet);
                EnsureNetworkAdaptersLoaded();
            }
            else if (rb == SubNavLanServer) ShowDashboardSubPanel(PanelLanServer);
            else if (rb == SubNavIsoStudio) ShowDashboardSubPanel(PanelIsoStudio);
        }

        private void ShowDashboardSubPanel(StackPanel target)
        {
            if (PanelDashboard != null) PanelDashboard.Visibility = Visibility.Collapsed;
            if (PanelLanFleet != null) PanelLanFleet.Visibility = Visibility.Collapsed;
            if (PanelLanServer != null) PanelLanServer.Visibility = Visibility.Collapsed;
            if (PanelIsoStudio != null) PanelIsoStudio.Visibility = Visibility.Collapsed;

            if (target != null) target.Visibility = Visibility.Visible;
        }

        private void SubNavHardware_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || sender is not RadioButton rb) return;

            if (rb == SubNavHardware) ShowHardwareSubPanel(PanelHardware);
            else if (rb == SubNavDrivers) ShowHardwareSubPanel(PanelDrivers);
            else if (rb == SubNavPrinters) ShowHardwareSubPanel(PanelPrinters);
        }

        private void ShowHardwareSubPanel(StackPanel target)
        {
            if (PanelHardware != null) PanelHardware.Visibility = Visibility.Collapsed;
            if (PanelDrivers != null) PanelDrivers.Visibility = Visibility.Collapsed;
            if (PanelPrinters != null) PanelPrinters.Visibility = Visibility.Collapsed;

            if (target != null) target.Visibility = Visibility.Visible;
        }

        private void SubNavPostFormat_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || sender is not RadioButton rb) return;

            if (rb == SubNavAppDeploy) ShowPostFormatSubPanel(PanelAppDeploy);
            else if (rb == SubNavContextMenu) ShowPostFormatSubPanel(PanelContextMenu);
            else if (rb == SubNavDebloat) ShowPostFormatSubPanel(PanelDebloat);
        }

        private void ShowPostFormatSubPanel(StackPanel target)
        {
            if (PanelAppDeploy != null) PanelAppDeploy.Visibility = Visibility.Collapsed;
            if (PanelContextMenu != null) PanelContextMenu.Visibility = Visibility.Collapsed;
            if (PanelDebloat != null) PanelDebloat.Visibility = Visibility.Collapsed;

            if (target != null) target.Visibility = Visibility.Visible;
        }

        private void SubNavRepair_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || sender is not RadioButton rb) return;

            if (rb == SubNavCleaner) ShowRepairSubPanel(PanelCleaner);
            else if (rb == SubNavNetwork) ShowRepairSubPanel(PanelNetwork);
            else if (rb == SubNavDiagnostics) ShowRepairSubPanel(PanelDiagnostics);
            else if (rb == SubNavProcesses) ShowRepairSubPanel(PanelProcesses);
            else if (rb == SubNavStartup) ShowRepairSubPanel(PanelStartup);
        }

        private void ShowRepairSubPanel(StackPanel target)
        {
            if (PanelCleaner != null) PanelCleaner.Visibility = Visibility.Collapsed;
            if (PanelNetwork != null) PanelNetwork.Visibility = Visibility.Collapsed;
            if (PanelDiagnostics != null) PanelDiagnostics.Visibility = Visibility.Collapsed;
            if (PanelProcesses != null) PanelProcesses.Visibility = Visibility.Collapsed;
            if (PanelStartup != null) PanelStartup.Visibility = Visibility.Collapsed;

            if (target != null) target.Visibility = Visibility.Visible;
        }

        private void SubNavAI_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelAI != null) PanelAI.Visibility = Visibility.Visible;
        }

        // ==========================================
        // SIDEBAR COLLAPSE / EXPAND TOGGLE
        // ==========================================

        private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            _isSidebarCollapsed = !_isSidebarCollapsed;

            if (_isSidebarCollapsed)
            {
                ColSidebar.Width = new GridLength(68);
                BrandTextStack.Visibility = Visibility.Collapsed;
                TxtNavSectionHubs.Visibility = Visibility.Collapsed;
                TxtSidebarFooterText.Visibility = Visibility.Collapsed;

                if (BtnLogoToggle != null)
                {
                    BtnLogoToggle.Margin = new Thickness(0);
                    BtnLogoToggle.ToolTip = "Menüyü Aç / Genişlet (WinoxIT)";
                }

                SetNavButtonsCompact(true);
            }
            else
            {
                ColSidebar.Width = new GridLength(270);
                BrandTextStack.Visibility = Visibility.Visible;
                TxtNavSectionHubs.Visibility = Visibility.Visible;
                TxtSidebarFooterText.Visibility = Visibility.Visible;

                if (BtnLogoToggle != null)
                {
                    BtnLogoToggle.Margin = new Thickness(0, 0, 10, 0);
                    BtnLogoToggle.ToolTip = "Menüyü Daralt (WinoxIT)";
                }

                SetNavButtonsCompact(false);
            }
        }

        private void SetNavButtonsCompact(bool compact)
        {
            SetButtonText(NavHubDashboard, "📊", "📊  Kontrol Merkezi & Ağ Filosu", compact);
            SetButtonText(NavHubHardware, "🛡️", "🛡️  Donanım, Sürücüler & Yazıcı", compact);
            SetButtonText(NavHubPostFormat, "🚀", "🚀  Format Sonrası & İnce Ayarlar", compact);
            SetButtonText(NavHubRepair, "🔧", "🔧  Sistem Onarımı & Bakım", compact);
            SetButtonText(NavHubAI, "🤖", "🤖  Yapay Zekâ & Web Çözüm", compact);
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
        // 1. HUB: DASHBOARD & LAN SERVER & ISO & FLEET
        // ==========================================

        private async void BtnScanHealth_Click(object sender, RoutedEventArgs e)
        {
            Log("🔍 Kapsamlı sistem sağlık taraması yürütülüyor...");
            var res = await NativeSystemEngine.RunPowerShellAsync("Get-ComputerInfo | Select-Object WindowsProductName, CsManufacturer, CsModel, OsUptime, WindowsVersion | Format-List | Out-String");
            Log("Sistem Sağlığı Raporu:\n" + res.Output);
            ShowModal("Sistem Sağlık Raporu", res.Output);
        }

        private void BtnExportReport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"WinoxIT_Rapor_{DateTime.Now:yyyyMMdd_HHmmss}.html");
                var html = $"<html><body style='font-family:sans-serif;background:#181818;color:#fff;padding:20px;'>" +
                           $"<h1>WinoxIT Enterprise Sağlık Raporu</h1><p>Tarih: {DateTime.Now}</p>" +
                           $"<p>Cihaz: {Environment.MachineName}</p><pre>{_currentHardwareJson}</pre></body></html>";
                File.WriteAllText(path, html, Encoding.UTF8);
                Log($"HTML Rapor üretildi: {path}");
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log($"Rapor dışa aktarma hatası: {ex.Message}");
            }
        }

        private async void BtnRefreshDashboard_Click(object sender, RoutedEventArgs e)
        {
            Log("Metrikler yenileniyor...");
            await LoadSystemSpecsAsync();
        }

        // ==========================================
        // LAN FLEET: ETHERNET / WI-FI KEŞFİ & SUBNET TARAMASI
        // ==========================================

        private void EnsureNetworkAdaptersLoaded()
        {
            if (_cachedAdapters.Count > 0) return;
            RefreshNetworkAdapters();
        }

        private void BtnRefreshAdapters_Click(object sender, RoutedEventArgs e)
        {
            RefreshNetworkAdapters();
        }

        private void RefreshNetworkAdapters()
        {
            try
            {
                _cachedAdapters = NativeSystemEngine.GetLocalNetworkAdapters();
                CmbNetworkAdapters.ItemsSource = null;
                CmbNetworkAdapters.ItemsSource = _cachedAdapters;

                if (_cachedAdapters.Count > 0)
                {
                    CmbNetworkAdapters.SelectedIndex = 0;
                    Log($"Ağ Kartları Algılandı: {_cachedAdapters.Count} aktif ağ arayüzü listelendi.");
                }
                else
                {
                    Log("Aktif Ethernet veya Wi-Fi ağ kartı bulunamadı.");
                }
            }
            catch (Exception ex)
            {
                Log($"Ağ kartları listelenirken hata: {ex.Message}");
            }
        }

        private void CmbNetworkAdapters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbNetworkAdapters.SelectedItem is LocalNetworkAdapterItem adapter)
            {
                TxtScanRangeStart.Text = adapter.SubnetRangeStart;
                TxtScanRangeEnd.Text = adapter.SubnetRangeEnd;
                TxtScanStatusHint.Text = $"{adapter.InterfaceType} ({adapter.IpAddress}) seçildi.";
            }
        }

        private async void BtnStartSubnetScan_Click(object sender, RoutedEventArgs e)
        {
            string startIp = (TxtScanRangeStart.Text ?? "").Trim();
            string endIp = (TxtScanRangeEnd.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(startIp) || string.IsNullOrWhiteSpace(endIp))
            {
                MessageBox.Show("Lütfen geçerli bir başlangıç ve bitiş IP adresi giriniz.", "Geçersiz Aralık", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _subnetScanCts?.Cancel();
            _subnetScanCts = new CancellationTokenSource();
            var ct = _subnetScanCts.Token;

            BtnStartSubnetScan.IsEnabled = false;
            BtnStopSubnetScan.IsEnabled = true;
            ProgressSubnetScan.Value = 0;
            TxtScanStatusHint.Text = "⚡ Layer-2 ARP ve ICMP ile taranıyor...";

            _currentLanHosts.Clear();
            GridLanFleet.ItemsSource = null;
            TxtCountOnline.Text = "0";
            TxtCountFirewall.Text = "0";
            TxtCountIdle.Text = "0";
            TxtCountTotal.Text = "0 / 0";

            var sw = Stopwatch.StartNew();
            Log($"Yerel ağ taraması başlatıldı: {startIp} — {endIp} ...");

            var progress = new Progress<(int Current, int Total, LanHostNode? Found)>(p =>
            {
                ProgressSubnetScan.Maximum = p.Total;
                ProgressSubnetScan.Value = p.Current;
                TxtCountTotal.Text = $"{p.Current} / {p.Total}";
                TxtScanDuration.Text = $"Süre: {sw.Elapsed.TotalSeconds:0.#}s";

                if (p.Found != null)
                {
                    lock (_currentLanHosts)
                    {
                        if (!_currentLanHosts.Any(x => x.IPAddress == p.Found.IPAddress))
                        {
                            _currentLanHosts.Add(p.Found);
                        }
                    }

                    int onlineCount = _currentLanHosts.Count(x => x.StatusCode == "Online");
                    int firewallCount = _currentLanHosts.Count(x => x.StatusCode == "Firewall");
                    int idleCount = _currentLanHosts.Count(x => x.StatusCode == "Idle");

                    TxtCountOnline.Text = onlineCount.ToString();
                    TxtCountFirewall.Text = firewallCount.ToString();
                    TxtCountIdle.Text = idleCount.ToString();

                    ApplyLanHostFilter();
                }
            });

            try
            {
                var scanned = await NativeSystemEngine.ScanSubnetRangeAsync(startIp, endIp, progress, ct);
                _currentLanHosts = scanned;
                ApplyLanHostFilter();

                sw.Stop();
                int online = _currentLanHosts.Count(x => x.StatusCode == "Online");
                int firewall = _currentLanHosts.Count(x => x.StatusCode == "Firewall");
                int idle = _currentLanHosts.Count(x => x.StatusCode == "Idle");

                TxtScanStatusHint.Text = $"✅ Tarama tamamlandı ({sw.Elapsed.TotalSeconds:0.##}s). {online} açık, {firewall} korumalı, {idle} boşta.";
                Log($"Ağ taraması bitti: Toplam {_currentLanHosts.Count} IP tarandı ({online} açık, {firewall} korumalı, {idle} boşta).");
            }
            catch (OperationCanceledException)
            {
                TxtScanStatusHint.Text = "🛑 Tarama kullanıcı tarafından durduruldu.";
                Log("Ağ taraması durduruldu.");
            }
            catch (Exception ex)
            {
                TxtScanStatusHint.Text = $"Hata: {ex.Message}";
                Log($"Ağ tarama hatası: {ex.Message}");
            }
            finally
            {
                BtnStartSubnetScan.IsEnabled = true;
                BtnStopSubnetScan.IsEnabled = false;
            }
        }

        private void BtnStopSubnetScan_Click(object sender, RoutedEventArgs e)
        {
            _subnetScanCts?.Cancel();
            BtnStopSubnetScan.IsEnabled = false;
        }

        private void BtnFilterLan_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                _currentLanFilter = tag;
                ApplyLanHostFilter();
            }
        }

        private void TxtFilterHost_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyLanHostFilter();
        }

        private void ApplyLanHostFilter()
        {
            if (_currentLanHosts == null) return;

            var q = (TxtFilterHost.Text ?? "").Trim().ToLowerInvariant();
            IEnumerable<LanHostNode> query = _currentLanHosts;

            if (_currentLanFilter == "Online")
            {
                query = query.Where(x => x.StatusCode == "Online" || x.StatusCode == "Firewall");
            }
            else if (_currentLanFilter == "Idle")
            {
                query = query.Where(x => x.StatusCode == "Idle");
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(x => 
                    x.IPAddress.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    x.Hostname.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    x.MACAddress.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    x.DeviceType.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            GridLanFleet.ItemsSource = query.ToList();
        }

        private void GridLanFleet_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridLanFleet.SelectedItem is LanHostNode host)
            {
                TxtFleetTargetIp.Text = host.IPAddress;
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

        private void BtnOpenLanWeb_Click(object sender, RoutedEventArgs e)
        {
            var target = TxtFleetTargetIp.Text.Trim();
            if (string.IsNullOrWhiteSpace(target)) return;

            try
            {
                var url = target.StartsWith("http") ? target : $"http://{target}";
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                Log($"Cihazın web arayüzü tarayıcıda açıldı: {url}");
            }
            catch (Exception ex)
            {
                Log($"Web arayüzü açılamadı: {ex.Message}");
            }
        }

        private async void BtnProbeFleetTarget_Click(object sender, RoutedEventArgs e)
        {
            var target = TxtFleetTargetIp.Text.Trim();
            if (string.IsNullOrWhiteSpace(target)) return;
            Log($"Hedef cihaza WinoxIT sağlık probu gönderiliyor: http://{target}:9999/api/health ...");
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
                var res = await client.GetStringAsync($"http://{target}:9999/api/health");
                Log($"Cihaz yanıt verdi ({target}):\n{res}");
                ShowModal($"WinoxIT Sağlık Raporu ({target})", res);
            }
            catch (Exception ex)
            {
                Log($"Cihazda WinoxIT servisi algılanamadı ({target}): {ex.Message}");
                ShowModal("Sağlık Probu Hatası", $"Hedef cihazda (http://{target}:9999) WinoxIT sunucusu yanıt vermedi.\n\nHata: {ex.Message}");
            }
        }

        // Lan Server
        private string FindProjectFile(string fileName)
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 5; i++)
            {
                var candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;
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
                var port = TxtLanServerPort?.Text.Trim() ?? "9999";
                var response = await _httpClient.GetAsync($"http://localhost:{port}/api/health");
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
            var port = TxtLanServerPort?.Text.Trim() ?? "9999";
            if (online)
            {
                TxtServerMiniStatus.Text = $"Sunucu: 🟢 Port {port} Açık";
                TxtLanServerStatus.Text = $"Sunucu Durumu: 🟢 ÇEVRİMİÇİ (http://0.0.0.0:{port} üzerinden dinleniyor)";
            }
            else
            {
                TxtServerMiniStatus.Text = "Sunucu: 🔴 Kapalı";
                TxtLanServerStatus.Text = "Sunucu Durumu: 🔴 KAPALI (Başlatılmadı)";
            }
        }

        private async void BtnStartLanServer_Click(object sender, RoutedEventArgs e)
        {
            Log("Sanal Ağ Sunucusu başlatılıyor...");
            if (await IsServerOnlineAsync())
            {
                Log("Sunucu zaten çalışıyor.");
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
                    TxtLanServerLog.AppendText($"[BAŞLATILDI] Node.js sunucusu PID {_serverProcess?.Id}\r\n");
                    await Task.Delay(1500);
                    await CheckServerStatusAsync();
                }
                else
                {
                    Log("server.js bulunamadı.");
                }
            }
            catch (Exception ex)
            {
                Log($"Sunucu başlatılamadı: {ex.Message}. Node.js yüklü mü kontrol edin.");
            }
        }

        private async void BtnStopLanServer_Click(object sender, RoutedEventArgs e)
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
                TxtLanServerLog.AppendText("[DURDURULDU] Sunucu kapatıldı.\r\n");
            }
            catch (Exception ex)
            {
                Log($"Durdurma hatası: {ex.Message}");
            }
        }

        private async void BtnOpenBrowser_Click(object sender, RoutedEventArgs e)
        {
            var port = TxtLanServerPort?.Text.Trim() ?? "9999";
            if (!await IsServerOnlineAsync())
            {
                Log("Sunucu henüz açık değil, önce başlatılıyor...");
                BtnStartLanServer_Click(sender, e);
                await Task.Delay(1500);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = $"http://localhost:{port}/dashboard.html",
                UseShellExecute = true
            });
            Log("Web Kokpiti tarayıcıda açıldı.");
        }

        private void BtnQuickServer_Click(object sender, RoutedEventArgs e)
        {
            BtnOpenBrowser_Click(sender, e);
        }

        // Iso Studio
        private void BtnBrowseIso_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "ISO İmajı (*.iso)|*.iso|Tüm Dosyalar (*.*)|*.*",
                Title = "Windows Kurulum ISO Dosyası Seçin"
            };
            if (ofd.ShowDialog() == true) TxtIsoPath.Text = ofd.FileName;
        }

        private void BtnBrowseWim_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "WIM İmaj Dosyası (*.wim)|*.wim|Tüm Dosyalar (*.*)|*.*",
                Title = "WIM İmaj Dosyası Seçin"
            };
            if (ofd.ShowDialog() == true) TxtWimPath.Text = ofd.FileName;
        }

        private void BtnBrowseMount_Click(object sender, RoutedEventArgs e)
        {
            var fbd = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "WIM Geçici Bağlama (Mount) Klasörünü Seçin"
            };
            if (fbd.ShowDialog() == true) TxtMountDir.Text = fbd.FolderName;
        }

        private async void BtnWimMount_Click(object sender, RoutedEventArgs e)
        {
            Log($"DISM Mount başlatılıyor: {TxtWimPath.Text} -> {TxtMountDir.Text}");
            var (success, output) = await NativeSystemEngine.MountWimAsync(TxtWimPath.Text, 1, TxtMountDir.Text);
            Log(output);
            ShowModal("DISM Mount Sonucu", output);
        }

        private async void BtnWimInjectDrivers_Click(object sender, RoutedEventArgs e)
        {
            var driversDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "drivers", "universal_nic");
            Log($"Sürücü enjeksiyonu başlatılıyor: {driversDir}");
            var (success, output) = await NativeSystemEngine.InjectDriversToWimAsync(TxtMountDir.Text, driversDir);
            Log(output);
            ShowModal("Sürücü Enjeksiyon Sonucu", output);
        }

        private async void BtnWimCommit_Click(object sender, RoutedEventArgs e)
        {
            Log($"DISM Commit (Kaydet & Kapat) başlatılıyor: {TxtMountDir.Text}");
            var (success, output) = await NativeSystemEngine.UnmountWimAsync(TxtMountDir.Text, commit: true);
            Log(output);
            ShowModal("DISM Commit Sonucu", output);
        }

        private async void BtnWimDiscard_Click(object sender, RoutedEventArgs e)
        {
            Log($"DISM Discard (İptal Et) başlatılıyor: {TxtMountDir.Text}");
            var (success, output) = await NativeSystemEngine.UnmountWimAsync(TxtMountDir.Text, commit: false);
            Log(output);
            ShowModal("DISM Discard Sonucu", output);
        }

        // ==========================================
        // 2. HUB: DONANIM, SÜRÜCÜLER & YAZICI
        // ==========================================

        private async Task UpdateHwidBoxAsync()
        {
            var info = await Task.Run(() => NativeSystemEngine.GetHardwareInfoNative());
            _currentHardwareJson = info.Json;
            TxtHwidHash.Text = info.Hash;
            TxtHwidCpu.Text = info.Cpu;
            TxtHwidMobo.Text = info.Mobo;
            TxtHwidDisk.Text = info.Disk;
            TxtHwidMac.Text = info.Mac;
            TxtHwidGpu.Text = info.Gpu;

            var baselinePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hardware_baseline.json");
            if (File.Exists(baselinePath))
            {
                TxtAntiTamperStatus.Text = "🟢 Mühür Kayıtlı: Sistem donanım değişikliği koruması aktif.";
            }
            else
            {
                TxtAntiTamperStatus.Text = "⚪ Henüz mühür kaydedilmedi. Donanımı kilitlemek için aşağıdaki butona basınız.";
            }
        }

        private async void BtnVerifyHardware_Click(object sender, RoutedEventArgs e)
        {
            Log("Donanım bileşenleri doğrulanıyor...");
            var baselinePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hardware_baseline.json");
            if (!File.Exists(baselinePath))
            {
                MessageBox.Show("Henüz kaydedilmiş bir mühür bulunamadı. Lütfen önce 'Mevcut Donanımı Mühürle' butonuna basınız.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var baselineContent = File.ReadAllText(baselinePath);
            var (matched, diffLog) = await NativeSystemEngine.CompareHardwareBaselineAsync(baselineContent);

            if (matched)
            {
                Log("✅ Donanım doğrulandı: %100 Orijinal eşleşme.");
                MessageBox.Show("Donanım Doğrulama Başarılı!\n\nTüm parçalar orijinal mühürle %100 eşleşiyor.", "Anti-Tamper Doğrulandı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                Log("⚠️ ALARM: Donanım sapması tespit edildi!");
                ShowModal("⚠️ Donanım Değişikliği Alarmı", diffLog);
            }
        }

        private void BtnAntiTamperLock_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentHardwareJson)) return;
            var baselinePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hardware_baseline.json");
            File.WriteAllText(baselinePath, _currentHardwareJson);
            TxtAntiTamperStatus.Text = "🟢 Mühür Başarıyla Kilitlendi! Değişiklikler anında tespit edilecektir.";
            Log("Mevcut donanım profili SHA-256 mührüyle kilitlendi.");
            MessageBox.Show("Donanım başarıyla mühürlendi!\n\nRAM, SSD veya anakart değiştirildiğinde alarm verilecektir.", "Mühür Kaydedildi", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnExportHwid_Click(object sender, RoutedEventArgs e)
        {
            var reportPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"WinoxIT_HWID_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            var content = $"=== WINOXIT DONANIM VE EKSPERTİZ RAPORU ===\r\nTarih: {DateTime.Now}\r\n" +
                          $"Baseline HWID: {TxtHwidHash.Text}\r\n\r\nBileşen Detayları:\r\n{_currentHardwareJson}";
            File.WriteAllText(reportPath, content);
            Log($"HWID raporu kaydedildi: {reportPath}");
            Process.Start(new ProcessStartInfo { FileName = "notepad.exe", Arguments = $"\"{reportPath}\"" });
        }

        // Drivers
        private async void BtnScanDrivers_Click(object sender, RoutedEventArgs e)
        {
            Log("Sistemdeki sürücüler taranıyor (pnputil)...");
            var drivers = await NativeSystemEngine.GetInstalledDriversAsync();
            GridDrivers.ItemsSource = drivers;
            TxtDriverCount.Text = $"{drivers.Count} Sürücü Bulundu";
            Log($"Sürücü taraması tamamlandı: {drivers.Count} adet 3. parti sürücü listelendi.");
        }

        private async void BtnExportDrivers_Click(object sender, RoutedEventArgs e)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var exportDir = Path.Combine(desktop, "WinoxIT_Driver_Backup");
            if (!Directory.Exists(exportDir)) Directory.CreateDirectory(exportDir);

            Log($"Tüm sürücüler DISM ile dışa aktarılıyor: {exportDir} ...");
            var res = await NativeSystemEngine.RunCmdAsync($"dism /online /export-driver /destination:\"{exportDir}\"", 180000);
            Log(res.Output);
            ShowModal("Sürücü Yedekleme Sonucu", res.Output);
        }

        private async void BtnInjectUniversalNic_Click(object sender, RoutedEventArgs e)
        {
            Log("🚀 Evrensel Çevrimdışı NIC Sürücüleri yükleniyor...");
            var res = await NativeSystemEngine.InstallOfflineNicDriversAsync();
            Log(res);
            ShowModal("Universal NIC Yükleme Sonucu", res);
        }

        // Printers
        private async void BtnFixPrinters_Click(object sender, RoutedEventArgs e)
        {
            Log("0x0000011b RPC yetkilendirme onarımı başlatılıyor...");
            var r = await NativeSystemEngine.FixPrinterRpc11bAsync();
            Log(r);
            TxtPrinterLog.Text = r;
        }

        private async void BtnFix709_Click(object sender, RoutedEventArgs e)
        {
            Log("0x00000709 Point & Print ilkesi sıfırlanıyor...");
            var r = await NativeSystemEngine.FixPointAndPrint709Async();
            Log(r);
            TxtPrinterLog.Text = r;
        }

        private async void BtnPurgeSpooler_Click(object sender, RoutedEventArgs e)
        {
            Log("Spooler servisi temizleniyor ve yeniden başlatılıyor...");
            var r = await NativeSystemEngine.RunCmdAsync("net stop spooler && del /Q /F /S \"%systemroot%\\System32\\Spool\\Printers\\*.*\" && net start spooler");
            Log(r.Output);
            TxtPrinterLog.Text = "Spooler kuyruğu temizlendi ve servis başlatıldı:\r\n" + r.Output;
        }

        private async void BtnClearQueue_Click(object sender, RoutedEventArgs e)
        {
            Log("Yazıcı kuyruğu temizleniyor...");
            var res = await NativeSystemEngine.RunPowerShellAsync("Get-PrintJob -PrinterName * -ErrorAction SilentlyContinue | Remove-PrintJob -ErrorAction SilentlyContinue; 'Yazıcı kuyruğu temizlendi.'");
            Log(res.Output);
            TxtPrinterLog.Text = res.Output;
        }

        // ==========================================
        // 3. HUB: FORMAT SONRASI, REVERSIBLE SAĞ TUŞ & DEBLOAT
        // ==========================================

        private async Task UpdateWingetStatusAsync()
        {
            var (avail, ver) = await NativeSystemEngine.CheckWingetAsync();
            if (avail)
                TxtWingetStatus.Text = $"🟢 Winget Aktif ({ver})";
            else
                TxtWingetStatus.Text = "🔴 Winget Bulunamadı";
        }

        private void BtnSelectAllApps_Click(object sender, RoutedEventArgs e)
        {
            SetAllAppCheckboxes(true);
            Log("Tüm kurulum paketleri seçildi.");
        }

        private void BtnClearAllApps_Click(object sender, RoutedEventArgs e)
        {
            SetAllAppCheckboxes(false);
            Log("Tüm kurulum paketlerinin seçimi kaldırıldı.");
        }

        private void SetAllAppCheckboxes(bool isChecked)
        {
            var boxes = new CheckBox?[]
            {
                ChkChrome, ChkFirefox, ChkBrave, ChkEdge, ChkOpera,
                ChkAnyDesk, ChkRustDesk, ChkTeamViewer, ChkWinScp, ChkPutty,
                Chk7Zip, ChkWinRar, ChkEverything, ChkPowerToys, ChkTreeSize,
                ChkNotepadPlus, ChkLibreOffice, ChkAdobeReader, ChkFoxitReader, ChkSumatra,
                ChkVlc, ChkKLite, ChkIrfanView,
                ChkVcRedist, ChkVSCode, ChkGit, ChkNodeJs, ChkPython,
                ChkUyap, ChkAkis
            };

            foreach (var b in boxes)
            {
                if (b != null) b.IsChecked = isChecked;
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

            // Browsers
            if (ChkChrome?.IsChecked == true) targets.Add(("Google.Chrome", "Google Chrome"));
            if (ChkFirefox?.IsChecked == true) targets.Add(("Mozilla.Firefox", "Mozilla Firefox"));
            if (ChkBrave?.IsChecked == true) targets.Add(("Brave.Brave", "Brave Browser"));
            if (ChkEdge?.IsChecked == true) targets.Add(("Microsoft.Edge", "Microsoft Edge"));
            if (ChkOpera?.IsChecked == true) targets.Add(("Opera.Opera", "Opera"));

            // Remote
            if (ChkAnyDesk?.IsChecked == true) targets.Add(("AnyDeskSoftwareGmbH.AnyDesk", "AnyDesk"));
            if (ChkRustDesk?.IsChecked == true) targets.Add(("RustDesk.RustDesk", "RustDesk"));
            if (ChkTeamViewer?.IsChecked == true) targets.Add(("TeamViewer.TeamViewer", "TeamViewer"));
            if (ChkWinScp?.IsChecked == true) targets.Add(("WinSCP.WinSCP", "WinSCP"));
            if (ChkPutty?.IsChecked == true) targets.Add(("PuTTY.PuTTY", "PuTTY"));

            // Utils & Archive
            if (Chk7Zip?.IsChecked == true) targets.Add(("7zip.7zip", "7-Zip"));
            if (ChkWinRar?.IsChecked == true) targets.Add(("RARLab.WinRAR", "WinRAR"));
            if (ChkEverything?.IsChecked == true) targets.Add(("voidtools.Everything", "Everything"));
            if (ChkPowerToys?.IsChecked == true) targets.Add(("Microsoft.PowerToys", "PowerToys"));
            if (ChkTreeSize?.IsChecked == true) targets.Add(("JAMSoftware.TreeSize.Free", "TreeSize Free"));

            // Editors & Office
            if (ChkNotepadPlus?.IsChecked == true) targets.Add(("Notepad++.Notepad++", "Notepad++"));
            if (ChkLibreOffice?.IsChecked == true) targets.Add(("TheDocumentFoundation.LibreOffice", "LibreOffice"));
            if (ChkAdobeReader?.IsChecked == true) targets.Add(("Adobe.Acrobat.Reader.64-bit", "Adobe Acrobat Reader"));
            if (ChkFoxitReader?.IsChecked == true) targets.Add(("Foxit.FoxitReader", "Foxit Reader"));
            if (ChkSumatra?.IsChecked == true) targets.Add(("SumatraPDF.SumatraPDF", "SumatraPDF"));

            // Media
            if (ChkVlc?.IsChecked == true) targets.Add(("VideoLAN.VLC", "VLC Media Player"));
            if (ChkKLite?.IsChecked == true) targets.Add(("CodecGuide.K-LiteCodecPack.Mega", "K-Lite Mega Codec"));
            if (ChkIrfanView?.IsChecked == true) targets.Add(("IrfanSkiljan.IrfanView", "IrfanView"));

            // Dev & Runtimes
            if (ChkVcRedist?.IsChecked == true) targets.Add(("Microsoft.VCRedist.2015+.x64", "Visual C++ Runtimes"));
            if (ChkVSCode?.IsChecked == true) targets.Add(("Microsoft.VisualStudioCode", "Visual Studio Code"));
            if (ChkGit?.IsChecked == true) targets.Add(("Git.Git", "Git for Windows"));
            if (ChkNodeJs?.IsChecked == true) targets.Add(("OpenJS.NodeJS.LTS", "Node.js LTS"));
            if (ChkPython?.IsChecked == true) targets.Add(("Python.Python.3.12", "Python 3.12"));

            // Public & e-Sign
            if (ChkUyap?.IsChecked == true) targets.Add(("AdaletBakanligi.UyapEditor", "UYAP Editör"));
            if (ChkAkis?.IsChecked == true) targets.Add(("Tubitak.Akis", "AKİS Akıllı Kart"));

            if (targets.Count == 0)
            {
                MessageBox.Show("Lütfen kurulacak en az bir uygulama seçiniz.", "Seçim Yapılmadı", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Log($"🚀 {targets.Count} adet uygulama sessiz kurulum sırasına alındı...");
            TxtAppDeployLog.Text = $"Kurulum başlatılıyor ({targets.Count} paket)...\r\n";

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

        // ==============================================================
        // REVERSIBLE WINDOWS TOOLS & CONTEXT MENU HANDLERS (SORDUM/NILESOFT)
        // ==============================================================

        private async void BtnEnableClassicMenu_Click(object sender, RoutedEventArgs e)
        {
            Log("Klasik Windows 10 sağ tık menüsü uygulanıyor...");
            var res = await NativeSystemEngine.RestoreClassicContextMenuAsync();
            Log(res);
            MessageBox.Show(res, "Klasik Sağ Tuş", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnRevertModernMenu_Click(object sender, RoutedEventArgs e)
        {
            Log("Windows 11 varsayılan modern sağ tık menüsüne geri dönülüyor...");
            var res = await NativeSystemEngine.RevertModernContextMenuAsync();
            Log(res);
            MessageBox.Show(res, "Modern Sağ Tuş", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnEnablePhotoViewer_Click(object sender, RoutedEventArgs e)
        {
            Log("Klasik Windows Fotoğraf Görüntüleyici etkinleştiriliyor...");
            var res = await NativeSystemEngine.EnableClassicPhotoViewerAsync();
            Log(res);
            MessageBox.Show(res, "Fotoğraf Görüntüleyici", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnRevertPhotoViewer_Click(object sender, RoutedEventArgs e)
        {
            Log("Varsayılan Fotoğraflar uygulamasına geri dönülüyor...");
            var res = await NativeSystemEngine.RevertDefaultPhotoViewerAsync();
            Log(res);
            MessageBox.Show(res, "Fotoğraf Görüntüleyici", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnEnableTakeOwnership_Click(object sender, RoutedEventArgs e)
        {
            Log("Sağ tık menüsüne 'Sahipliği Al' ekleniyor...");
            var res = await NativeSystemEngine.EnableTakeOwnershipMenuAsync();
            Log(res);
            MessageBox.Show(res, "Sahipliği Al", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnDisableTakeOwnership_Click(object sender, RoutedEventArgs e)
        {
            Log("Sağ tık menüsünden 'Sahipliği Al' kaldırılıyor...");
            var res = await NativeSystemEngine.DisableTakeOwnershipMenuAsync();
            Log(res);
            MessageBox.Show(res, "Sahipliği Al", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnEnableAdminShell_Click(object sender, RoutedEventArgs e)
        {
            Log("Sağ tık arka planına Yönetici CMD & PowerShell ekleniyor...");
            var res = await NativeSystemEngine.EnableAdminShellContextMenuAsync();
            Log(res);
            MessageBox.Show(res, "Yönetici Terminal", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnDisableAdminShell_Click(object sender, RoutedEventArgs e)
        {
            Log("Sağ tık arka planından Yönetici terminalleri kaldırılıyor...");
            var res = await NativeSystemEngine.DisableAdminShellContextMenuAsync();
            Log(res);
            MessageBox.Show(res, "Yönetici Terminal", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnEnableSpoolerRestart_Click(object sender, RoutedEventArgs e)
        {
            Log("Masaüstü sağ tık menüsüne 'Yazıcı Sıfırla' ekleniyor...");
            var res = await NativeSystemEngine.EnableSpoolerRestartMenuAsync();
            Log(res);
            MessageBox.Show(res, "Yazıcı Sıfırla Menüsü", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnDisableSpoolerRestart_Click(object sender, RoutedEventArgs e)
        {
            Log("Masaüstü sağ tık menüsünden 'Yazıcı Sıfırla' kaldırılıyor...");
            var res = await NativeSystemEngine.DisableSpoolerRestartMenuAsync();
            Log(res);
            MessageBox.Show(res, "Yazıcı Sıfırla Menüsü", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnEnablePowerMenu_Click(object sender, RoutedEventArgs e)
        {
            Log("Masaüstü sağ tık menüsüne Gelişmiş Güç Menüsü ekleniyor...");
            var res = await NativeSystemEngine.EnablePowerMenuContextMenuAsync();
            Log(res);
            MessageBox.Show(res, "Gelişmiş Güç Menüsü", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnDisablePowerMenu_Click(object sender, RoutedEventArgs e)
        {
            Log("Masaüstü sağ tık menüsünden Güç Menüsü kaldırılıyor...");
            var res = await NativeSystemEngine.DisablePowerMenuContextMenuAsync();
            Log(res);
            MessageBox.Show(res, "Gelişmiş Güç Menüsü", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnShowHiddenExt_Click(object sender, RoutedEventArgs e)
        {
            Log("Gizli dosyalar ve dosya uzantıları görünür yapılıyor...");
            var res = await NativeSystemEngine.ShowHiddenFilesAndExtensionsAsync();
            Log(res);
            MessageBox.Show(res, "Dosya Gezgini Ayarları", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnHideHiddenExt_Click(object sender, RoutedEventArgs e)
        {
            Log("Gizli dosyalar ve dosya uzantıları varsayılan gizli durumuna getiriliyor...");
            var res = await NativeSystemEngine.RestoreHiddenFilesAndExtensionsAsync();
            Log(res);
            MessageBox.Show(res, "Dosya Gezgini Ayarları", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Reversible Debloat
        private async void BtnDisableCopilot_Click(object sender, RoutedEventArgs e)
        {
            Log("Copilot ve Recall telemetrisi kapatılıyor...");
            var res = await NativeSystemEngine.DisableCopilotAndRecallAsync();
            Log(res);
            MessageBox.Show(res, "Copilot & Recall", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnEnableCopilot_Click(object sender, RoutedEventArgs e)
        {
            Log("Copilot ve Recall telemetrisi geri açılıyor (Varsayılan)...");
            var res = await NativeSystemEngine.EnableCopilotAndRecallAsync();
            Log(res);
            MessageBox.Show(res, "Copilot & Recall", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnDisableTelemetry_Click(object sender, RoutedEventArgs e)
        {
            Log("Telemetri servisleri kapatılıyor...");
            var res = await NativeSystemEngine.RunPowerShellAsync("Stop-Service DiagTrack, dmwappushservice -Force -ErrorAction SilentlyContinue; Set-Service DiagTrack, dmwappushservice -StartupType Disabled -ErrorAction SilentlyContinue; 'Telemetri servisleri devre dışı bırakıldı.'");
            Log(res.Output);
            MessageBox.Show(res.Output, "Telemetri Kapatıldı", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnEnableTelemetry_Click(object sender, RoutedEventArgs e)
        {
            Log("Telemetri servisleri geri açılıyor...");
            var res = await NativeSystemEngine.EnableTelemetryAsync();
            Log(res);
            MessageBox.Show(res, "Telemetri Açıldı", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnDisableBingSearch_Click(object sender, RoutedEventArgs e)
        {
            Log("Başlat Bing web aramaları kapatılıyor...");
            var res = await NativeSystemEngine.RunPowerShellAsync("Set-ItemProperty -Path 'HKCU:\\Software\\Policies\\Microsoft\\Windows\\Explorer' -Name 'DisableSearchBoxSuggestions' -Value 1 -Type DWord -Force -ErrorAction SilentlyContinue; 'Bing web aramaları kapatıldı.'");
            Log(res.Output);
            MessageBox.Show(res.Output, "Bing Arama Kapatıldı", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnEnableBingSearch_Click(object sender, RoutedEventArgs e)
        {
            Log("Başlat Bing web aramaları geri açılıyor...");
            var res = await NativeSystemEngine.EnableBingSearchAsync();
            Log(res);
            MessageBox.Show(res, "Bing Arama Açıldı", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnClassicMenu_Click(object sender, RoutedEventArgs e)
        {
            // Switch to ContextMenu tab
            if (SubNavContextMenu != null) SubNavContextMenu.IsChecked = true;
            ShowPostFormatSubPanel(PanelContextMenu);
        }

        // ==========================================
        // 4. HUB: SİSTEM ONARIMI & BAKIM MERKEZİ
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
            Log("Windows Update indirme önbelleği sıfırlanıyor...");
            var res = await NativeSystemEngine.RunPowerShellAsync("Stop-Service wuauserv -Force -ErrorAction SilentlyContinue; Remove-Item 'C:\\Windows\\SoftwareDistribution\\Download\\*' -Recurse -Force -ErrorAction SilentlyContinue; Start-Service wuauserv -ErrorAction SilentlyContinue; 'Windows Update önbelleği temizlendi.'");
            Log(res.Output);
        }

        private async void BtnEmptyRecycle_Click(object sender, RoutedEventArgs e)
        {
            Log("Geri Dönüşüm Kutusu boşaltılıyor...");
            var res = await NativeSystemEngine.RunPowerShellAsync("Clear-RecycleBin -Force -ErrorAction SilentlyContinue; 'Geri Dönüşüm Kutusu boşaltıldı.'");
            Log(res.Output);
        }

        private async void BtnFlushDns_Click(object sender, RoutedEventArgs e)
        {
            Log("DNS önbelleği temizleniyor...");
            var res = await NativeSystemEngine.FlushDnsAsync();
            Log(res);
        }

        private async void BtnFixNetworkAdapter_Click(object sender, RoutedEventArgs e)
        {
            Log("Ağ bağdaştırıcıları yeniden başlatılıyor...");
            var res = await NativeSystemEngine.RunPowerShellAsync("Get-NetAdapter | Restart-NetAdapter -Confirm:$false -ErrorAction SilentlyContinue; 'Ağ bağdaştırıcıları yeniden başlatıldı.'");
            Log(res.Output);
            MessageBox.Show(res.Output, "Ağ Bağdaştırıcıları", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnResetWinsock_Click(object sender, RoutedEventArgs e)
        {
            Log("Winsock ve TCP/IP ağ protokolü fabrika ayarlarına sıfırlanıyor...");
            var res = await NativeSystemEngine.RunCmdAsync("netsh winsock reset && netsh int ip reset");
            Log("Winsock sıfırlandı: " + res.Output);
            MessageBox.Show("Winsock ve TCP/IP sıfırlandı. Değişikliklerin geçerli olması için bilgisayarınızı yeniden başlatmanız önerilir.", "Ağ Sıfırlandı", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnReleaseRenew_Click(object sender, RoutedEventArgs e)
        {
            Log("IP adresi yenileniyor (release & renew)...");
            var res = await NativeSystemEngine.RunCmdAsync("ipconfig /release && ipconfig /renew");
            Log("IP yenilendi: " + res.Output);
            MessageBox.Show("IP adresi başarıyla yenilendi.", "DHCP Yenilendi", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnCreateRestorePoint_Click(object sender, RoutedEventArgs e)
        {
            Log("🛡️ Güvenli Sistem Geri Yükleme Noktası oluşturuluyor...");
            var (success, output) = await NativeSystemEngine.CreateRestorePointAsync("WinoxIT Bakim Oncesi Guvenli Nokta");
            Log(output);
            MessageBox.Show(output, success ? "Geri Yükleme Noktası" : "Uyarı", MessageBoxButton.OK, success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private async void BtnRunSfc_Click(object sender, RoutedEventArgs e)
        {
            Log("SFC /scannow başlatıldı (Bu işlem 3-5 dakika sürebilir)...");
            var res = await NativeSystemEngine.RunCmdAsync("sfc /scannow", 300000);
            Log(res.Output);
            ShowModal("SFC /scannow Doğrulama Sonucu", res.Output);
        }

        private async void BtnRunDism_Click(object sender, RoutedEventArgs e)
        {
            Log("DISM /Online /Cleanup-Image /RestoreHealth başlatıldı...");
            var res = await NativeSystemEngine.RunCmdAsync("dism /Online /Cleanup-Image /RestoreHealth", 600000);
            Log(res.Output);
            ShowModal("DISM /RestoreHealth Onarım Sonucu", res.Output);
        }

        private async void BtnRunChkdsk_Click(object sender, RoutedEventArgs e)
        {
            Log("Chkdsk C: /scan başlatıldı...");
            var res = await NativeSystemEngine.RunCmdAsync("chkdsk C: /scan", 180000);
            Log(res.Output);
            ShowModal("Chkdsk Disk Taraması Sonucu", res.Output);
        }

        // Processes & Startup
        private async void BtnRefreshProcesses_Click(object sender, RoutedEventArgs e)
        {
            Log("Çalışan işlemler listeleniyor...");
            var script = "Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 30 Id, ProcessName, @{N='MemoryMB';E={[math]::Round($_.WorkingSet64/1MB,1)}}, Responding | ConvertTo-Json -Compress";
            var res = await NativeSystemEngine.RunPowerShellAsync(script);
            if (res.Success && !string.IsNullOrWhiteSpace(res.Output))
            {
                try
                {
                    var list = new List<ProcessItem>();
                    using var doc = System.Text.Json.JsonDocument.Parse(res.Output);
                    if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var el in doc.RootElement.EnumerateArray())
                        {
                            list.Add(new ProcessItem
                            {
                                Id = el.TryGetProperty("Id", out var pId) ? pId.GetInt32() : 0,
                                Name = el.TryGetProperty("ProcessName", out var pName) ? pName.GetString() ?? "" : "",
                                MemoryMB = el.TryGetProperty("MemoryMB", out var pMem) ? pMem.GetDouble() : 0,
                                Responding = el.TryGetProperty("Responding", out var pR) && pR.GetBoolean() ? "Evet" : "Yanıt Vermiyor"
                            });
                        }
                    }
                    GridProcesses.ItemsSource = list;
                    Log($"{list.Count} adet işlem yüklendi.");
                }
                catch (Exception ex)
                {
                    Log($"İşlemleri ayrıştırma hatası: {ex.Message}");
                }
            }
        }

        private async void BtnKillSelectedProcess_Click(object sender, RoutedEventArgs e)
        {
            if (GridProcesses.SelectedItem is ProcessItem item)
            {
                Log($"İşlem sonlandırılıyor: {item.Name} (PID: {item.Id})...");
                var res = await NativeSystemEngine.RunCmdAsync($"taskkill /f /pid {item.Id}");
                Log(res.Output);
                BtnRefreshProcesses_Click(sender, e);
            }
            else
            {
                MessageBox.Show("Lütfen tablodan sonlandırılacak bir işlem seçiniz.", "İşlem Seçilmedi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void BtnRefreshStartup_Click(object sender, RoutedEventArgs e)
        {
            Log("Windows Başlangıç programları taranıyor (Kayıt Defteri & Görevler)...");
            try
            {
                var list = await NativeSystemEngine.GetStartupItemsAsync();
                GridStartup.ItemsSource = list;
                int activeCount = list.Count(x => x.IsEnabled);
                int disabledCount = list.Count(x => !x.IsEnabled);
                Log($"{list.Count} başlangıç öğesi listelendi ({activeCount} etkin, {disabledCount} devre dışı).");
            }
            catch (Exception ex)
            {
                Log($"Başlangıç öğelerini okuma hatası: {ex.Message}");
            }
        }

        private async void BtnEnableStartupItem_Click(object sender, RoutedEventArgs e)
        {
            if (GridStartup.SelectedItem is StartupItemModel item)
            {
                Log($"Başlangıç öğesi etkinleştiriliyor: {item.Name}...");
                var (ok, msg) = await NativeSystemEngine.EnableStartupItemAsync(item);
                Log(msg);
                MessageBox.Show(msg, "Başlangıç Düzenleme", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
                BtnRefreshStartup_Click(sender, e);
            }
            else
            {
                MessageBox.Show("Lütfen tablodan etkinleştirilecek bir başlangıç öğesi seçiniz.", "Öğe Seçilmedi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void BtnDisableStartupItem_Click(object sender, RoutedEventArgs e)
        {
            if (GridStartup.SelectedItem is StartupItemModel item)
            {
                Log($"Başlangıç öğesi devre dışı bırakılıyor: {item.Name}...");
                var (ok, msg) = await NativeSystemEngine.DisableStartupItemAsync(item);
                Log(msg);
                MessageBox.Show(msg, "Başlangıç Düzenleme", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
                BtnRefreshStartup_Click(sender, e);
            }
            else
            {
                MessageBox.Show("Lütfen tablodan devre dışı bırakılacak bir başlangıç öğesi seçiniz.", "Öğe Seçilmedi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void BtnRemoveStartupItem_Click(object sender, RoutedEventArgs e)
        {
            if (GridStartup.SelectedItem is StartupItemModel item)
            {
                var confirm = MessageBox.Show($"'{item.Name}' başlangıç kaydı sistemden tamamen silinecektir.\nDevam etmek istiyor musunuz?", "Başlangıç Kaydı Sil", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) return;

                Log($"Başlangıç öğesi siliniyor: {item.Name}...");
                var (ok, msg) = await NativeSystemEngine.RemoveStartupItemAsync(item);
                Log(msg);
                MessageBox.Show(msg, "Başlangıç Düzenleme", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
                BtnRefreshStartup_Click(sender, e);
            }
            else
            {
                MessageBox.Show("Lütfen tablodan silinecek bir başlangıç öğesi seçiniz.", "Öğe Seçilmedi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ==========================================
        // 5. HUB: YAPAY ZEKÂ TEŞHİS, ÇOK KATMANLI WEB & ONAYLI ROZET
        // ==========================================

        private void TxtAiIssueQuery_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                BtnAiAnalyze_Click(sender, e);
            }
        }

        private void BtnAiPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Content is string text)
            {
                TxtAiIssueQuery.Text = text;
                BtnAiAnalyze_Click(sender, e);
            }
        }

        private async void BtnAiAnalyze_Click(object sender, RoutedEventArgs e)
        {
            string query = (TxtAiIssueQuery.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                MessageBox.Show("Lütfen analiz edilecek bir arıza, mavi ekran (BSOD) kodu veya PowerShell/CMD sorusu yazınız.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Log($"🤖 Yapay Zekâ çok katmanlı araştırma motoru tetiklendi: '{query}'...");
            BtnAiAnalyze.IsEnabled = false;
            BtnAiAnalyze.Content = "⏳ Analiz Ediliyor...";

            try
            {
                var result = await AIDiagnosticEngine.Instance.AnalyzeIssueAsync(query);
                if (result != null)
                {
                    CardAiSolution.Visibility = Visibility.Visible;
                    TxtAiSolutionTitle.Text = result.Title;
                    TxtAiCategoryText.Text = $"Kategori: {result.Category}";
                    TxtAiSeverity.Text = result.Severity;

                    // Kaynak hiyerarşisi gösterimi (Microsoft Learn vs Forumlar)
                    TxtAiSourceTier.Text = $"Kaynak: {result.SourceTier} ({result.SourceUrl})";

                    // Onaylı Çözüm Rozeti (Kullanıcı Talebi: Onay İşaretli Çözüm Rozeti)
                    if (result.IsVerified)
                    {
                        BadgeAiVerified.Visibility = Visibility.Visible;
                        TxtAiVerifiedBadge.Text = result.VerifiedBadge; // "✅ ONAYLI RESMİ ÇÖZÜM" or "✅ ONAYLI TOPLULUK ÇÖZÜMÜ"
                    }
                    else
                    {
                        BadgeAiVerified.Visibility = Visibility.Collapsed;
                    }

                    // Severity badge styling
                    if (result.Severity.ToLower().Contains("kritik"))
                        BadgeAiSeverity.Background = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                    else if (result.Severity.ToLower().Contains("yüksek"))
                        BadgeAiSeverity.Background = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
                    else
                        BadgeAiSeverity.Background = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));

                    TxtAiRootCause.Text = result.RootCause;

                    if (result.HasWebInsights)
                    {
                        PanelAiWebInsights.Visibility = Visibility.Visible;
                        TxtAiWebInsights.Text = result.WebInsights;
                    }
                    else
                    {
                        PanelAiWebInsights.Visibility = Visibility.Collapsed;
                    }

                    if (result.Steps != null && result.Steps.Count > 0)
                    {
                        var sb = new StringBuilder();
                        for (int i = 0; i < result.Steps.Count; i++)
                        {
                            sb.AppendLine($"{i + 1}. {result.Steps[i]}");
                        }
                        TxtAiSteps.Text = sb.ToString().TrimEnd();
                    }
                    else
                    {
                        TxtAiSteps.Text = "Belirlenen otomatik onarım scriptini çalıştırarak çözümü test edin.";
                    }

                    TxtAiAutoFixScript.Text = result.AutoFixScript;

                    Log($"✅ Çözüm hazır: {result.Title} [{result.Severity}] ({result.SourceTier})");
                }
            }
            catch (Exception ex)
            {
                Log($"Yapay Zekâ analiz hatası: {ex.Message}");
                MessageBox.Show($"Analiz sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnAiAnalyze.IsEnabled = true;
                BtnAiAnalyze.Content = "⚡ Yapay Zekâ & Web Analizi";
            }
        }

        private async void BtnRunAiScript_Click(object sender, RoutedEventArgs e)
        {
            string script = TxtAiAutoFixScript.Text.Trim();
            if (string.IsNullOrWhiteSpace(script))
            {
                MessageBox.Show("Çalıştırılacak bir onarım scripti bulunmuyor.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show($"Bu PowerShell onarım komutu yönetici yetkileriyle çalıştırılacaktır. Devam etmek istiyor musunuz?\n\nKomut:\n{script}", "Onarım Onayı", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            Log("⚡ Yapay Zekâ otomatik onarım komutu yürütülüyor...");
            var res = await NativeSystemEngine.RunPowerShellAsync(script);
            Log(res.Output);

            ShowModal("Yapay Zekâ Onarım Sonucu", res.Output);
        }

        private void BtnCopyAiScript_Click(object sender, RoutedEventArgs e)
        {
            string script = TxtAiAutoFixScript.Text.Trim();
            if (!string.IsNullOrWhiteSpace(script))
            {
                Clipboard.SetText(script);
                Log("Çözüm komutu panoya kopyalandı.");
            }
        }

        private void BtnSaveAiScript_Click(object sender, RoutedEventArgs e)
        {
            string script = TxtAiAutoFixScript.Text.Trim();
            if (string.IsNullOrWhiteSpace(script)) return;

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Onarım Scriptini Kaydet",
                Filter = "PowerShell Script (*.ps1)|*.ps1|Batch Dosyası (*.bat)|*.bat",
                FileName = "WinoxIT_FixScript.ps1"
            };

            if (sfd.ShowDialog() == true)
            {
                File.WriteAllText(sfd.FileName, script, Encoding.UTF8);
                Log($"Script kaydedildi: {sfd.FileName}");
            }
        }

        private async void BtnAiEventScan_Click(object sender, RoutedEventArgs e)
        {
            Log("Windows Olay Günlüğü'ndeki son 24 saatlik kritik sistem hataları taranıyor...");
            var res = await NativeSystemEngine.RunPowerShellAsync("Get-EventLog -LogName System -EntryType Error -Newest 3 -ErrorAction SilentlyContinue | Select-Object TimeGenerated, Source, Message | Format-List | Out-String");
            Log("Kritik Sistem Hataları:\n" + res.Output);
            ShowModal("Olay Günlüğü Kritik Hatalar", res.Output);
        }

        private async void BtnAiBsodScan_Click(object sender, RoutedEventArgs e)
        {
            Log("C:\\Windows\\Minidump klasöründeki mavi ekran (BSOD) döküm dosyaları taranıyor...");
            var res = await NativeSystemEngine.RunPowerShellAsync("Get-ChildItem C:\\Windows\\Minidump -ErrorAction SilentlyContinue | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize | Out-String");
            var outText = string.IsNullOrWhiteSpace(res.Output) ? "Sistemde yakın zamanda oluşmuş bir mavi ekran (BSOD) dump dosyası bulunamadı. Sistem kararlı." : res.Output;
            Log(outText);
            ShowModal("BSOD Minidump Taraması", outText);
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            TxtConsoleLog.Clear();
        }
    }

    public class ProcessItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public double MemoryMB { get; set; }
        public string Responding { get; set; } = "Evet";
    }

    public class StartupItem
    {
        public string Name { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
    }
}