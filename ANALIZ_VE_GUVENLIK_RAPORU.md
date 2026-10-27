# 🛡️ WinoxIT — Depo, Güvenlik ve Mimari Analiz Raporu
> **Metodoloji:** Ponytail Audit · Karpathy Guidelines · ECC Standard  
> **Tarih:** 27 Ekim 2026  
> **Kapsam:** WinoxIT Deposu — Güvenlik açıkları, API anahtarı, kişisel veri/yol sızıntıları, statik sunucu güvenliği ve derleme sağlığı.

---

## 📌 1. Depo Ne İşi Yapar? (What This Repo Does)

**WinoxIT**, yüksek performanslı hibrit sistem mühendisliği aracıdır:
- **Rust Çekirdeği (`src/`):** Donanım parmak izi, SHA-256 donanım drift dedektörü, çevrimdışı debloat ve sistem tanılama motoru.
- **WPF .NET 8 GUI (`gui/`):** Modern XAML arayüzü, donanım kartları, ağ ve sürücü orkestrasyonu.
- **Node.js Sanal Sunucusu (`server.js`):** LAN / mobil HUD kokpiti ve yerel REST API ağ geçidi (`0.0.0.0:9999`).
- **Web Kokpiti (`web/dashboard.html`):** TR/EN dil destekli, telemetri, sürücü, donanım ekspertiz ve tek tıkla onarım arayüzü.

---

## 🚨 2. Güvenlik, Veri ve Anahtar Analizi (Audit Bulguları & Düzeltmeler)

### 1. API Anahtarları ve Hassas Kimlik Bilgileri (API Keys & Secrets)
- **Durum:** ✅ **TEMİZ (0 Açık)**
- **Analiz:** Depo genelinde Google Gemini, OpenAI, AWS, GitHub token veya özel şifreleme anahtarları arandı (`AIzaSy`, `api_key`, `secret`, `bearer`, `.key`, `.env`). Depoda sızdırılmış hiçbir API anahtarı veya kimlik bilgisi bulunmamaktadır.

### 2. Kişisel Kullanıcı Yolları Temizlendi (`web/dashboard.html`)
- **Problem:** Web arayüzünde 3 farklı noktada üçüncü taraf geliştiriciye ait `C:\Users\Ansh\...` hardcoded dosya yolları yer alıyordu:
  - `Line 95457:` Duvar kâğıdı placeholder'ı `C:\Users\Ansh\Desktop\fresh\...`
  - `Line 257401:` Teşhis raporu çıktısı `C:\Users\Ansh\Desktop\fresh\Docs\HealthReport.html`
  - `Line 410778:` Discord uygulama yolu `C:\Users\Ansh\AppData\Local\Discord\...`
- **Düzeltme:** 
  - Duvar kâğıdı yolu: `C:\Users\Public\Pictures\wallpaper.png`
  - Teşhis raporu yolu: `C:\Users\Public\Documents\WinoxIT\Docs\HealthReport.html`
  - Discord yolu: `C:\Program Files\Discord\Discord.exe`

### 3. `server.js` Dizin Geçişi (Directory Traversal) ve Hassas Dosya Engeli
- **Problem:** Statik dosya sunucusu yalnızca `startsWith(WEB_DIR)` kontrolü yapıyordu; dosya uzantısı veya gizli dosya (`.`) kısıtlaması yoktu.
- **Düzeltme:**
  - `path.resolve` ile güvenli yol çözümlenmesi sağlandı.
  - Gizli dosyalara (`.`) ve hassas dosya uzantılarına (`.key`, `.token`, `.env`, `.pfx`, `.cer`, `.db`, `.log`, `.cs`, `.csproj`, `.sln`, `.suo`, `.rs`, `.toml`, `.lock`) erişim `403 Forbidden` ile engellendi.
  - SPA fallback mekanizması yalnızca geçerli web rotaları için sınırlandırıldı.

### 4. Windows Sandbox Sabit Host Yolu & Taşınabilir Başlatıcı
- **Problem:** `WinoxIT_Sandbox_Test.wsb` dosyasında `<HostFolder>D:\mtahiryilmazz\Github\winoxit</HostFolder>` şeklinde geliştiricinin fiziksel sürücü ve kullanıcı yolu hardcoded yazılmıştı. Başka bir makinede çalışmıyordu.
- **Düzeltme:**
  - WSB içindeki yol `C:\WinoxIT` olarak genelleştirildi.
  - Dinamik olarak o anki çalışma dizinini algılayıp `%TEMP%` üzerinde sandbox dosyasını üreten ve çift tıkla başlatan [`Launch-Sandbox.cmd`](Launch-Sandbox.cmd) eklendi.

### 5. `.gitignore` İzolasyonu
- Gelecekte oluşabilecek `.env`, `.key`, `.token`, `.pfx`, SQLite `.db` ve 17 farklı yapay zekâ IDE klasörünün (`.cursor`, `.windsurf`, `.claude`, `.factory` vb.) yanlışlıkla commit edilmesini engelleyen kurallar [`.gitignore`](.gitignore)'a eklendi.

---

## 🛠️ 3. Derleme ve Çalışma Doğrulaması

1. **WPF Masaüstü (.NET 8 Windows x64 Release):**
   ```powershell
   dotnet build gui\WinoxIT.csproj -c Release
   ```
   - **Sonuç:** `0 Uyarı, 0 Hata` ile başarıyla derlendi.
2. **Rust Sistem Çekirdeği:**
   ```powershell
   cargo check
   ```
   - **Sonuç:** `Finished dev profile` (0 hata).
3. **Node.js Sanal Sunucu:**
   ```powershell
   node -c server.js
   ```
   - **Sonuç:** Sözdizimi geçerli ve hatasız.

---

## 📊 4. Sonuç Özeti

- **Güvenlik Derecesi:** **A+ (Güvenli & Temiz)**
- **Gizlilik / PII Durumu:** Tüm kişisel ve makineye özel yollar temizlendi; depo açık kaynak veya ekip içi paylaşıma hazır hale getirildi.
- **Çalıştırılabilirlik:** Hem masaüstü (.NET 8) hem web kokpiti (Node.js) hem de Windows Sandbox ortamında bağımsız ve taşınabilir olarak test edilebilir.
