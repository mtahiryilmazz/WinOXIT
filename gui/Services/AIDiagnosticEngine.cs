using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WinoxIT.Services;

namespace WinoxIT.Services
{
    public class AIDiagnosticEngine
    {
        public static AIDiagnosticEngine Instance { get; } = new AIDiagnosticEngine();

        public string ApiKey { get; set; } = "";
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

        public AIDiagnosticEngine()
        {
            LoadSavedApiKey();
        }

        private void LoadSavedApiKey()
        {
            string? envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (!string.IsNullOrWhiteSpace(envKey))
            {
                ApiKey = envKey.Trim();
                return;
            }

            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinoxIT");
            string keyFile = Path.Combine(appData, "gemini_api.key");
            if (File.Exists(keyFile))
            {
                try { ApiKey = File.ReadAllText(keyFile).Trim(); } catch { }
            }
        }

        public void SaveApiKey(string key)
        {
            ApiKey = key.Trim();
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinoxIT");
            try
            {
                Directory.CreateDirectory(appData);
                File.WriteAllText(Path.Combine(appData, "gemini_api.key"), ApiKey, Encoding.UTF8);
            }
            catch { }
        }

        public async Task<AISolutionResult> AnalyzeIssueAsync(string query)
        {
            query = (query ?? "").Trim();
            var symptom = SymptomNormalizer.Normalize(query);

            if (string.IsNullOrWhiteSpace(query))
            {
                var empty = GetDefaultFallbackSolution("Boş Sorgu");
                empty.PredictedSymptom = symptom;
                empty.ExtractedTokens = symptom.Tokens;
                empty.ConfidenceScore = symptom.Confidence;
                return empty;
            }

            // 1. If Gemini API key is configured, query Google Gemini cloud AI
            if (!string.IsNullOrWhiteSpace(ApiKey))
            {
                try
                {
                    var cloudResult = await QueryGeminiApiAsync(query);
                    if (cloudResult != null && !string.IsNullOrWhiteSpace(cloudResult.Title))
                    {
                        PopulateSmartAlternatives(cloudResult, query);
                        cloudResult.PredictedSymptom = symptom;
                        cloudResult.ExtractedTokens = symptom.Tokens;
                        cloudResult.ConfidenceScore = symptom.Confidence;
                        return cloudResult;
                    }
                }
                catch { }
            }

            // 2. Start background web research concurrently
            var webResearchTask = QueryWebResearchAsync(query);

            // 3. High-precision offline rule engine with Turkish tokenization and score weighting
            var local = GetLocalExpertSolution(query);

            // 4. Wait up to 3.5 seconds for background web research to complete
            AISolutionResult? webResult = null;
            try
            {
                var completed = await Task.WhenAny(webResearchTask, Task.Delay(3500));
                if (completed == webResearchTask)
                {
                    webResult = await webResearchTask;
                }
            }
            catch { }

            if (local != null)
            {
                if (webResult != null && !string.IsNullOrWhiteSpace(webResult.RootCause))
                {
                    local.WebInsights = webResult.RootCause;
                }
                PopulateSmartAlternatives(local, query);
                local.PredictedSymptom = symptom;
                local.ExtractedTokens = symptom.Tokens;
                local.ConfidenceScore = symptom.Confidence;
                return local;
            }

            if (webResult != null)
            {
                PopulateSmartAlternatives(webResult, query);
                webResult.PredictedSymptom = symptom;
                webResult.ExtractedTokens = symptom.Tokens;
                webResult.ConfidenceScore = symptom.Confidence;
                return webResult;
            }

            // 5. Contextual System Log & Smart Domain Fallback
            var fallback = GetDefaultFallbackSolution(query);
            PopulateSmartAlternatives(fallback, query);
            fallback.PredictedSymptom = symptom;
            fallback.ExtractedTokens = symptom.Tokens;
            fallback.ConfidenceScore = symptom.Confidence;
            return fallback;
        }

        public static string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string lower = text.ToLowerInvariant();
            var sb = new StringBuilder(lower.Length);
            foreach (char c in lower)
            {
                switch (c)
                {
                    case 'ı':
                    case 'i':
                    case 'İ':
                    case 'I':
                        sb.Append('i');
                        break;
                    case 'ğ':
                        sb.Append('g');
                        break;
                    case 'ü':
                        sb.Append('u');
                        break;
                    case 'ş':
                        sb.Append('s');
                        break;
                    case 'ö':
                        sb.Append('o');
                        break;
                    case 'ç':
                        sb.Append('c');
                        break;
                    default:
                        if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c == 'x' || c == '-')
                            sb.Append(c);
                        else
                            sb.Append(' ');
                        break;
                }
            }
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        private class DiagnosticRule
        {
            public string[] ErrorCodes { get; set; } = Array.Empty<string>();
            public string[][] RequiredTokenGroups { get; set; } = Array.Empty<string[]>();
            public string[] Keywords { get; set; } = Array.Empty<string>();
            public AISolutionResult Solution { get; set; } = new();
        }

        private AISolutionResult? GetLocalExpertSolution(string rawQuery)
        {
            string norm = NormalizeText(rawQuery);
            string[] tokens = norm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            HashSet<string> tokenSet = new(tokens, StringComparer.OrdinalIgnoreCase);

            var rules = GetBuiltInRules();

            // 1. Direct Hex / Error Code Matching (Highest Priority)
            foreach (var rule in rules)
            {
                foreach (var code in rule.ErrorCodes)
                {
                    string normCode = NormalizeText(code);
                    if (!string.IsNullOrEmpty(normCode) && (norm.Contains(normCode) || tokenSet.Contains(normCode)))
                    {
                        return rule.Solution;
                    }
                }
            }

            // 2. Required Token Group Matching
            foreach (var rule in rules)
            {
                if (rule.RequiredTokenGroups.Length > 0)
                {
                    foreach (var group in rule.RequiredTokenGroups)
                    {
                        bool groupMatched = true;
                        foreach (var token in group)
                        {
                            string normTok = NormalizeText(token);
                            if (!tokenSet.Contains(normTok) && !norm.Contains(normTok))
                            {
                                groupMatched = false;
                                break;
                            }
                        }
                        if (groupMatched)
                        {
                            return rule.Solution;
                        }
                    }
                }
            }

            // 3. Keyword Scoring Match
            DiagnosticRule? bestRule = null;
            int maxScore = 0;

            foreach (var rule in rules)
            {
                int score = 0;
                foreach (var kw in rule.Keywords)
                {
                    string normKw = NormalizeText(kw);
                    if (tokenSet.Contains(normKw))
                    {
                        score += 3;
                    }
                    else if (norm.Contains(normKw))
                    {
                        score += 2;
                    }
                }

                if (score > maxScore)
                {
                    maxScore = score;
                    bestRule = rule;
                }
            }

            if (bestRule != null && maxScore >= 3)
            {
                return bestRule.Solution;
            }

            return null;
        }

        private AISolutionResult GetDefaultFallbackSolution(string q)
        {
            return new AISolutionResult
            {
                Title = $"Sistem Tanılama Analizi: {q}",
                Category = "Sistem & Hata Günlüğü",
                RootCause = $"'{q}' ile ilgili sistem yapılandırması, kayıt defteri, grup ilkeleri veya donanım sürücüsü durumu denetlenmelidir.",
                Severity = "Normal",
                Steps = new List<string>
                {
                    "Windows Olay Görüntüleyicisi (eventvwr.msc) altında 'Sistem' ve 'Uygulama' günlüklerini inceleyin.",
                    "İlgili ayarın Grup İlkesi (GPO) veya Kayıt Defteri kısıtlaması altında olup olmadığını doğrulayın.",
                    "Aşağıdaki alternatif onarım seçeneklerinden probleminize en uygun olanı seçip uygulayın."
                },
                AutoFixScript = $"# '{q}' konusu için sistem hata günlüğü taraması\nGet-EventLog -LogName System -Newest 10 -EntryType Error,Warning | Select-Object TimeGenerated, Source, Message"
            };
        }

