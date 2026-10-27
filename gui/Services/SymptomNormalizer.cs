using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace WinoxIT.Services
{
    public sealed record NormalizedSymptom(
        string Code,
        string Label,
        string IssueFamily,
        string Domain,
        string NormalizedText,
        IReadOnlyList<string> Tokens,
        IReadOnlyList<string> MatchedTerms,
        double Confidence,
        string RecommendedActionTitle,
        string ActionKey);

    /// <summary>
    /// YAPAIR-1 tabanlı deterministik Türkçe şikayet & kullanıcı ifadesi normalizasyonu,
    /// anahtar kelime/token çıkarımı ve arıza tahmin motoru.
    /// </summary>
    public static partial class SymptomNormalizer
    {
        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "bir", "ve", "bu", "su", "o", "da", "de", "ile", "icin", "ama", "fakat",
            "cok", "az", "var", "yok", "gibi", "daha", "en", "mi", "mu", "ise",
            "the", "a", "an", "is", "are", "and", "or", "to", "of", "my", "not",
            "benim", "bize", "olan", "bunu", "icin", "kadar", "nasil", "neden"
        };

        private static readonly (string[] Keywords, string Domain)[] DomainMap =
        {
            (new[] { "bluetooth", "kablosuz", "bt" }, "bluetooth"),
            (new[] { "yazici", "yazdir", "printer", "spooler", "kuyruk", "kagit" }, "printers"),
            (new[] { "ag", "internet", "wifi", "ethernet", "baglanti", "ip", "dns", "ping" }, "network"),
            (new[] { "ses", "hoparlor", "mikrofon", "audio", "sound", "kulaklik" }, "audio"),
            (new[] { "usb", "aygit", "surucu", "driver", "pnp", "cihaz", "ekran karti", "gpu" }, "devices"),
            (new[] { "guncelleme", "update", "reboot", "yeniden baslat", "wu" }, "windows_update"),
            (new[] { "uac", "yonetici", "izin", "yetki", "firewall", "guvenlik", "defender", "admin" }, "security"),
            (new[] { "e-imza", "eimza", "kamusm", "akis", "ibsigner", "arksigner", "uyap", "sertifika", "java" }, "kamu_eimza"),
            (new[] { "bsod", "mavi ekran", "minidump", "crash", "coktu", "kilitlendi", "nvlddmkm" }, "bsod"),
            (new[] { "disk", "depolama", "yer yok", "dolu", "temp", "hafiza", "ram", "donuyor", "yavas" }, "performance_storage")
        };

        private static readonly SymptomRule[] Rules =
        {
            Rule("printer_queue", "Yazdırma kuyruğu / Spooler kilitlenmesi", "printers", "printers",
                "⚡ Spooler Servisini Yeniden Başlat", "restart_spooler",
                "kuyruk", "queue", "spooler", "biriktirici", "takildi", "kuyrukta", "yazdirilamiyor", "silinmiyor"),

            Rule("printer_offline", "Yazıcı çevrimdışı görünüyor", "printers", "printers",
                "⚡ Yazıcı Durumunu & Portu Denetle", "check_printer",
                "cevrimdisi", "offline", "bagli degil", "ulasilamiyor", "gorunmuyor", "baglanti kesildi"),

            Rule("printer_output_failure", "Yazıcı çıktı vermiyor", "printers", "printers",
                "⚡ Yazdırma Biriktiricisini Yeniden Başlat", "restart_spooler",
                "yazdirmiyor", "cikti gelmiyor", "cikti vermiyor", "print etmiyor", "baski yapmiyor", "yazici calismiyor"),

            Rule("printer_jam", "Yazıcıda kağıt sıkışması", "printers", "printers",
                "⚡ Yazıcı Kuyruğunu Temizle", "clear_printer_queue",
                "kagit sikismasi", "sikisma", "paper jam", "jam", "kagit"),

            Rule("network_dns", "DNS & İnternet bağlantı sorunu", "network", "network",
                "⚡ DNS Önbelleğini Temizle & Yenile", "flush_dns",
                "dns", "internet baglanmiyor", "internet yok", "site acilmiyor", "baglanti hatasi", "dns cozumlenmiyor"),

            Rule("network_adapter", "Ağ bağdaştırıcısı & TCP/IP çökmesi", "network", "network",
                "⚡ Ağ Bağdaştırıcısını & Winsock Sıfırla", "reset_network",
                "ag baglantisi", "wifi baglanmiyor", "ethernet baglanmiyor", "ip almiyor", "ag kopuyor", "winsock", "tcp ip"),

            Rule("audio_no_sound", "Ses çıkışı / Kulaklık / Hoparlör arızası", "audio", "audio",
                "⚡ Windows Ses Servislerini Yeniden Başlat", "restart_audio",
                "ses gelmiyor", "ses yok", "hoparlor", "kulaklik", "audio", "sound", "mikrofon calismiyor"),

            Rule("storage_low_space", "Disk doluluğu / %TEMP% şişmesi", "storage", "performance_storage",
                "⚡ %TEMP% ve Sistem Atıklarını Temizle", "clean_temp",
                "disk dolu", "yer yok", "depolama", "disk alani", "c surucusu dolu", "temp temizle", "low disk"),

            Rule("performance_slow", "Sistemde aşırı yavaşlık & Donma", "performance", "performance_storage",
                "⚡ Süreç Monitörünü Aç & RAM Rahatlat", "open_processes",
                "yavas", "donuyor", "donma", "freeze", "slow", "performans", "kasti", "takiliyor", "ram dolu"),

            Rule("bsod_crash", "Mavi Ekran (BSOD) / Sistem Çökmesi", "system", "bsod",
                "⚡ BSOD Minidump & Olay Analizi Yap", "scan_bsod",
                "mavi ekran", "bsod", "minidump", "dmp", "nvlddmkm", "coktu", "yeniden basladi", "kernel data inpage"),

            Rule("kamu_eimza", "Kamu E-İmza & AKİS / UYAP / Sertifika", "kamu", "kamu_eimza",
                "⚡ Kamu E-İmza Kurulum Menüsünü Aç", "open_kamu_hub",
                "e-imza", "eimza", "kamusm", "akis", "ibsigner", "arksigner", "uyap", "sertifika", "kart okunmuyor"),

            Rule("uac_admin", "UAC / Yönetici yetkisi / Dosya kilidi", "security", "security",
                "⚡ Sağ Tık 'Sahipliği Al' Menüsü Ekle", "enable_take_ownership",
                "uac", "yonetici izni", "admin", "izin istiyor", "erisim engellendi", "dosya silinmiyor", "trustedinstaller"),

            Rule("windows_update", "Windows Update hata / Yükleme kilitlenmesi", "windows_update", "windows_update",
                "⚡ Windows Update Önbelleğini Sıfırla", "clean_wu",
                "guncelleme", "update", "windows update", "guncellenmiyor", "0x8007", "0x8024", "update hatasi"),

            Rule("app_crash", "Uygulama açılmıyor / Visual C++ Runtime eksik", "system", "system",
                "⚡ SFC Dosya Taraması & Runtime Onar", "run_sfc",
                "program acilmiyor", "uygulama acilmiyor", "dll bulunamadi", "0xc000007b", "runtime error")
        };

        public static string DetectDomain(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return "general";
            var normalized = " " + NormalizeForMatch(message) + " ";
            foreach (var (keys, domain) in DomainMap)
            {
                if (keys.Any(k => normalized.Contains(" " + k + " ") || normalized.Contains(k)))
                    return domain;
            }
            return "general";
        }

        public static NormalizedSymptom Normalize(string? text)
        {
            var normalized = NormalizeForMatch(text);
            var tokens = Tokenize(normalized);
            var domain = DetectDomain(text ?? "");

            SymptomRule? bestRule = null;
            var bestMatches = new List<string>();
            var bestScore = 0;

            foreach (var rule in Rules)
            {
                var matches = rule.Phrases
                    .Where(phrase => ContainsPhrase(normalized, phrase))
                    .ToList();

                if (matches.Count == 0) continue;

                var score = (matches.Count * 12) + matches.Max(static phrase => phrase.Length);
                if (score > bestScore)
                {
                    bestRule = rule;
                    bestMatches = matches;
                    bestScore = score;
                }
            }

            if (bestRule is null)
            {
                return new NormalizedSymptom(
                    "unknown_symptom",
                    "Genel BT Belirtisi",
                    "system",
                    domain,
                    string.Join(' ', tokens),
                    tokens,
                    Array.Empty<string>(),
                    tokens.Count > 0 ? 0.35 : 0.0,
                    "⚡ Kapsamlı AI Analizi Başlat",
                    "general_ai_analyze");
            }

            double confidence = Math.Min(0.99, Math.Max(0.50, bestScore / 25.0));

            return new NormalizedSymptom(
                bestRule.Code,
                bestRule.Label,
                bestRule.IssueFamily,
                bestRule.Domain,
                string.Join(' ', tokens),
                tokens,
                bestMatches,
                confidence,
                bestRule.ActionTitle,
                bestRule.ActionKey);
        }

        public static string NormalizeForMatch(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            var lowered = text.ToLower(new CultureInfo("tr-TR"))
                .Replace('ç', 'c')
                .Replace('ğ', 'g')
                .Replace('ı', 'i')
                .Replace('ö', 'o')
                .Replace('ş', 's')
                .Replace('ü', 'u')
                .Replace('İ', 'i')
                .Replace('I', 'i');

            lowered = Regex.Replace(lowered, @"[^a-z0-9\s]+", " ");
            lowered = Regex.Replace(lowered, @"\s+", " ").Trim();
            return lowered;
        }

        public static IReadOnlyList<string> Tokenize(string normalizedText)
        {
            if (string.IsNullOrWhiteSpace(normalizedText)) return Array.Empty<string>();

            return normalizedText
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(static token => token.Length > 1 && !StopWords.Contains(token))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool ContainsPhrase(string normalizedText, string phrase)
        {
            if (normalizedText.Length == 0 || phrase.Length == 0) return false;
            return $" {normalizedText} ".Contains($" {phrase} ", StringComparison.Ordinal);
        }

        private static SymptomRule Rule(string code, string label, string family, string domain, string actionTitle, string actionKey, params string[] phrases) =>
            new(code, label, family, domain, actionTitle, actionKey, phrases.Select(NormalizeForMatch).Where(static p => p.Length > 0).ToArray());

        private sealed record SymptomRule(
            string Code,
            string Label,
            string IssueFamily,
            string Domain,
            string ActionTitle,
            string ActionKey,
            IReadOnlyList<string> Phrases);
    }
}
