# 🏢 WinoxIT — Orijinal OEM Kurumsal Sürücü Orkestratörü
## Dell, HP ve Lenovo Resmi Katalog API & CLI Entegrasyonu

Bu modül, kurumsal bilgisayarlarda üçüncü parti şüpheli sürücü paketleri (SDI vb.) yerine **üreticinin kendi resmi kurumsal güncelleme motorlarını** kullanarak en temiz ve en güncel WHQL sürücüleri yükler.

### 1. Dell Technologies
- **Araç:** Dell Command | Update CLI (`dcu-cli.exe`)
- **Varsayılan Konum:** `C:\Program Files\Dell\CommandUpdate\dcu-cli.exe`
- **Komutlar:**
  - Tarama: `dcu-cli.exe /scan -outputLog="C:\ProgramData\WinoxIT\dell_scan.log"`
  - Sessiz Kurulum: `dcu-cli.exe /applyUpdates -silent -reboot=disable`
  - Winget ID: `Dell.CommandUpdate`

### 2. Lenovo Think & Idea Serileri
- **Araç:** Lenovo System Update / ThinInstaller CLI (`tvsu.exe` / `ThinInstaller.exe`)
- **Varsayılan Konum:** `C:\Program Files (x86)\Lenovo\System Update\tvsu.exe`
- **Komutlar:**
  - Sessiz Güncelleme: `tvsu.exe /CM -search A -action INSTALL -includereboot 0 -noreboot`
  - Winget ID: `Lenovo.SystemUpdate`

### 3. HP (Hewlett-Packard)
- **Araç:** HP Image Assistant (HPIA) / HP CMSL (Client Management Script Library)
- **Komutlar:**
  - `HPImageAssistant.exe /Operation:Analyze /Category:All /Action:Install /NonInteractive`
  - Winget ID: `HP.SupportAssistant`

### 4. OEM Lisans ve Servis Kodu Tespiti
Anakart BIOS/UEFI tablosundan OEM Service Tag / Serial Number ve MSDM Orijinal Windows Ürün Anahtarı otomatik okunur.