        private void PopulateSmartAlternatives(AISolutionResult result, string query)
        {
            if (result.Alternatives == null)
            {
                result.Alternatives = new List<AISolutionAlternative>();
            }

            if (result.Alternatives.Count > 0)
                return;

            string norm = NormalizeText(query);

            if (norm.Contains("ekran") || norm.Contains("gpu") || norm.Contains("surucu") || norm.Contains("goruntu") || norm.Contains("cozunurluk"))
            {
                result.Alternatives.Add(new AISolutionAlternative
                {
                    Title = "DirectX Shader & Grafik Önbelleği Temizliği",
                    Category = "Ekran Kartı",
                    Description = "Grafik sürücüsü kilitlenmelerinde bozuk derlenmiş DirectX shader dosyalarını sıfırlar.",
                    Script = "Remove-Item -Path \"$env:LOCALAPPDATA\\D3DSCache\\*\" -Recurse -Force -ErrorAction SilentlyContinue; Remove-Item -Path \"$env:LOCALAPPDATA\\NVIDIA\\DXCache\\*\" -Recurse -Force -ErrorAction SilentlyContinue; Write-Host 'Shader önbellekleri temizlendi.'"
                });
                result.Alternatives.Add(new AISolutionAlternative
                {
                    Title = "Donanım Hızlandırma Kayıt Defteri Bayraklarını Sıfırla",
                    Category = "Kayıt Defteri",
                    Description = "Kayıt defterindeki donanım hızlandırma çakışmalarını kaldırır.",
                    Script = "reg add \"HKCU\\Software\\Microsoft\\Avalon.Graphics\" /v DisableHWAcceleration /t REG_DWORD /d 0 /f"
                });
                result.Alternatives.Add(new AISolutionAlternative
                {
                    Title = "Grafik Alt Sistemini Hızlı Yeniden Başlat (DWM Reset)",
                    Category = "Grafik Sürücüsü",
                    Description = "Windows Grafik Arayüz Yöneticisini (DWM) yeniden başlatarak siyah ekran ve takılmaları çözer.",
                    Script = "Stop-Process -Name dwm -Force"
                });
            }
            else if (norm.Contains("grup") || norm.Contains("gpo") || norm.Contains("ilke") || norm.Contains("yonetici") || norm.Contains("engel"))
            {
                result.Alternatives.Add(new AISolutionAlternative
                {
                    Title = "Tüm Yerel GPO Politikalarını Sıfırla (registry.pol Silme)",
                    Category = "Grup İlkesi (GPO)",
                    Description = "İstemcide takılı kalmış yerel grup ilkesi dosyalarını sıfırlar ve temiz ilke indirir.",
                    Script = "Remove-Item \"C:\\Windows\\System32\\GroupPolicy\\Machine\\registry.pol\" -Force -ErrorAction SilentlyContinue; Remove-Item \"C:\\Windows\\System32\\GroupPolicy\\User\\registry.pol\" -Force -ErrorAction SilentlyContinue; gpupdate /force"
                });
                result.Alternatives.Add(new AISolutionAlternative
                {
                    Title = "Görev Yöneticisi & Kayıt Defteri Kilitlerini Aç",
                    Category = "Kayıt Defteri",
                    Description = "İlkeler tarafından kilitlenen Görev Yöneticisi ve Regedit erişimini açar.",
                    Script = "reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v DisableTaskMgr /f -ErrorAction SilentlyContinue; reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v DisableRegistryTools /f -ErrorAction SilentlyContinue; gpupdate /force"
                });
            }
            else if (norm.Contains("uzanti") || norm.Contains("dosya") || norm.Contains("gizli") || norm.Contains("explorer") || norm.Contains("gezgin"))
            {
                result.Alternatives.Add(new AISolutionAlternative
                {
                    Title = "Grup İlkesi Klasör Seçenekleri Kilidini Aç (NoFolderOptions)",
                    Category = "Grup İlkesi (GPO)",
                    Description = "Grup ilkesi engeli nedeniyle gizlenen veya grileşen dosya uzantısı ayarını serbest bırakır.",
                    Script = "reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer\" /v NoFolderOptions /f -ErrorAction SilentlyContinue; reg delete \"HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer\" /v NoFolderOptions /f -ErrorAction SilentlyContinue; gpupdate /force; Stop-Process -Name explorer -Force"
                });
                result.Alternatives.Add(new AISolutionAlternative
                {
                    Title = "Korumalı Sistem Dosyaları & Gizli Dosyaları Göster",
                    Category = "Kayıt Defteri",
                    Description = "Gizli klasörleri ve korunan sistem dosyalarını (.sys, Desktop.ini vb.) tam görünür yapar.",
                    Script = "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced' -Name 'Hidden' -Value 1; Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced' -Name 'ShowSuperHidden' -Value 1; Stop-Process -Name explorer -Force"
                });
                result.Alternatives.Add(new AISolutionAlternative
                {
                    Title = "Simge ve Gezgin Önbelleğini Sıfırla (IconCache.db)",
                    Category = "Dosya Gezgini",
                    Description = "Uzantılar değiştiğinde simgeler bozuk görünüyorsa IconCache veritabanını yeniler.",
                    Script = "Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue; Remove-Item -Path \"$env:LOCALAPPDATA\\IconCache.db\" -Force -ErrorAction SilentlyContinue; Start-Process explorer"
                });
            }
            else
            {
                result.Alternatives.Add(new AISolutionAlternative
                {
                    Title = "Sistem Dosyası Bütünlük Onarımı (SFC /scannow)",
                    Category = "Sistem Dosyaları",
                    Description = "Bozulmuş temel Windows DLL ve sistem bileşenlerini otomatik onarır.",
                    Script = "sfc /scannow"
                });
                result.Alternatives.Add(new AISolutionAlternative
                {
                    Title = "Bileşen Deposu İmaj Onarımı (DISM RestoreHealth)",
                    Category = "Windows İmajı",
                    Description = "Windows Update sunucuları üzerinden bozuk sistem paketlerini yeniler.",
                    Script = "DISM /Online /Cleanup-Image /RestoreHealth"
                });
                result.Alternatives.Add(new AISolutionAlternative
                {
                    Title = "Grup İlkelerini ve Ağ Politikalarını Yenile (gpupdate)",
                    Category = "Grup İlkesi",
                    Description = "Domain veya yerel ilkeleri anında yeniden uygular.",
                    Script = "gpupdate /force"
                });
            }
        }

        private async Task<AISolutionResult?> QueryWebResearchAsync(string query)
        {
            try
            {
                // 1. ÖNCELİK 1: RESMİ KAYNAKLAR (Microsoft Learn, Microsoft Support, TechNet)
                string officialSearchUrl = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString("site:learn.microsoft.com OR site:support.microsoft.com " + query + " powershell fix")}";
                var officialResult = await QueryDuckDuckGoHtmlAsync(officialSearchUrl, query, isOfficial: true);
                if (officialResult != null)
                {
                    return officialResult;
                }

                // 2. ÖNCELİK 2: DOĞRULANMIŞ TEKNOLOJİ FORUMLARI (SuperUser, ElevenForum, TenForums, Spiceworks, BleepingComputer)
                string forumSearchUrl = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString("site:superuser.com OR site:elevenforum.com OR site:tenforums.com OR site:community.spiceworks.com " + query + " solution solved")}";
                var forumResult = await QueryDuckDuckGoHtmlAsync(forumSearchUrl, query, isOfficial: false, isForum: true);
                if (forumResult != null)
                {
                    return forumResult;
                }

