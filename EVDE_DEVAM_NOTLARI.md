# ⚡ WinoxIT — Evde Devam & Geliştirme El Kitabı (Session Handover)

> **Oturum Tarihi:** 21 Ekim 2026  
> **Proje:** WinoxIT — Enterprise Windows IT, Hardware & Deployment Suite  
> **Sürüm:** v1.0.0-enterprise (Derleme: 1.0.0.0)  
> **GitHub Deposu:** [https://github.com/mtahiryilmazz/WinoxIT](https://github.com/mtahiryilmazz/WinoxIT)  
> **Çalışma Alanı:** `C:\Users\BT\Documents\Github\WinoxIT`

---

## 📌 1. Bu Oturumda Neler Yapıldı? (Detaylı Özet)

### A. Depo Kurulumu ve GitHub Eşitlemesi
* Proje bağımsız bir git deposu olarak yapılandırıldı.
* `mtahiryilmazz/WinoxIT` adı altında **özel (private)** GitHub deposu oluşturuldu.
* Kurumsal standartlarda `README.md`, `ROADMAP.md`, `CHANGELOG.md`, `LICENSE`, `.gitignore`, `.github/workflows/build.yml` ve issue şablonları eklendi.

### B. Piyasa Analizi ve Ticari Yol Haritası Genişletmesi
* Muadil araçlar (Chris Titus WinUtil, Optimizer, BloatyNosy, Sophia Script, Tactical RMM, PDQ Deploy, ManageEngine, 3DP Net, Snappy Driver) analiz edildi.
* **BTK, Kamu ve Bakanlıklar** için satışa çıkacak ticari düzeyde **3 Katil Modül** yol haritasına ve mimariye eklendi:
  1. **Gömülü Evrensel Çevrimdışı Ağ Kartı Paketi (Universal Offline NIC):** Format sonrası interneti olmayan cihazlara 5 saniyede internetsiz WHQL sürücü enjekte etme (Intel, Realtek, Broadcom, MediaTek).
  2. **Anakart Tespiti ve Orijinal OEM Sürücü İndirici:** Dell (`dcu-cli`), HP (`HPIA/CMSL`), Lenovo (`ThinInstaller`) ve ASUS/MSI resmi kataloglarından temiz sürücü çekme.
  3. **Kurumsal Ninite/Winget AppDeploy & Yerel LAN Önbellekleme:** Sessiz kurulum motoru ve tek bilgisayarın indirdiği paketleri yerel ağdaki 50 cihaza internet kotası harcamadan dağıtma.

### C. Web Kokpiti & Node.js Sanal Sunucu Katmanı
* `server.js` geliştirildi (0.0.0.0:9999 portunda tüm ağ arayüzlerini otomatik keşfeder).
* `Launch-NodeServer.cmd` ile tek tıkla Windows Güvenlik Duvarı'nda Port 9999 TCP kuralı açılır ve sunucu başlar.
* **Markalama:** `dashboard.html` içerisindeki tüm "SysForge" / "SysForge Studio" referansları (65 yer) tamamen temizlendi ve **WinoxIT** yapıldı.
* **Tema Onarımı:** 17 temanın tamamını ezen CSS kuralı düzeltildi; Matrix, Cyberpunk, Tesla, Iron Man, Solar, Ocean vb. tüm temalar çalışır duruma getirildi.
* Ekran düzeni (layout) tam ekran ve dengeli konuma getirildi.

### D. Masaüstü Uygulaması (.NET 8 WPF Fluent UI)
* **Açılış Çökme Hatası Giderildi:** XAML parse aşamasında oluşan `NullReferenceException` engellendi (`_isInitialized` bayrağı ve güvenli panel kontrolleri).
* **11 Menünün Tamamı Aktifleştirildi:** Sol menüdeki tüm butonlar (Dashboard, LAN Sunucu, Donanım & HWID, Yazıcı Onarım, Temizlik, Debloat, Ağ, Çekirdek Onarım, İşlemler, Başlangıç, Yapay Zekâ) tıklandığında kendi özel interaktif panellerini açacak şekilde bağlandı.
* **Tek Tarz Buton Tasarımı:** Kullanıcı talebi doğrultusunda ilk butonun alakasız renkli olup diğerlerinin gri kalması engellendi; tüm operasyon butonları modern, tutarlı ve tek tarz Fluent buton (`FluentButton`) stiline kavuşturuldu.
* **Açık & Koyu Tema Desteği:** Masaüstü arayüzüne sağ üstten tek tıkla çalışan dinamik **Açık / Koyu Tema** değiştirici eklendi (`BtnThemeToggle`).
* **Sürüm Numarası:** `WinoxIT.csproj` dosyasına `1.0.0.0` sürüm numarası, telif hakkı ve kurumsal ürün bilgileri işlendi.
* **Taşınabilirlik:** `WinoxIT.exe` derlendiğinde `server.js`, `Launch-NodeServer.cmd` ve `web/` klasörünü otomatik olarak kendi yanına (`bin/Release`) kopyalaması sağlandı.

---

## 🚀 2. Evde Başlarken (Quick Start at Home)

### Adım 1: Depoyu Güncelleyin
Evdeki bilgisayarda projeyi çekmek için:
```powershell
# İlk defa çekiyorsanız:
git clone https://github.com/mtahiryilmazz/WinoxIT.git
cd WinoxIT

# Zaten varsa güncelleyin:
git pull origin main
```

### Adım 2: Masaüstü Programını Çalıştırma
* Hazır tek dosya derleme:
  📁 `C:\Users\BT\Documents\Github\WinoxIT\bin\Release\WinoxIT.exe`
* Geliştirici ortamında çalıştırmak için:
  ```powershell
  cd gui
  dotnet run
  ```

### Adım 3: Web Kokpiti & LAN Sanal Sunucusunu Başlatma
* Kök dizindeki `Launch-NodeServer.cmd` dosyasını yönetici olarak çalıştırın veya:
  ```powershell
  node server.js
  ```
* Tarayıcıdan açın:
  🔗 `http://localhost:9999/dashboard.html`

---

## 📂 3. Önemli Dosya Haritası

| Dosya / Dizin | Görevi ve Önemi |
| :--- | :--- |
| `bin/Release/WinoxIT.exe` | Son derlenen, taşınabilir ve sürüm damgalı kurumsal masaüstü programı. |
| `gui/MainWindow.xaml` | 11 menünün ve tek tarz butonların bulunduğu XAML arayüzü. |
| `gui/MainWindow.xaml.cs` | Menü geçişleri, Açık/Koyu tema yönetimi, sunucu denetleyicisi ve sistem API çağrıları. |
| `gui/Styles/FluentStyles.xaml` | Dinamik renk paletleri ve tek tip `FluentButton` stili. |
| `server.js` | 0.0.0.0:9999 Node.js yüksek eşzamanlı sanal sunucusu. |
| `web/dashboard.html` | 17 çalışan tema, Türkçeleştirilmiş arayüz ve siber HUD paneli. |
| `WinoxIT_Roadmap.md` | Ticari ve teknik kapsamlı geliştirme yol haritası. |

---

## 🎯 4. Evde Devam Edilecek Sıradaki Görevler (Next Steps Checklist)

- [ ] **Universal Offline NIC Paketleme:** Intel ve Realtek Ethernet/Wi-Fi INF sürücülerinin `drivers/universal_nic/` klasörüne LZMA sıkıştırmasıyla eklenmesi ve `PnPUtil` ile tek tıkla yükleme testinin yapılması.
- [ ] **OEM Driver İndirme Entegrasyonu:** Dell `dcu-cli.exe` ve Lenovo `ThinInstaller` komut satırı tetikleyicilerinin C# `NativeSystemEngine.cs` içerisine metot olarak eklenmesi.
- [ ] **AppDeploy Winget Modülü:** Popüler kurumsal programların (Chrome, 7-Zip, AnyDesk, UYAP, AKİS) WPF arayüzünden tek tıkla sessiz (`--silent`) kurulabilmesi.
- [ ] **Hardware Drift Raporu:** Cihaz teslim baseline hash'i ile anlık hash karşılaştırmasını WPF ekranında görsel diff olarak sunma.

---
*WinoxIT projesi başarıyla eşitlenmiştir. Evde `git pull` yaparak kaldığınız yerden devam edebilirsiniz.*
