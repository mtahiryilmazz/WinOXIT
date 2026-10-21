# 🌐 WinoxIT Universal Offline NIC (Ağ Kartı Sürücü Paketi)
Bu dizin, format sonrası interneti olmayan cihazlara **sıfır internet gereksinimiyle** 5 saniye içinde sürücü enjekte etmek üzere tasarlanmıştır.

## Desteklenen Üreticiler ve Çipler:
- **Intel:** I217, I218, I219, I225-V, Wi-Fi 6 AX200/AX201/AX210/AX211/BE200 WHQL INF
- **Realtek:** RTL8111, RTL8125 (2.5GbE), RTL8821CE, RTL8822CE, RTL8852BE
- **Broadcom / Qualcomm Atheros / MediaTek:** MT7921, MT7922, QCA6174, QCA9377

## Kurulum Motoru:
WinoxIT, `pnputil.exe /add-driver "*.inf" /subdirs /install` komutu ile sürücüleri Windows DriverStore içerisine doğrudan enjekte eder.
