# 📝 WinoxIT Değişiklik Günlüğü (Changelog)

Tüm önemli değişiklikler bu dosyada belgelenecektir.  
Proje [Keep a Changelog](https://keepachangelog.com/tr/1.0.0/) standartlarını takip eder.

---

## [1.0.1] - 2026-10-26

### ✨ Eklenenler (Added)
- **Yeni marka kimliği:** "Windows Dynamic X-O Blades" logosu; `branding/` altında kaynak SVG'ler, siyah/beyaz/kare varyantlar, PWA ikon seti ve `site.webmanifest`.
- **Uygulama simgesi:** `gui/app.ico` (16/32/48/256 px) `WinoxIT.csproj` içine `ApplicationIcon` olarak bağlandı.
- **Web kokpit varlıkları:** `web/favicon.ico`, `web/logo.png`, `web/winoxit-logo.svg`.
- **Windows Sandbox test profili:** `WinoxIT_Sandbox_Test.wsb`.

### 🔧 Değişenler (Changed)
- `publish/` çıktısı `.gitignore`'a eklendi (self-contained tek dosya ~155 MB; GitHub Releases ile dağıtılacak).
- README: logo, marka kimliği ve derleme/yayımlama bölümleri eklendi.

### 📝 Düzeltmeler (Fixed)
- README'deki "Rust Native Engine" ifadesi düzeltildi: Rust modülleri henüz `main.rs`'e bağlı değil, işlemler C# `NativeSystemEngine` ile çalışıyor.

### 🗺️ Planlanan
- SysForge-Studio'nun tüm özelliklerinin WinoxIT'e (.NET 8) taşınması; ek araçlar sessiz modda menülere entegre edilecek.

---
## [1.0.0-alpha] - 2026-10-21

### ✨ Yeni Özellikler (Added)
- **Hibrit Mimari Entegrasyonu:** .NET 8 WPF Fluent UI masaüstü istemcisi ile yerel Rust çekirdek motoru ve Node.js sanal sunucusu birleştirildi.
- **Node.js Virtual Server (`server.js`):** `0.0.0.0:9999` portunda çalışan, çoklu ağ arayüzlerini (Ethernet/Wi-Fi) otomatik keşfeden asenkron sunucu katmanı.
- **Cyber HUD Web Kokpiti (`web/dashboard.html`):** 10 ana modül içeren, bağımsız çalışan siber web arayüzü entegre edildi.
- **Tek Tıkla Sunucu Başlatıcı (`Launch-NodeServer.cmd`):** Port 9999 TCP Windows Güvenlik Duvarı kuralını otomatik açan ve tarayıcıyı başlatan toplu iş betiği.
- **Kurumsal Yazıcı Onarım Motoru:** 
  - `0x0000011b` RPC yetkilendirme kayıt defteri onarımı (`RpcAuthnLevelExemption`).
  - `0x00000709` Point & Print yetki kısıtlama onarımı.
- **Windows 11 Modern Gizlilik Kalkanı:**
  - Copilot ve Windows Recall AI telemetrisini GPO/Registry üzerinden engelleme.
  - Windows 10 klasik bağlam (sağ tık) menüsünü geri getirme anahtarı.
- **Hızlı Sistem Operasyonları:** DNS önbelleği temizleme, %TEMP% temizleme, kilitlenen Spooler servisini yeniden başlatma.

### 🌐 Dil & Çeviri Düzeltmeleri (Fixed)
- `dashboard.html` içerisindeki melez/bozuk ifadeler giderildi (`loadBaşlatup` -> `loadStartup`, `runRecommendedEylems` -> `runRecommendedActions`, `Product Lisans Anahtarıs` -> `Product License Keys` vb.).
- Geriye dönük uyumluluk takma adları (aliases) oluşturuldu.

### 🎯 Ticari & Kurumsal Modüller (Implemented)
- **Evrensel Çevrimdışı Ağ Kartı Paketi (Universal Offline NIC Engine):** `drivers/universal_nic/` dizini, `install_nic.cmd` bağımsız WinPE aracı ve `PnPUtil` tek tıkla sürücü enjeksiyonu.
- **Orijinal OEM Donanım & Sürücü Orkestratörü:** Dell (`dcu-cli.exe`), Lenovo (`tvsu.exe`), HP Image Assistant resmi katalog tetikleyicileri ve otomatik OEM üretici web portal yönlendirmesi.
- **Kurumsal AppDeploy & Paket Dağıtımı:** Winget sessiz (`--silent`) kurulum motoru (Chrome, Firefox, 7-Zip, AnyDesk, RustDesk, Notepad++, VLC, VS Code, Git, LibreOffice, UYAP, AKİS) ve yerel LAN önbellek (`packages/`) entegrasyonu.
- **Kriptografik Donanım Ekspertizi & Drift Tespiti:** WMI tabanlı anakart, CPU, RAM modül slotları ve disk seri numaralarını içeren deterministik SHA-256 baseline kaydı ve görsel karşılaştırmalı donanım sapması (Drift Diff) alarm sistemi.
