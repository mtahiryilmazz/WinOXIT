# 📝 WinoxIT Değişiklik Günlüğü (Changelog)

Tüm önemli değişiklikler bu dosyada belgelenecektir.  
Proje [Keep a Changelog](https://keepachangelog.com/tr/1.0.0/) standartlarını takip eder.

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

### 🎯 Planlanan Ticari Modüller (Roadmap Additions)
- **Evrensel Çevrimdışı Ağ Kartı Paketi (Universal Offline NIC Engine):** Intel, Realtek, Broadcom, MediaTek WHQL INF sürücü paketi ile format sonrası sıfır internetsiz anında ağ bağlantısı.
- **Orijinal OEM Sürücü İndirici:** Dell, HP, Lenovo ve ASUS resmi kataloglarından donanım bazlı sürücü çekme.
- **Kurumsal AppDeploy:** Ninite/Winget silent kurulum ve yerel LAN önbellekleme (Offline Mirror) mimarisi.
