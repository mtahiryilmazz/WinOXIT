# WinoxIT Consolidation & Redesign Checklist

## Phase 1: Foundation & Safety
- [x] Task 1.1: Verify Windows System Restore Point creation in WinoxIT (`Checkpoint-Computer` in `NativeSystemEngine.cs`).
- [x] Task 1.2: Copy repository brand icon `branding/dist/v3-concept-1-symbol-app-icon-64.png` to `gui/logo.png` and configure as Resource in `WinoxIT.csproj`.

## Phase 2: Navigation & Hub Consolidation
- [x] Task 2.1: Remove hamburger button (`☰`) and implement single clickable brand logo toggle in sidebar header.
- [x] Task 2.2: Consolidate 15 navigation buttons down to 5 Category Hubs in `gui/MainWindow.xaml`:
  - 📊 Kontrol Merkezi & Ağ Filosu (Genel Bakış, Ağ Filosu, Web Kokpiti, ISO Stüdyosu)
  - 🛡️ Donanım, Sürücüler & Yazıcı (HWID Anti-Tamper, Evrensel Sürücüler, 0x11b/0x709 Yazıcı Onarımı)
  - 🚀 Format Sonrası Kurulum & İnce Ayarlar (27+ Katılımsız Paket, Sordum/Nilesoft Tarzı Sağ Tuş Araçları, Debloat)
  - 🔧 Sistem Onarımı & Bakım (Temizlik, Ağ/DNS Onarımı, SFC/DISM, RAM Monitörü, Başlangıç)
  - 🤖 Yapay Zekâ Teşhis & Web Çözüm (Microsoft Learn/Support ilk, Teknoloji Forumları ikinci, Onaylı Rozet)
- [x] Task 2.3: Wire category sub-navigation pills (`FluentSubNavPill`), panel switching logic, and compact icon modes in `gui/MainWindow.xaml.cs`.

## Phase 3: Post-Format Catalog, Reversible Context Menu & AI Multi-Tier Hierarchy
- [x] Task 3.1: Expand post-format software catalog to 27 essential packages with "Tümünü Seç" / "Seçimi Temizle" and silent winget installer.
- [x] Task 3.2: Implement Sordum Easy Context Menu & Nilesoft Shell style reversible Windows tools:
  - Klasik Windows 10 Sağ Tuş Menüsü (Uygula & Geri Al)
  - Klasik Fotoğraf Görüntüleyici / PhotoViewer.dll (Uygula & Geri Al)
  - Sahipliği Al / Take Ownership (Uygula & Geri Al)
  - Yönetici CMD & PowerShell Aç (Uygula & Geri Al)
  - Yazdırma Biriktiricisini Yeniden Başlat (Uygula & Geri Al)
  - Gelişmiş Güç Menüsü: Kapat / Yeniden Başlat / UEFI / Güvenli Mod (Uygula & Geri Al)
  - Bilinen Dosya Uzantılarını ve Gizli Dosyaları Göster (Uygula & Geri Al)
  - Reversible Debloat: Copilot/Recall, Telemetri, Bing Web Araması (Uygula & Geri Al)
- [x] Task 3.3: Enlarge AI query input box to 2 lines (`MinHeight="64"`, `AcceptsReturn="True"`, `TextWrapping="Wrap"`).
- [x] Task 3.4: Upgrade AI Search Engine:
  - Kaynak Hiyerarşisi: Tier 1 (learn.microsoft.com, support.microsoft.com) -> Tier 2 (SuperUser, ElevenForum, TenForums, Spiceworks).
  - Onaylı Çözüm Rozeti: `BadgeAiVerified` ("✅ ONAYLI RESMİ ÇÖZÜM" / "✅ ONAYLI TOPLULUK ÇÖZÜMÜ") ve `TxtAiSourceTier`.

## Phase 4: Web Dashboard Redesign
- [x] Task 4.1: Clean up and redesign `web/dashboard.html` with modern Fluent Dark / Glassmorphism UI, real-time KPI cards, and responsive layout.

## Phase 5: Verification & Publish
- [x] Task 5.1: Build Release .NET 8 WPF (`dotnet build gui\WinoxIT.csproj -c Release` -> 0 Uyarı, 0 Hata).
- [x] Task 5.2: Publish single-file executable to `publish/WinoxIT.exe`.
- [x] Task 5.3: Verify runtime window handle and clean state.
- [x] Task 5.4: Git commit and push to `origin/main`.