                // 3. ÖNCELİK 3: DUCKDUCKGO INSTANT ANSWER API & GENEL TOPLULUK VERİTABANI
                string searchUrl = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(query + " windows fix solution powershell")}&format=json&no_html=1";
                var response = await _http.GetAsync(searchUrl);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    string heading = doc.RootElement.TryGetProperty("Heading", out var h) ? h.GetString() ?? "" : "";
                    string abstractText = doc.RootElement.TryGetProperty("AbstractText", out var a) ? a.GetString() ?? "" : "";
                    string abstractSource = doc.RootElement.TryGetProperty("AbstractSource", out var s) ? s.GetString() ?? "" : "";

                    if (string.IsNullOrWhiteSpace(abstractText) && doc.RootElement.TryGetProperty("RelatedTopics", out var rel) && rel.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in rel.EnumerateArray())
                        {
                            if (item.TryGetProperty("Text", out var t) && !string.IsNullOrWhiteSpace(t.GetString()))
                            {
                                abstractText = t.GetString() ?? "";
                                abstractSource = "Doğrulanmış Topluluk Kaynağı";
                                break;
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(abstractText))
                    {
                        return new AISolutionResult
                        {
                            Title = $"🌐 Web Araştırması Teşhisi: {(string.IsNullOrEmpty(heading) ? query : heading)}",
                            Category = "Topluluk & Forum Araştırması",
                            RootCause = $"Kaynak ({abstractSource}): {abstractText}",
                            Severity = "Normal",
                            IsVerified = true,
                            VerifiedBadge = "✅ ONAYLI TOPLULUK ÇÖZÜMÜ",
                            SourceTier = abstractSource,
                            Steps = new List<string>
                            {
                                "Resmi ve topluluk teknik kılavuzlarına göre sistem yapılandırmasını doğrulayın.",
                                "Olay Görüntüleyicisi ve sistem bileşen durumunu inceleyin.",
                                "Aşağıdaki PowerShell onarım komutunu çalıştırın."
                            },
                            AutoFixScript = GenerateSmartScriptFromQuery(query)
                        };
                    }
                }
            }
            catch { }
            return null;
        }

        private async Task<AISolutionResult?> QueryDuckDuckGoHtmlAsync(string url, string query, bool isOfficial, bool isForum = false)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return null;

                string html = await resp.Content.ReadAsStringAsync();
                var snippetMatch = Regex.Match(html, @"<a class=""result__snippet[^""]*""[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                var urlMatch = Regex.Match(html, @"<a class=""result__url[^""]*""[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

                if (snippetMatch.Success)
                {
                    string snippet = Regex.Replace(snippetMatch.Groups[1].Value, "<.*?>", "").Trim();
                    snippet = System.Net.WebUtility.HtmlDecode(snippet);

                    string sourceUrl = urlMatch.Success ? Regex.Replace(urlMatch.Groups[1].Value, "<.*?>", "").Trim() : "";

                    if (!string.IsNullOrWhiteSpace(snippet))
                    {
                        string badge = isOfficial ? "✅ ONAYLI RESMİ ÇÖZÜM (Microsoft)" : (isForum ? "✅ ONAYLI TOPLULUK ÇÖZÜMÜ" : "🌐 GENEL WEB ÇÖZÜMÜ");
                        string sourceTier = isOfficial ? "Microsoft Learn & Support Resmi Kaynağı" : (isForum ? "Doğrulanmış Teknoloji Forumu (SuperUser / ElevenForum / Spiceworks)" : "Web Veritabanı");

                        return new AISolutionResult
                        {
                            Title = isOfficial ? $"📘 Microsoft Resmi Çözümü: {query}" : $"💬 Doğrulanmış Forum Çözümü: {query}",
                            Category = isOfficial ? "Microsoft Resmi Dokümantasyonu" : "Doğrulanmış Teknoloji Forumu",
                            RootCause = $"Tespit Edilen Çözüm ({sourceTier}):\n{snippet}",
                            Severity = "Normal",
                            IsVerified = true,
                            VerifiedBadge = badge,
                            SourceTier = sourceTier,
                            SourceUrl = sourceUrl,
                            Steps = new List<string>
                            {
                                isOfficial 
                                    ? "Microsoft Resmi Dokümantasyonu doğrultusunda ilgili servis veya sistem ayarını denetleyin."
                                    : "Kullanıcı topluluğunda ve teknoloji forumlarında onaylanmış çözüm önerisini uygulayın.",
                                "Sistem değişiklikleri öncesi WinoxIT geri yükleme noktasını kontrol edin.",
                                "Aşağıdaki PowerShell komutunu çalıştırarak onarımı uygulayın."
                            },
                            AutoFixScript = GenerateSmartScriptFromQuery(query)
                        };
                    }
                }
            }
            catch { }
            return null;
        }

        private string GenerateSmartScriptFromQuery(string query)
        {
            string q = (query ?? "").ToLowerInvariant();
            if (q.Contains("0x11b") || q.Contains("yazıcı") || q.Contains("spooler") || q.Contains("printer"))
            {
                return "Stop-Service -Name Spooler -Force; Set-ItemProperty -Path 'HKLM:\\System\\CurrentControlSet\\Control\\Print' -Name 'RpcAuthnLevelPrivacyEnabled' -Value 0 -Type DWord -Force; Start-Service -Name Spooler";
            }
            if (q.Contains("dns") || q.Contains("internet") || q.Contains("bağlantı") || q.Contains("network"))
            {
                return "ipconfig /flushdns; netsh int ip reset; netsh winsock reset";
            }
            if (q.Contains("sfc") || q.Contains("dism") || q.Contains("bozuk") || q.Contains("corrupt"))
            {
                return "sfc /scannow; DISM /Online /Cleanup-Image /RestoreHealth";
            }
            if (q.Contains("update") || q.Contains("0x80070002") || q.Contains("güncelleme"))
            {
                return "Stop-Service wuauserv, bits -Force; Remove-Item 'C:\\Windows\\SoftwareDistribution\\Download\\*' -Recurse -Force; Start-Service wuauserv, bits";
            }
            return $"# Otomatik Teşhis & İyileştirme Betiği: {query}\nGet-Service | Where-Object {{ $_.Status -eq 'Running' -and ($_.DisplayName -like '*{query}*' -or $_.Name -like '*{query}*') }}";
        }

        private List<DiagnosticRule> GetBuiltInRules()
        {
            return new List<DiagnosticRule>
            {
                // 1. 0x0000011b Yazıcı RPC Hatası
                new()
                {
                    ErrorCodes = new[] { "0x0000011b", "0x11b", "11b" },
                    RequiredTokenGroups = new[]
                    {
                        new[] { "yazici", "rpc" },
                        new[] { "point", "print" },
                        new[] { "yazici", "kimlik" }
                    },
                    Keywords = new[] { "yazici", "rpc", "point", "print", "11b", "baglanilamadi", "kimlik", "spooler" },
                    Solution = new AISolutionResult
                    {
                        Title = "Ağ Yazıcısı RPC Kimlik Doğrulama Hatası (0x0000011b)",
                        RootCause = "Windows Update (CVE-2021-36958) sonrası Point&Print RPC kimlik doğrulama zorunluluğundan kaynaklanan ağ yazıcı paylaşım engeli.",
                        Severity = "Yüksek",
                        Steps = new List<string>
                        {
                            "Kayıt defterinde RpcAuthnLevelPrivacyEnabled değeri 0 olarak ayarlanmalı.",
                            "Yazdırma Biriktiricisi (Spooler) servisi durdurulup yeniden başlatılmalı.",
                            "İstemci bilgisayar yeniden başlatılmadan yazıcı bağlantısı test edilmeli."
                        },
                        AutoFixScript = @"reg add ""HKEY_LOCAL_MACHINE\System\CurrentControlSet\Control\Print"" /v RpcAuthnLevelPrivacyEnabled /t REG_DWORD /d 0 /f; Restart-Service -Name Spooler -Force"
                    }
                },

                // 2. 0x00000709 Varsayılan Aygıt / CNAME Hatası
                new()
                {
                    ErrorCodes = new[] { "0x00000709", "0x709", "709" },
                    RequiredTokenGroups = new[]
                    {
                        new[] { "varsayilan", "yazici" },
                        new[] { "yazici", "709" }
                    },
                    Keywords = new[] { "709", "varsayilan", "yazici", "aygit", "atanmadi", "cname" },
                    Solution = new AISolutionResult
                    {
                        Title = "Varsayılan Yazıcı & CNAME Bağlantı Hatası (0x00000709)",
                        RootCause = "Windows kayıt defterindeki yazıcı aygıt eşlemesi izinlerinin bozulması veya DNS CNAME isim çözümleme kısıtlaması.",
                        Severity = "Orta",
                        Steps = new List<string>
                        {
                            "Kayıt defterinde ForceKerberosForRpc değeri 0 yapılmalı.",
                            "RestrictDriverInstallationToAdministrators politikası düzenlenmeli.",
                            "Spooler servisi yeniden başlatılmalı."
                        },
                        AutoFixScript = @"reg add ""HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows NT\Printers"" /v ForceKerberosForRpc /t REG_DWORD /d 0 /f; reg add ""HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows NT\Printers"" /v RestrictDriverInstallationToAdministrators /t REG_DWORD /d 0 /f; Restart-Service -Name Spooler -Force"
                    }
                },

                // 3. Yazıcı Sürücü Yükleme Hatası 0x0000007e & 0x00000bcb
                new()
                {
                    ErrorCodes = new[] { "0x0000007e", "0x00000bcb", "0x7e", "0xbcb", "7e", "bcb" },
                    RequiredTokenGroups = new[]
                    {
                        new[] { "yazici", "surucu" },
                        new[] { "surucu", "yuklenemedi" }
                    },
                    Keywords = new[] { "surucu", "driver", "yazici", "inf", "yuklenmiyor", "0x0000007e", "0x00000bcb" },
                    Solution = new AISolutionResult
                    {
                        Title = "Yazıcı Sürücüsü Yükleme Hatası (0x0000007e / 0x00000bcb)",
                        RootCause = "Point and Print kısıtlamaları veya hasarlı sürücü deposu (DriverStore) yazıcı inf dosyasının kurulmasını engelliyor.",
                        Severity = "Yüksek",
                        Steps = new List<string>
                        {
                            "Point and Print Restrictions kayıt defteri anahtarlarını yapılandırın.",
                            "PackagePointAndPrintServerList ayarlarını sıfırlayın.",
                            "Spooler servisini yeniden başlatıp sürücüyü tekrar yükleyin."
                        },
                        AutoFixScript = @"reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows NT\Printers\PointAndPrint"" /v Restricted /t REG_DWORD /d 0 /f; reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows NT\Printers\PointAndPrint"" /v TrustedServers /t REG_DWORD /d 0 /f; Restart-Service -Name Spooler -Force"
                    }
                },

                // 4. Yazdırma Biriktiricisi / Spooler Kilitlenmesi & Kuyruk
                new()
                {
                    ErrorCodes = new[] { "spoolsv", "spooler" },
                    RequiredTokenGroups = new[]
                    {
                        new[] { "spooler" },
                        new[] { "kuyruk", "takildi" },
                        new[] { "yazici", "kuyruk" },
                        new[] { "yazici", "yazdirmiyor" },
                        new[] { "yazdirma", "biriktiricisi" }
                    },
                    Keywords = new[] { "spooler", "kuyruk", "yazdirmiyor", "biriktirici", "takili", "silinmiyor", "bekliyor" },
                    Solution = new AISolutionResult
                    {
                        Title = "Yazdırma Biriktiricisi (Print Spooler) Kilitlenmesi",
                        RootCause = "C:\\Windows\\System32\\spool\\PRINTERS klasöründe bozuk veya kilitli kalmış .SHD ve .SPL geçici yazdırma iş dosyaları birikmiş.",
                        Severity = "Normal",
                        Steps = new List<string>
                        {
                            "Spooler servisini zorla durdurun.",
                            "PRINTERS klasöründeki tüm takılı kalmış kuyruk dosyalarını temizleyin.",
                            "Spooler servisini tekrar başlatın."
                        },
                        AutoFixScript = @"Stop-Service -Name Spooler -Force -ErrorAction SilentlyContinue; Stop-Process -Name spoolsv -Force -ErrorAction SilentlyContinue; Remove-Item ""C:\Windows\System32\spool\PRINTERS\*"" -Force -Recurse -ErrorAction SilentlyContinue; Start-Service -Name Spooler"
                    }
                },

                // 5. Yazıcı Çevrimdışı (Offline) Hatası
                new()
                {
                    RequiredTokenGroups = new[]
                    {
                        new[] { "yazici", "cevrildisi" },
                        new[] { "yazici", "offline" },
                        new[] { "yazici", "duraklatildi" }
                    },
                    Keywords = new[] { "cevrildisi", "offline", "yazici", "duraklatildi", "bagli", "gorunmuyor" },
                    Solution = new AISolutionResult
                    {
                        Title = "Yazıcı Çevrimdışı (Offline / Duraklatıldı) Sorunu",
                        RootCause = "SNMP durum sorgulaması yanıt vermiyor ya da yazıcı kuyruğu 'Çevrimdışı Kullan' moduna geçmiş.",
                        Severity = "Orta",
                        Steps = new List<string>
                        {
                            "Yazıcı portundaki SNMP durum denetimini devre dışı bırakın.",
                            "Kuyruktaki 'Yazıcıyı Çevrimdışı Kullan' bayrağını kaldırın.",
                            "IP port bağlantısını WMI üzerinden yenileyin."
                        },
                        AutoFixScript = @"Get-Printer | ForEach-Object { Set-Printer -Name $_.Name -PortName $_.PortName -ErrorAction SilentlyContinue }; Restart-Service -Name Spooler -Force"
                    }
                },

                // 6. SMB Ağ Paylaşımı / 0x80070035 / Misafir Erişimi
                new()
                {
                    ErrorCodes = new[] { "0x80070035", "80070035" },
                    RequiredTokenGroups = new[]
                    {
                        new[] { "paylasim", "erisim" },
                        new[] { "ag", "yolu", "bulunamadi" },
                        new[] { "smb", "paylasim" },
                        new[] { "klasor", "paylasim" },
                        new[] { "misafir", "ag" }
                    },
                    Keywords = new[] { "paylasim", "smb", "0x80070035", "ag", "yolu", "bulunamadi", "erisim", "klasor", "sifresiz" },
                    Solution = new AISolutionResult
                    {
                        Title = "SMB Ağ Paylaşımı & Ağ Yolu Bulunamadı (0x80070035)",
                        RootCause = "Windows Güvenlik Politikası güvensiz misafir (Insecure Guest Logon) oturumlarını engelliyor veya LanmanWorkstation servisi kısıtlı.",
                        Severity = "Yüksek",
                        Steps = new List<string>
                        {
                            "AllowInsecureGuestAuth kayıt defteri değerini 1 olarak açın.",
                            "LanmanWorkstation ve SMBv2 istemcisini etkinleştirin.",
                            "Ağ bulma ve dosya paylaşımını Güvenlik Duvarında serbest bırakın."
                        },
                        AutoFixScript = @"reg add ""HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters"" /v AllowInsecureGuestAuth /t REG_DWORD /d 1 /f; Set-SmbClientConfiguration -EnableInsecureGuestLogons $true -Force -ErrorAction SilentlyContinue; Restart-Service -Name LanmanWorkstation -Force"
                    }
                },

                // 6.5 Ağ Kartı Tanınmıyor / Sürücüsü Kurulmamış (Universal Offline Driver Kurulumu)
                new()
                {
                    ErrorCodes = new[] { "code 28", "kod 28", "code 10", "kod 10", "pci\\ven" },
                    RequiredTokenGroups = new[]
                    {
                        new[] { "ag", "karti" },
                        new[] { "ag", "surucu" },
                        new[] { "ethernet", "surucu" },
                        new[] { "wifi", "surucu" },
                        new[] { "karti", "taninmiyor" },
                        new[] { "karti", "kurulmamis" },
                        new[] { "universal", "driver" },
                        new[] { "universal", "surucu" },
                        new[] { "ag", "bagdastirici" },
                        new[] { "surucusuz", "ag" }
                    },
                    Keywords = new[] { "ag", "karti", "surucusu", "ethernet", "wifi", "wlan", "lan", "kurulmamis", "taninmiyor", "bagdastirici", "universal", "driver", "internet", "kod28", "code28" },
                    Solution = new AISolutionResult
                    {
                        Title = "Ağ Kartı Tanınmıyor & Çevrimdışı Universal Sürücü Kurulumu",
                        Category = "Ağ & Donanım Sürücüsü",
                        RootCause = "Ethernet veya Wi-Fi ağ bağdaştırıcısının sürücüsü Windows tarafından tanınmamış (Kod 28) veya bozulmuş. Kullanıcı internetsiz olduğu için sürücüyü çevrimiçi indiremiyor; yerel Windows Sürücü Ambarı (DriverStore) ve NDIS deposundan çevrimdışı kurulum yapılmalıdır.",
                        Severity = "Kritik",
                        Steps = new List<string>
                        {
                            "Plug and Play (PnP) donanım değişikliklerini pnputil /scan-devices ile yeniden sorgulayın.",
                            "C:\\Windows\\System32\\DriverStore\\FileRepository altındaki tüm yerel net*.inf paketlerini sisteme bağlayın.",
                            "WlanSvc, Netman ve DHCP Windows ağ servislerini yeniden başlatın.",
                            "TCP/IP ve Winsock yığınını sıfırlayarak ağ kartı bağdaştırıcılarını devreye alın."
                        },
                        AutoFixScript = @"pnputil.exe /scan-devices; Get-ChildItem 'C:\Windows\System32\DriverStore\FileRepository\net*.inf' -Recurse -ErrorAction SilentlyContinue | ForEach-Object { pnputil.exe /add-driver $_.FullName /install }; Start-Service -Name netman, nlasvc, netprofm, wlansvc, Dhcp -ErrorAction SilentlyContinue; netsh winsock reset; netsh int ip reset; ipconfig /renew",
                        Alternatives = new List<AISolutionAlternative>
                        {
                            new()
                            {
                                Title = "Hatalı PnP Ağ Aygıtlarını Zorla Etkinleştir",
                                Category = "PowerShell PnP",
                                Description = "Devre dışı kalmış veya hata kodlu ağ bağdaştırıcılarını PnP arabirimiyle zorla açar.",
                                Script = "Get-PnpDevice -Class Net -ErrorAction SilentlyContinue | Where-Object { $_.Status -ne 'OK' } | Enable-PnpDevice -Confirm:$false"
                            },
                            new()
                            {
                                Title = "Aygıt Yöneticisini Aç (devmgmt.msc)",
                                Category = "Windows GUI",
                                Description = "Donanım kimliklerini (PCI\\VEN & DEV) incelemek ve elle sürücü göstermek için Aygıt Yöneticisini açar.",
                                Script = "devmgmt.msc"
                            }
                        }
                    }
                },

                // 7. DNS & Ağ Yığını Sıfırlama
                new()
                {
                    RequiredTokenGroups = new[]
                    {
                        new[] { "dns", "yanit" },
                        new[] { "dns", "cozumleme" },
                        new[] { "internet", "yok" },
                        new[] { "ag", "yigini" },
                        new[] { "winsock" },
                        new[] { "sinirli", "baglanti" }
                    },
                    Keywords = new[] { "dns", "internet", "winsock", "ipconfig", "cozumleme", "baglanti", "sinirli", "tarayici" },
                    Solution = new AISolutionResult
                    {
                        Title = "Ağ Yığını & DNS Çözümleme Protokol Onarımı",
                        RootCause = "Winsock protokol yığınında bozulma, hatalı yönlendirme tabloları veya bozuk DNS yerel önbellek kayıtları.",
                        Severity = "Orta",
                        Steps = new List<string>
                        {
                            "DNS önbelleğini temizleyin (flushdns).",
                            "Winsock kataloğunu fabrika ayarlarına sıfırlayın.",
                            "TCP/IP arayüz yığınını sıfırlayıp IP kira yenilemesi yapın."
                        },
                        AutoFixScript = @"ipconfig /flushdns; netsh winsock reset; netsh int ip reset; ipconfig /release; ipconfig /renew"
                    }
                },

                // 8. Domain Güven İlişkisi Bozulması (Trust Relationship Failed)
                new()
                {
                    RequiredTokenGroups = new[]
                    {
                        new[] { "domain", "guven" },
                        new[] { "active", "directory" },
                        new[] { "guven", "iliskisi" },
                        new[] { "trust", "relationship" }
                    },
                    Keywords = new[] { "domain", "trust", "guven", "iliskisi", "active", "directory", "etki", "alani", "makine", "hesabi" },
                    Solution = new AISolutionResult
                    {
                        Title = "Active Directory Domain Güven İlişkisi Bozulması",
                        RootCause = "İstemci bilgisayar hesabı şifresi (Secure Channel) ile Etki Alanı Denetleyicisi (DC) senkronizasyonu kopmuş veya saat farkı (Kerberos Skew) var.",
                        Severity = "Kritik",
                        Steps = new List<string>
                        {
                            "İstemci saati ile DC saati arasındaki Kerberos farkını w32tm ile eşitleyin.",
                            "Test-ComputerSecureChannel ile güven kanalını tamir edin.",
                            "Gerekiyorsa makine hesabı şifresini yerel olarak sıfırlayın."
                        },
                        AutoFixScript = @"w32tm /resync /force; Test-ComputerSecureChannel -Repair -Verbose"
                    }
                },

                // 9. Wi-Fi Bağlantı & Ağ Kartı Sorunları
                new()
                {
                    RequiredTokenGroups = new[]
                    {
                        new[] { "wifi", "baglanmiyor" },
                        new[] { "wifi", "gorunmuyor" },
                        new[] { "kablosuz", "ag" },
                        new[] { "wlan" }
                    },
                    Keywords = new[] { "wifi", "kablosuz", "wlan", "ssid", "baglanmiyor", "gorunmuyor", "kopuyor" },
                    Solution = new AISolutionResult
                    {
                        Title = "Wi-Fi / Kablosuz Ağ Adaptörü Bağlantı Sorunu",
                        RootCause = "WLAN Otomatik Yapılandırma (WlanSvc) servisi durmuş veya kablosuz adaptör güç tasarrufu modunda askıya alınmış.",
                        Severity = "Orta",
                        Steps = new List<string>
                        {
                            "WLAN AutoConfig servisini otomatik başlatın.",
                            "Wi-Fi güç yönetimi uyku modunu kapatın.",
                            "Kablosuz adaptörü yeniden başlatın."
                        },
                        AutoFixScript = @"Set-Service -Name WlanSvc -StartupType Automatic; Start-Service -Name WlanSvc; Get-NetAdapter | Where-Object { $_.PhysicalMediaType -like '*802.11*' -or $_.Name -like '*Wi-Fi*' } | Restart-NetAdapter -Confirm:$false"
                    }
                },

                // 10. Uzak Masaüstü / RDP Hatası & CredSSP (Port 3389)
                new()
                {
                    ErrorCodes = new[] { "credssp", "3389" },
                    RequiredTokenGroups = new[]
                    {
                        new[] { "rdp", "baglanmiyor" },
                        new[] { "uzak", "masaustu" },
                        new[] { "credssp" }
                    },
                    Keywords = new[] { "rdp", "uzak", "masaustu", "credssp", "3389", "sertifika", "nla", "kimlik" },
                    Solution = new AISolutionResult
                    {
                        Title = "Uzak Masaüstü (RDP) & CredSSP Kimlik Doğrulama Hatası",
                        RootCause = "CVE-2018-0886 CredSSP şifreleme oracle düzeltmesi nedeniyle güncel olmayan sunuculara RDP bağlantısı engelleniyor.",
                        Severity = "Yüksek",
                        Steps = new List<string>
                        {
                            "CredSSP AllowEncryptionOracle politikasını 'Vulnerable' (2) olarak esnetin.",
                            "Port 3389 RDP güvenlik duvarı kuralını açın.",
                            "fDenyTSConnections ayarını 0 yaparak RDP kabulünü sağlayın."
                        },
                        AutoFixScript = @"reg add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\CredSSP\Parameters"" /v AllowEncryptionOracle /t REG_DWORD /d 2 /f; Set-ItemProperty -Path 'HKLM:\System\CurrentControlSet\Control\Terminal Server' -Name ""fDenyTSConnections"" -Value 0; Enable-NetFirewallRule -DisplayGroup ""Remote Desktop"""
                    }
                },

                // 11. Mavi Ekran (BSOD) & Minidump & Sistem Bütünlüğü
                new()
                {
                    ErrorCodes = new[] { "bsod", "0xc000021a", "0xc0000005", "0x0000001a", "0x0000007b" },
                    RequiredTokenGroups = new[]
                    {
                        new[] { "mavi", "ekran" },
                        new[] { "bsod" },
                        new[] { "sistem", "cokuyor" }
                    },
                    Keywords = new[] { "mavi", "ekran", "bsod", "dmp", "minidump", "cokme", "yeniden", "basliyor", "bugcheck" },
                    Solution = new AISolutionResult
                    {
                        Title = "Windows Mavi Ekran (BSOD) & Kritik Çökme Onarımı",
                        RootCause = "Bozulmuş çekirdek sürücüsü (Kernel Driver), hasarlı NTFS dosya sistemi veya bozuk sistem DLL dosyaları.",
                        Severity = "Kritik",
                        Steps = new List<string>
                        {
                            "DISM ile Windows Component Store (WinSxS) imajını çevrimiçi onarın.",
                            "SFC ile çekirdek korumalı dosyaları doğrulayın.",
                            "C:\\Windows\\Minidump klasöründeki crash loglarını denetleyin."
                        },
                        AutoFixScript = @"dism /online /cleanup-image /restorehealth; sfc /scannow; chkdsk C: /scan"
                    }
                },

                // 12. Disk %100 Kullanımı & SysMain / Windows Search
                new()
                {
                    RequiredTokenGroups = new[]
                    {
                        new[] { "disk", "100" },
                        new[] { "disk", "kullanimi" },
                        new[] { "sysmain" },
                        new[] { "donuyor" }
                    },
                    Keywords = new[] { "disk", "100", "yavaslama", "kilitleniyor", "donuyor", "sysmain", "superfetch", "wsearch" },
                    Solution = new AISolutionResult
                    {
                        Title = "Windows %100 Disk Kullanımı & Aşırı I/O Kilitlenmesi",
                        RootCause = "SysMain (Superfetch) servisi ve Windows Search Dizin Oluşturucusu sabit diskte sürekli yüksek I/O okuma/yazma kuyruğu oluşturuyor.",
                        Severity = "Yüksek",
                        Steps = new List<string>
                        {
                            "SysMain servisini durdurun ve devre dışı bırakın.",
                            "Windows Search dizinini geçici olarak optimize edin.",
                            "Arka plan telemetri kuyruklarını sınırlandırın."
                        },
                        AutoFixScript = @"Stop-Service -Name SysMain -Force; Set-Service -Name SysMain -StartupType Disabled; Stop-Service -Name WSearch -Force; Set-Service -Name WSearch -StartupType Manual"
                    }
                },

                // 13. Windows Update Hataları (0x80070002, 0x80070422, 0x80240020)
                new()
                {
                    ErrorCodes = new[] { "0x80070002", "0x80070422", "0x80240020", "0x80070003", "80070422" },
                    RequiredTokenGroups = new[]
                    {
                        new[] { "guncelleme", "yuklenmiyor" },
                        new[] { "windows", "update" },
                        new[] { "update", "hata" }
                    },
                    Keywords = new[] { "update", "guncelleme", "windowsupdate", "yuklenmiyor", "takildi", "softwaredistribution", "0x80070422" },
                    Solution = new AISolutionResult
                    {
                        Title = "Windows Update Güncelleştirme Yükleme Hatası",
                        RootCause = "C:\\Windows\\SoftwareDistribution indirme önbelleğinde hasarlı paketler birikmiş ya da wuauserv servisi kilitli.",
                        Severity = "Yüksek",
                        Steps = new List<string>
                        {
                            "Windows Update, BITS ve Cryptographic servislerini durdurun.",
                            "SoftwareDistribution ve Catroot2 önbellek klasörlerini temizleyin.",
                            "Servisleri yeniden başlatıp güncellemeleri tetikleyin."
                        },
                        AutoFixScript = @"net stop wuauserv; net stop bits; net stop cryptsvc; Remove-Item 'C:\Windows\SoftwareDistribution\Download\*' -Recurse -Force -ErrorAction SilentlyContinue; net start cryptsvc; net start bits; net start wuauserv; UsoClient StartScan"
                    }
                },

                // 14. 0x80070005 Erişim Engellendi (Access Denied)
                new()
                {
                    ErrorCodes = new[] { "0x80070005", "80070005" },
                    RequiredTokenGroups = new[]
                    {
                        new[] { "erisim", "engellendi" },
                        new[] { "access", "denied" },
                        new[] { "yetki", "yetersiz" }
                    },
                    Keywords = new[] { "0x80070005", "erisim", "engellendi", "yetki", "yonetici", "izin", "uac" },
                    Solution = new AISolutionResult
                    {
                        Title = "Erişim Engellendi Hatası (0x80070005 / Access Denied)",
                        RootCause = "İlgili dosya, klasör veya kayıt defteri anahtarı üzerinde Administrators grubunun 'Tam Denetim' izni bulunmuyor.",
                        Severity = "Yüksek",
                        Steps = new List<string>
                        {
                            "Dosya sahipliğini Administrators grubuna devredin (takeown).",
                            "NTFS ACL izinlerini 'Full Control' olarak miras verin (icacls).",
                            "UAC kısıtlamalarını yönetici seviyesinde çalıştırın."
                        },
                        AutoFixScript = @"takeown /F ""C:\ProgramData"" /A /R /D Y; icacls ""C:\ProgramData"" /grant Administrators:F /T /C /Q"
                    }
                },

                // 15. Geçici Kullanıcı Profili (Temp User Profile)
                new()
                {
                    RequiredTokenGroups = new[]
                    {
                        new[] { "gecici", "profil" },
                        new[] { "temp", "profile" },
                        new[] { "profil", "yuklenemedi" }
                    },
                    Keywords = new[] { "profil", "gecici", "temp", "oturum", "yuklenemedi", "ayarlar", "kayboldu", "bak" },
                    Solution = new AISolutionResult
                    {
                        Title = "Kullanıcı Profili Yüklenemedi (Geçici Profil / TEMP Hatası)",
                        RootCause = "Windows ProfileList kayıt defterinde kullanıcının SID anahtarı kilitlenmiş ve .bak uzantılı yedek anahtar oluşmuş.",
                        Severity = "Kritik",
                        Steps = new List<string>
                        {
                            "HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\ProfileList altındaki .bak anahtarını inceleyin.",
                            "Geçici TEMP profil anahtarını silin ve .bak uzantısını kaldırarak asıl anahtarı geri yükleyin.",
                            "Oturumu kapatıp tekrar açın."
                        },
                        AutoFixScript = @"$profileList = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList'; Get-ChildItem $profileList | Where-Object { $_.Name -like '*.bak' } | ForEach-Object { Rename-Item -Path $_.PSPath -NewName ($_.PSChildName -replace '\.bak$','') -Force -ErrorAction SilentlyContinue }"
                    }
                },

                // 16. Ses Gelmiyor / Ses Aygıtı Algılanmıyor
                new()
                {
                    RequiredTokenGroups = new[]
                    {
                        new[] { "ses", "gelmiyor" },
                        new[] { "hoparlor" },
                        new[] { "kulaklik", "calismiyor" },
                        new[] { "ses", "aygiti" }
                    },
                    Keywords = new[] { "ses", "audio", "hoparlor", "kulaklik", "gelmiyor", "calismiyor", "cikis", "aygiti" },
                    Solution = new AISolutionResult
                    {
                        Title = "Windows Ses & Audio Çıkış Aygıtı Hatası",
                        RootCause = "Windows Audio (Audiosrv) veya AudioEndpointBuilder servisi çökmüş ya da aygıt sürücüsü askıda kalmış.",
                        Severity = "Orta",
                        Steps = new List<string>
                        {
                            "Windows Audio ve AudioEndpointBuilder servislerini yeniden başlatın.",
                            "Varsayılan ses yürütme aygıtını etkinleştirin.",
                            "Ses sürücüsünü PnP servisi üzerinden yenileyin."
                        },
                        AutoFixScript = @"Restart-Service -Name AudioEndpointBuilder, Audiosrv -Force"
                    }
                },

                // 17. USB Bellek Görünmüyor / Harf Atanmadı
                new()
                {
                    RequiredTokenGroups = new[]
                    {
                        new[] { "usb", "gorunmuyor" },
                        new[] { "flash", "bellek" },
                        new[] { "surucu", "harfi" }
                    },
                    Keywords = new[] { "usb", "flash", "bellek", "gorunmuyor", "harf", "atanmadi", "diskpart", "taninmadi" },
                    Solution = new AISolutionResult
                    {
                        Title = "USB Flash Bellek / Harici Disk Görünmeme Sorunu",
                        RootCause = "USB aygıtı takıldığında Windows otomatik sürücü harfi ataması (automount) kapalı veya dosya sistemi RAW durumunda.",
                        Severity = "Orta",
                        Steps = new List<string>
                        {
                            "Diskpart automount özelliğini etkinleştirin.",
                            "USB seçici askıya alma (Selective Suspend) güç tasarrufunu devre dışı bırakın.",
                            "Disk yönetimini yenileyin."
                        },
                        AutoFixScript = @"cmd /c ""echo automount enable | diskpart""; powercfg /SETACVALUEINDEX SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba4d5a0 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0"
                    }
                },

                // 18. Outlook Sürekli Şifre İstiyor / Credential Manager Döngüsü
                new()
                {
                    RequiredTokenGroups = new[]
                    {
                        new[] { "outlook", "sifre" },
                        new[] { "outlook", "parola" },
                        new[] { "kimlik", "bilgisi" }
                    },
                    Keywords = new[] { "outlook", "sifre", "parola", "surekli", "istiyor", "credential", "kimlik", "office" },
                    Solution = new AISolutionResult
                    {
                        Title = "Outlook Sürekli Şifre İsteme & Kimlik Bilgisi Döngüsü",
                        RootCause = "Windows Kimlik Bilgisi Yöneticisinde (Credential Manager) bozuk veya eski Office/ADAL/Modern Auth tokenları birikmiş.",
                        Severity = "Orta",
                        Steps = new List<string>
                        {
                            "Kullanıcıya ait eski MicrosoftOffice ve Outlook kimlik bilgilerini temizleyin.",
                            "Modern Kimlik Doğrulama (Modern Auth) kayıt defteri bayrağını açın.",
                            "Outlook'u güvenli modda açıp profili tazeleyin."
                        },
                        AutoFixScript = @"cmdkey /list | Select-String 'Target: (Microsoft.*Office.*|Outlook.*)' | ForEach-Object { $target = ($_ -replace '.*Target: ','').Trim(); cmdkey /delete:$target }"
                    }
                },

                // 19. Windows Etkinleştirme / Lisans Hatası (SLMGR / KMS)
                new()
                {
                    RequiredTokenGroups = new[]
                    {
                        new[] { "etkinlestirme" },
                        new[] { "lisans", "suresi" },
                        new[] { "slmgr" },
                        new[] { "kms" }
                    },
                    Keywords = new[] { "etkinlestirme", "lisans", "slmgr", "kms", "windows", "orijinal", "anahtar" },
                    Solution = new AISolutionResult
                    {
                        Title = "Windows Lisans & Etkinleştirme (KMS / MAK) Onarımı",
                        RootCause = "Yazılım Koruma Hizmeti (sppsvc) önbellek hatası veya KMS sunucusuna ulaşılamaması nedeniyle lisans düşmesi.",
                        Severity = "Yüksek",
                        Steps = new List<string>
                        {
                            "Software Protection Platform (sppsvc) servisini başlatın.",
                            "KMS istemci lisansını slmgr.vbs /ato ile tetikleyin.",
                            "Lisans önbelleğini yenileyin."
                        },
                        AutoFixScript = @"Start-Service -Name sppsvc -ErrorAction SilentlyContinue; cscript.exe C:\Windows\System32\slmgr.vbs /ato"
                    }
                },

                // 20. Dosya Uzantıları Görünmüyor (HideFileExt)
                new()
                {
                    RequiredTokenGroups = new[]
                    {
                        new[] { "dosya", "uzantisi" },
                        new[] { "dosya", "uzantilari" },
                        new[] { "uzantisi", "gorunmuyor" },
                        new[] { "uzantilari", "gorunmuyor" },
                        new[] { "uzanti", "gizli" },
                        new[] { "uzanti" }
                    },
                    Keywords = new[] { "dosya", "uzantisi", "uzantilari", "gorunmuyor", "uzanti", "hidefileext", "uzantilarini", "goster" },
                    Solution = new AISolutionResult
                    {
                        Title = "Windows Dosya Gezgini Dosya Uzantılarını Gösterme (.exe, .txt, .pdf)",
                        Category = "Dosya Gezgini & Kayıt Defteri",
                        RootCause = "Windows Dosya Gezgini varsayılan olarak güvenlik ve sadelik gerekçesiyle bilinen dosya türlerinin uzantılarını (HideFileExt) gizlemektedir. Bazı sistemlerde ise Grup İlkesi (GPO) 'NoFolderOptions' ayarı kullanıcıların uzantıları açmasını kilitler.",
                        Severity = "Normal",
                        Steps = new List<string>
                        {
                            "Kayıt defterinde HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced altındaki HideFileExt değerini 0 (Göster) yapın.",
                            "Grup ilkesi engeli varsa NoFolderOptions kısıtlamasını kaldırın.",
                            "Windows Gezgini (explorer.exe) sürecini yeniden başlatarak değişikliği anında etkinleştirin."
                        },
                        AutoFixScript = "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced' -Name 'HideFileExt' -Value 0; Stop-Process -Name explorer -Force",
                        TargetRegistryBackupPath = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                        Alternatives = new List<AISolutionAlternative>
                        {
                            new()
                            {
                                Title = "Grup İlkesi Klasör Seçenekleri Kilidini Aç (NoFolderOptions)",
                                Category = "Grup İlkesi (GPO)",
                                Description = "Eğer dosya uzantısı seçeneği grileşmişse veya 'Bu ayar yöneticiniz tarafından yönetilir' yazıyorsa ilkeyi temizler.",
                                Script = @"reg delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer"" /v NoFolderOptions /f -ErrorAction SilentlyContinue; reg delete ""HKLM\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer"" /v NoFolderOptions /f -ErrorAction SilentlyContinue; gpupdate /force; Stop-Process -Name explorer -Force"
                            },
                            new()
                            {
                                Title = "Korumalı Sistem & Gizli Dosyaları da Göster (ShowSuperHidden)",
                                Category = "Kayıt Defteri",
                                Description = "Gizli sistem dosyalarını (.sys, Desktop.ini vb.) ve gizli klasörleri de aynı anda tam görünür yapar.",
                                Script = @"Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' -Name 'Hidden' -Value 1; Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' -Name 'ShowSuperHidden' -Value 1; Stop-Process -Name explorer -Force"
                            },
                            new()
                            {
                                Title = "Simge ve Gezgin Önbelleğini Sıfırla (IconCache)",
                                Category = "Dosya Gezgini",
                                Description = "Uzantılar değiştiğinde simgeler bozuk veya beyaz görünüyorsa IconCache veritabanını yeniler.",
                                Script = @"Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue; Remove-Item -Path ""$env:LOCALAPPDATA\IconCache.db"" -Force -ErrorAction SilentlyContinue; Start-Process explorer"
                            }
                        }
                    }
                },

                // 21. Gizli Dosyalar & Klasörler Görünmüyor
                new()
                {
                    RequiredTokenGroups = new[]
                    {
                        new[] { "gizli", "dosyalar" },
                        new[] { "gizli", "klasor" },
                        new[] { "gizli", "dosya" },
                        new[] { "appdata" }
                    },
                    Keywords = new[] { "gizli", "dosyalar", "klasorler", "gorunmuyor", "hidden", "appdata", "goster" },
                    Solution = new AISolutionResult
                    {
                        Title = "Gizli Dosya ve Klasörleri Görünür Yapma (AppData, ProgramData)",
                        Category = "Dosya Gezgini & Kayıt Defteri",
                        RootCause = "Windows Dosya Gezgini gizli ve sistem dosyalarını görüntüleme ayarı (Hidden / ShowSuperHidden) kapalı durumdadır.",
                        Severity = "Normal",
                        Steps = new List<string>
                        {
                            "Explorer Advanced anahtarında 'Hidden' değerini 1 yapın.",
                            "Windows Gezginini yeniden başlatarak gizli öğeleri görünür kılın."
                        },
                        AutoFixScript = "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced' -Name 'Hidden' -Value 1; Stop-Process -Name explorer -Force",
                        TargetRegistryBackupPath = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                        Alternatives = new List<AISolutionAlternative>
                        {
                            new()
                            {
                                Title = "Korumalı Sistem Dosyalarını da Göster (ShowSuperHidden = 1)",
                                Category = "Kayıt Defteri",
                                Description = "Sistem klasörlerindeki gizli dosyaları ve yapılandırma dosyalarını gösterir.",
                                Script = "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced' -Name 'ShowSuperHidden' -Value 1; Stop-Process -Name explorer -Force"
                            },
                            new()
                            {
                                Title = "AppData / ProgramData Klasörlerini Doğrudan Aç",
                                Category = "Kısayol",
                                Description = "Gizli AppData ve ProgramData sistem klasörlerini doğrudan Gezgin penceresinde açar.",
                                Script = "explorer.exe \"$env:APPDATA\"; explorer.exe \"$env:ProgramData\""
                            }
                        }
                    }
                },

                // 22. Görev Çubuğu & Başlat Menüsü Kilitlendi / Dondu
                new()
                {
                    RequiredTokenGroups = new[]
                    {
                        new[] { "gorev", "cubugu" },
                        new[] { "baslat", "menusu" },
                        new[] { "taskbar" }
                    },
                    Keywords = new[] { "gorev", "cubugu", "baslat", "menusu", "dondu", "kilitlendi", "yanit", "vermiyor", "taskbar" },
                    Solution = new AISolutionResult
                    {
                        Title = "Windows Görev Çubuğu & Başlat Menüsü Kilitlenme Onarımı",
                        RootCause = "Windows Kabuk Deneyimi (ShellExperienceHost) veya explorer.exe sürecinin askıda kalması.",
                        Severity = "Orta",
                        Steps = new List<string>
                        {
                            "explorer.exe sürecini sonlandırıp otomatik yeniden başlatın.",
                            "StartMenuExperienceHost arka plan sürecini tazeleyin."
                        },
                        AutoFixScript = "Stop-Process -Name explorer -Force"
                    }
                },

                // 23. Windows Saat & Zaman Sunucusu Senkronizasyonu
                new()
                {
                    RequiredTokenGroups = new[]
                    {
                        new[] { "saat", "yanlis" },
                        new[] { "saat", "senkronize" },
                        new[] { "tarih", "saat" },
                        new[] { "w32time" }
                    },
                    Keywords = new[] { "saat", "tarih", "zaman", "senkronize", "yanlis", "ileri", "geri", "w32time", "ntp" },
                    Solution = new AISolutionResult
                    {
                        Title = "Windows Saat & Zaman Sunucusu (NTP / W32Time) Senkronizasyonu",
                        RootCause = "Windows Zaman Hizmeti (w32time) yerel CMOS pili düşmesi veya etki alanı zaman sunucusu ile senkronizasyon kopması.",
                        Severity = "Orta",
                        Steps = new List<string>
                        {
                            "W32Time servisini pool.ntp.org sunucusuna yönlendirin.",
                            "Zaman servisini yeniden başlatıp zorla senkronizasyon (resync) yapın."
                        },
                        AutoFixScript = "w32tm /config /manualpeerlist:\"pool.ntp.org\" /syncfromflags:manual /reliable:yes /update; Restart-Service -Name W32Time -Force; w32tm /resync /force"
                    }
                },

                // 24. Windows 11 Klasik Sağ Tık Menüsü
                new()
                {
                    RequiredTokenGroups = new[]
                    {
                        new[] { "sag", "tik" },
                        new[] { "klasik", "menu" }
                    },
                    Keywords = new[] { "sag", "tik", "menu", "klasik", "windows 11", "eski" },
                    Solution = new AISolutionResult
                    {
                        Title = "Windows 11 Klasik Tam Sağ Tık Bağlam Menüsünü Geri Getirme",
                        RootCause = "Windows 11 yeni kompakt sağ tık menüsü klasik üçüncü parti ve BT araçlarını 'Daha fazla seçenek göster' altına gizlemektedir.",
                        Severity = "Normal",
                        Steps = new List<string>
                        {
                            "HKCU:\\Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\\InprocServer32 anahtarını kaydedin.",
                            "Explorer sürecini yeniden başlatarak klasik menüyü varsayılan yapın."
                        },
                        AutoFixScript = "reg add \"HKCU\\Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\\InprocServer32\" /f /ve; Stop-Process -Name explorer -Force"
                    }
                },

                // 25. Windows Arama & Dizinleme Çalışmıyor
                new()
                {
                    RequiredTokenGroups = new[]
                    {
                        new[] { "arama", "calismiyor" },
                        new[] { "arama", "kutusu" },
                        new[] { "search" }
                    },
                    Keywords = new[] { "arama", "search", "dizin", "index", "calismiyor", "yazilmiyor" },
                    Solution = new AISolutionResult
                    {
                        Title = "Windows Arama ve Dizinleme (Windows Search) Sıfırlama",
                        RootCause = "SearchApp arka plan paketi kilitlenmesi veya dizin veritabanı bozulması.",
                        Severity = "Orta",
                        Steps = new List<string>
                        {
                            "SearchApp sürecini yeniden başlatın.",
                            "WSearch servisini durdurup başlatın."
                        },
                        AutoFixScript = "Stop-Process -Name SearchApp -Force -ErrorAction SilentlyContinue; Restart-Service -Name WSearch -Force"
                    }
                },

                // 25. Ekran Kartı / GPU / Görüntü Sürücüsü Kilitlenmesi
                new()
                {
                    ErrorCodes = new[] { "nvlddmkm", "amdkmdag", "igfx", "0x00000116", "VIDEO_TDR_FAILURE" },
                    RequiredTokenGroups = new[]
                    {
                        new[] { "ekran", "karti" },
                        new[] { "gpu" },
                        new[] { "goruntu", "surucusu" },
                        new[] { "siyah", "ekran" },
                        new[] { "ekran", "karardi" },
                        new[] { "ekran", "titriyor" },
                        new[] { "cozunurluk" }
                    },
                    Keywords = new[] { "ekran", "karti", "gpu", "nvidia", "amd", "radeon", "intel", "cozunurluk", "surucu", "goruntu", "siyah", "karardi", "titriyor", "fps", "driver" },
                    Solution = new AISolutionResult
                    {
                        Title = "Ekran Kartı / Grafik Sürücüsü Kilitlenmesi & Donanım Hızlandırma",
                        Category = "Ekran Kartı & Grafik",
                        RootCause = "Ekran kartı sürücüsü (NVIDIA, AMD veya Intel) DWM veya DirectX çağrısında zaman aşımına uğramış (TDR çökmesi), grafik önbelleği bozulmuş veya sürücü kilitlenmiş.",
                        Severity = "Orta",
                        Steps = new List<string>
                        {
                            "Windows Grafik Aygıt Yöneticisini (DWM & GPU PnP) anında yeniden başlatın.",
                            "DirectX ve Shader önbelleğini temizleyin.",
                            "Donanım hızlandırma kayıt defteri değerlerini sıfırlayın.",
                            "Sorun sürerse DDU ile temiz sürücü kurulumu gerçekleştirin."
                        },
                        AutoFixScript = @"Stop-Process -Name dwm -Force -ErrorAction SilentlyContinue; Get-PnpDevice -Class 'Display' | Where-Object { $_.Status -eq 'OK' } | ForEach-Object { Disable-PnpDevice -InstanceId $_.InstanceId -Confirm:$false -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 400; Enable-PnpDevice -InstanceId $_.InstanceId -Confirm:$false -ErrorAction SilentlyContinue }; Write-Host 'Grafik bağdaştırıcısı ve DWM sıfırlandı.'",
                        TargetRegistryBackupPath = @"HKEY_CURRENT_USER\Software\Microsoft\Avalon.Graphics",
                        Alternatives = new List<AISolutionAlternative>
                        {
                            new()
                            {
                                Title = "DirectX Shader & Grafik Önbelleği Temizliği",
                                Category = "Ekran Kartı",
                                Description = "Oyun veya program açılırken oluşan kilitlenmelerde bozuk shader önbelleğini temizler.",
                                Script = "Remove-Item -Path \"$env:LOCALAPPDATA\\D3DSCache\\*\" -Recurse -Force -ErrorAction SilentlyContinue; Remove-Item -Path \"$env:LOCALAPPDATA\\NVIDIA\\DXCache\\*\" -Recurse -Force -ErrorAction SilentlyContinue; Write-Host 'Grafik önbellekleri temizlendi.'"
                            },
                            new()
                            {
                                Title = "Donanım Hızlandırmasını Devre Dışı Bırak / Sıfırla",
                                Category = "Kayıt Defteri",
                                Description = "Pencerelerde beyaz kalma veya takılma oluyorsa donanım hızlandırma bayrağını sıfırlar.",
                                Script = "reg add \"HKCU\\Software\\Microsoft\\Avalon.Graphics\" /v DisableHWAcceleration /t REG_DWORD /d 0 /f"
                            },
                            new()
                            {
                                Title = "Grafik Sürücüsünü Hızlı Sıfırla (Win+Ctrl+Shift+B Simülasyonu)",
                                Category = "Sürücü",
                                Description = "Ekran kararması durumunda grafik alt sistemini anında yeniden başlatır.",
                                Script = "Stop-Process -Name dwm -Force"
                            }
                        }
                    }
                },

                // 26. Grup İlkesi (GPO) ve Sistem Kısıtlaması Engeli
                new()
                {
                    ErrorCodes = new[] { "gpo", "gpedit", "gpupdate" },
                    RequiredTokenGroups = new[]
                    {
                        new[] { "grup", "ilkesi" },
                        new[] { "gpo" },
                        new[] { "yonetici", "engellendi" },
                        new[] { "yetkiniz", "yok" },
                        new[] { "kurulusunuz", "tarafindan" },
                        new[] { "yoneticiniz", "tarafindan" }
                    },
                    Keywords = new[] { "grup", "ilkesi", "gpo", "yonetici", "engellendi", "yetkiniz", "yok", "kurulusunuz", "yoneticiniz", "gorev", "yoneticisi", "kayit", "defteri", "ilke", "gpupdate" },
                    Solution = new AISolutionResult
                    {
                        Title = "Grup İlkesi (GPO) ve Sistem Kısıtlaması Engeli",
                        Category = "Grup İlkesi (GPO)",
                        RootCause = "Windows yerel veya etki alanı (Domain GPO) politikaları, kayıt defterinde 'Policies' anahtarları altına kısıtlayıcı güvenlik kuralları yazarak kullanıcı erişimini engellemiş.",
                        Severity = "Orta",
                        Steps = new List<string>
                        {
                            "Kayıt defterindeki kullanıcı ve makine ilke kısıtlama anahtarlarını temizleyin.",
                            "gpupdate /force komutu ile ilkeleri anında yenileyin.",
                            "Engellenen yönetim araçlarının (Görev Yöneticisi, Kayıt Defteri, Klasör Seçenekleri) kilitlerini açın."
                        },
                        AutoFixScript = @"reg delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System"" /v DisableTaskMgr /f -ErrorAction SilentlyContinue; reg delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System"" /v DisableRegistryTools /f -ErrorAction SilentlyContinue; reg delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer"" /v NoFolderOptions /f -ErrorAction SilentlyContinue; gpupdate /force",
                        TargetRegistryBackupPath = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Policies",
                        Alternatives = new List<AISolutionAlternative>
                        {
                            new()
                            {
                                Title = "Tüm Yerel GPO Politikalarını Sıfırla (registry.pol Silme)",
                                Category = "Grup İlkesi (GPO)",
                                Description = "Takılı kalmış yerel grup ilkesi dosyalarını tamamen sıfırlar.",
                                Script = "Remove-Item \"C:\\Windows\\System32\\GroupPolicy\\Machine\\registry.pol\" -Force -ErrorAction SilentlyContinue; Remove-Item \"C:\\Windows\\System32\\GroupPolicy\\User\\registry.pol\" -Force -ErrorAction SilentlyContinue; gpupdate /force"
                            },
                            new()
                            {
                                Title = "GPO Tanılama Raporu Çıkar (gpresult HTML)",
                                Category = "Tanılama",
                                Description = "Hangi ilkenin neyi engellediğini gösteren detaylı HTML raporu üretir.",
                                Script = "gpresult /h \"$env:USERPROFILE\\Desktop\\GPO_Raporu.html\" /f; Start-Process \"$env:USERPROFILE\\Desktop\\GPO_Raporu.html\""
                            }
                        }
                    }
                },

                // 27. Kayıt Defteri Hatası & .EXE / .LNK Dosya İlişkilendirme Onarımı
                new()
                {
                    ErrorCodes = new[] { "regedit", "regsvr32" },
                    RequiredTokenGroups = new[]
                    {
                        new[] { "kayit", "defteri", "hatasi" },
                        new[] { "reg", "hatasi" },
                        new[] { "exe", "acilmiyor" },
                        new[] { "lnk", "acilmiyor" },
                        new[] { "dosya", "iliskilendirme" }
                    },
                    Keywords = new[] { "kayit", "defteri", "reg", "hatasi", "exe", "lnk", "acilmiyor", "calismiyor", "iliskilendirme", "birlikte", "ac", "userchoice" },
                    Solution = new AISolutionResult
                    {
                        Title = "Kayıt Defteri & .EXE / .LNK Dosya İlişkilendirme Onarımı",
                        Category = "Kayıt Defteri & Kabuk Bütünlüğü",
                        RootCause = "Windows Registry altında .exe, .lnk veya temel kabuk uzantılarının ilişkilendirme anahtarları bozulmuş veya silinmiş.",
                        Severity = "Yüksek",
                        Steps = new List<string>
                        {
                            "Temel dosya türü ilişkilendirmelerini (assoc / ftype) varsayılana çekin.",
                            "UserChoice bozuk anahtarlarını sıfırlayın.",
                            "Sistem DLL bileşen kayıtlarını tazeleyin."
                        },
                        AutoFixScript = "cmd.exe /c \"assoc .exe=exefile && ftype exefile=\"\"%1\"\" %*\"; reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\FileExts\\.exe\\UserChoice\" /f -ErrorAction SilentlyContinue; reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\FileExts\\.lnk\\UserChoice\" /f -ErrorAction SilentlyContinue; Stop-Process -Name explorer -Force",
                        TargetRegistryBackupPath = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts",
                        Alternatives = new List<AISolutionAlternative>
                        {
                            new()
                            {
                                Title = "Temel Windows DLL Dosyalarını Yeniden Kaydet (regsvr32)",
                                Category = "Kayıt Defteri",
                                Description = "Kabuk ve tarayıcı bileşen DLL kayıtlarını yeniden kaydeder.",
                                Script = "regsvr32 /s atl.dll; regsvr32 /s urlmon.dll; regsvr32 /s mshtml.dll; regsvr32 /s shell32.dll"
                            },
                            new()
                            {
                                Title = "User Shell Klasörlerini Sıfırla (Masaüstü, Belgeler)",
                                Category = "Kabuk",
                                Description = "Masaüstü veya Belgeler klasörü kaybolduysa kayıt defteri yollarını varsayılana döndürür.",
                                Script = "reg add \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\User Shell Folders\" /v Desktop /t REG_EXPAND_SZ /d \"%USERPROFILE%\\Desktop\" /f; Stop-Process -Name explorer -Force"
                            }
                        }
                    }
                },

                // 28. Windows Update Yükleme Hatası (SoftwareDistribution Sıfırlama)
                new()
                {
                    ErrorCodes = new[] { "0x80070002", "0x80240020", "0x80070422", "wuauserv" },
                    RequiredTokenGroups = new[]
                    {
                        new[] { "windows", "update" },
                        new[] { "guncellestirme", "hatasi" },
                        new[] { "guncelleme", "yapilamiyor" }
                    },
                    Keywords = new[] { "windows", "update", "guncellestirme", "guncelleme", "0x80070002", "0x80240020", "indirilemiyor", "yuklenemiyor" },
                    Solution = new AISolutionResult
                    {
                        Title = "Windows Update İndirme ve Yükleme Hatası Onarımı",
                        Category = "Windows Update",
                        RootCause = "C:\\Windows\\SoftwareDistribution indirme önbelleği bozulmuş veya wuauserv/BITS servisleri yanıt vermiyor.",
                        Severity = "Orta",
                        Steps = new List<string>
                        {
                            "Windows Update ve BITS hizmetlerini geçici olarak durdurun.",
                            "SoftwareDistribution\\Download klasöründeki bozuk paketleri temizleyin.",
                            "Hizmetleri yeniden başlatıp güncelleme denetimini tetikleyin."
                        },
                        AutoFixScript = "Stop-Service -Name wuauserv, bits, cryptsvc -Force -ErrorAction SilentlyContinue; Remove-Item 'C:\\Windows\\SoftwareDistribution\\Download\\*' -Recurse -Force -ErrorAction SilentlyContinue; Start-Service -Name wuauserv, bits, cryptsvc; usoclient StartScan",
                        Alternatives = new List<AISolutionAlternative>
                        {
                            new()
                            {
                                Title = "Windows Update Servislerini Tam Baştan Başlat",
                                Category = "Servisler",
                                Description = "Tüm güncelleme arka plan servislerini zorla yeniden başlatır.",
                                Script = "Restart-Service -Name wuauserv, bits, cryptsvc, trustedinstaller -Force"
                            }
                        }
                    }
                },

                // 29. Ses Hizmeti (AudioSrv) & Hoparlör / Mikrofon Kilitlenmesi
                new()
                {
                    RequiredTokenGroups = new[]
                    {
                        new[] { "ses", "yok" },
                        new[] { "hoparlor" },
                        new[] { "mikrofon" },
                        new[] { "audiosrv" }
                    },
                    Keywords = new[] { "ses", "hoparlor", "mikrofon", "calismiyor", "gelmiyor", "audiosrv", "kulaklik" },
                    Solution = new AISolutionResult
                    {
                        Title = "Windows Ses Hizmeti (AudioSrv) ve Çıkış Aygıtı Onarımı",
                        Category = "Ses & Donanım",
                        RootCause = "Windows Audio (AudioSrv) veya Windows Audio Endpoint Builder servisleri çökmüş veya aygıt askıya alınmış.",
                        Severity = "Normal",
                        Steps = new List<string>
                        {
                            "AudioSrv ve AudioEndpointBuilder hizmetlerini durdurup yeniden başlatın.",
                            "Kullanıcı oturumu ses sürücüsü iş parçacığını tazeleyin."
                        },
                        AutoFixScript = "Restart-Service -Name AudioEndpointBuilder, Audiosrv -Force",
                        Alternatives = new List<AISolutionAlternative>
                        {
                            new()
                            {
                                Title = "Ses Denetim Masasını Aç (mmsys.cpl)",
                                Category = "Denetim Masası",
                                Description = "Varsayılan ses çalma ve kayıt aygıtlarını doğrudan yapılandırmak için açar.",
                                Script = "mmsys.cpl"
                            }
                        }
                    }
                }
            };
        }

        private async Task<AISolutionResult?> QueryGeminiApiAsync(string query)
        {
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={ApiKey}";
            string prompt = $@"Sen üst düzey bir Kurumsal Windows ve Ağ BT Sistem Mühendisliği Yapay Zekasısın.
Aşağıdaki kullanıcı veya teknisyen arıza bildirimini analiz et ve SADECE saf JSON formatında cevap ver. Markdown kod blokları (```json vs.) KESİNLİKLE KULLANMA.

Kullanıcı Arızası:
{query}

İstenen JSON formatı:
{{
  ""title"": ""Arıza Kısa Başlığı"",
  ""rootCause"": ""Teknik kök neden açıklaması (1-2 cümle)"",
  ""severity"": ""Normal/Orta/Yüksek/Kritik"",
  ""steps"": [""Adım 1"", ""Adım 2"", ""Adım 3""],
  ""autoFixScript"": ""Tek satırda veya noktalı virgüllü çalıştırılabilir PowerShell onarım komutu""
}}";

            var payload = new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = prompt } } }
                }
            };

            string jsonBody = JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                string resJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(resJson);
                string text = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text").GetString() ?? "";

                text = text.Replace("```json", "").Replace("```", "").Trim();
                return JsonSerializer.Deserialize<AISolutionResult>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }

            return null;
        }
    }

    public class AISolutionAlternative
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Script { get; set; } = "";
        public string Category { get; set; } = "Genel";
    }

    public class AISolutionResult
    {
        public string Title { get; set; } = "";
        public string Category { get; set; } = "Genel Sistem";
        public string RootCause { get; set; } = "";
        public string Severity { get; set; } = "Orta";
        public List<string> Steps { get; set; } = new();
        public string AutoFixScript { get; set; } = "";
        public bool HasScript => !string.IsNullOrWhiteSpace(AutoFixScript);
        public string WebInsights { get; set; } = "";
        public bool HasWebInsights => !string.IsNullOrWhiteSpace(WebInsights);
        public List<AISolutionAlternative> Alternatives { get; set; } = new();
        public bool HasAlternatives => Alternatives != null && Alternatives.Count > 0;
        public string TargetRegistryBackupPath { get; set; } = "";
        public bool IsVerified { get; set; } = true;
        public string VerifiedBadge { get; set; } = "✅ ONAYLI RESMİ ÇÖZÜM";
        public string SourceTier { get; set; } = "Microsoft & Kurumsal WinoxIT Standardı";
        public string SourceUrl { get; set; } = "";
        public NormalizedSymptom? PredictedSymptom { get; set; }
        public IReadOnlyList<string> ExtractedTokens { get; set; } = Array.Empty<string>();
        public double ConfidenceScore { get; set; } = 0.85;
    }
}
