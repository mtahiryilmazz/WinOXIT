use serde::{Deserialize, Serialize};
use std::net::{Ipv4Addr, SocketAddr, TcpStream, UdpSocket};
use std::time::Duration;
use super::run_powershell;
use super::printer::OperationResult;

#[link(name = "iphlpapi")]
extern "system" {
    fn SendARP(dest_ip: u32, src_ip: u32, mac_addr: *mut u8, phy_addr_len: *mut u32) -> u32;
}

#[derive(Debug, Serialize, Deserialize, Clone)]
pub struct HostProbeResult {
    pub ip_address: String,
    pub hostname: String,
    pub netbios_name: String,
    pub workgroup: String,
    pub mac_address: String,
    pub vendor: String,
    pub status: String,
    pub status_code: String, // Online, Firewall, Idle
    pub latency: String,
    pub device_type: String,
    pub open_ports: String,
    pub has_smb: bool,
    pub has_rdp: bool,
    pub has_http: bool,
    pub has_ssh: bool,
    pub is_online: bool,
}

/// SendARP Layer-2 Win32 check
pub fn send_arp(ip: Ipv4Addr) -> Option<String> {
    let mut mac_bytes = [0u8; 6];
    let mut len: u32 = 6;
    let octets = ip.octets();
    let dest_ip = u32::from_le_bytes(octets);

    unsafe {
        let ret = SendARP(dest_ip, 0, mac_bytes.as_mut_ptr(), &mut len);
        if ret == 0 && len == 6 {
            Some(format!(
                "{:02X}:{:02X}:{:02X}:{:02X}:{:02X}:{:02X}",
                mac_bytes[0], mac_bytes[1], mac_bytes[2], mac_bytes[3], mac_bytes[4], mac_bytes[5]
            ))
        } else {
            None
        }
    }
}

/// Fast NetBIOS Node Status query over UDP 137 (RFC 1001/1002)
pub fn query_netbios(ip: &str, timeout_ms: u64) -> (String, String) {
    let socket = match UdpSocket::bind("0.0.0.0:0") {
        Ok(s) => s,
        Err(_) => return (String::new(), String::new()),
    };

    let timeout = Duration::from_millis(timeout_ms);
    let _ = socket.set_read_timeout(Some(timeout));
    let _ = socket.set_write_timeout(Some(timeout));

    let target_addr: SocketAddr = match format!("{}:137", ip).parse() {
        Ok(a) => a,
        Err(_) => return (String::new(), String::new()),
    };

    // Standard RFC 1001 NetBIOS node status request
    let query_pkt: [u8; 50] = [
        0x80, 0x94, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x20, 0x43, 0x4B, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41,
        0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41,
        0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x00, 0x00, 0x21,
        0x00, 0x01,
    ];

    if socket.send_to(&query_pkt, target_addr).is_err() {
        return (String::new(), String::new());
    }

    let mut buf = [0u8; 512];
    match socket.recv_from(&mut buf) {
        Ok((amt, _)) if amt > 56 => {
            let num_names = buf[56] as usize;
            let mut comp_name = String::new();
            let mut workgroup = String::new();

            let mut offset = 57;
            for _ in 0..num_names {
                if offset + 18 > amt {
                    break;
                }
                let raw_name = String::from_utf8_lossy(&buf[offset..offset + 15])
                    .trim()
                    .to_string();
                let name_type = buf[offset + 15];
                let flags = ((buf[offset + 16] as u16) << 8) | (buf[offset + 17] as u16);
                let is_group = (flags & 0x0080) != 0;

                if is_group && workgroup.is_empty() {
                    workgroup = raw_name;
                } else if !is_group && comp_name.is_empty() && name_type == 0x00 {
                    comp_name = raw_name;
                }
                offset += 18;
            }

            (comp_name, workgroup)
        }
        _ => (String::new(), String::new()),
    }
}

/// Fast TCP port prober
pub fn probe_tcp_ports(ip: &str, ports: &[u16], timeout_ms: u64) -> Vec<u16> {
    let timeout = Duration::from_millis(timeout_ms);
    let mut open = Vec::new();

    for &port in ports {
        if let Ok(addr) = format!("{}:{}", ip, port).parse::<SocketAddr>() {
            if TcpStream::connect_timeout(&addr, timeout).is_ok() {
                open.push(port);
            }
        }
    }
    open
}

/// Lookup MAC OUI Vendor
pub fn lookup_mac_vendor(mac: &str) -> &'static str {
    if mac.len() < 8 {
        return "Bilinmiyor";
    }
    let p = mac[..8].to_uppercase();
    let prefix = p.as_str();

    // IP Phones
    if prefix.starts_with("00:15:65") || prefix.starts_with("80:5E:C0") || prefix.starts_with("00:26:8B") { return "Yealink"; }
    if prefix.starts_with("00:0B:82") { return "Grandstream"; }
    if prefix.starts_with("00:04:F2") || prefix.starts_with("00:10:94") || prefix.starts_with("48:25:67") { return "Polycom"; }

    // Firewalls
    if prefix.starts_with("00:09:0F") || prefix.starts_with("70:4C:A5") || prefix.starts_with("90:6C:AC") { return "Fortinet"; }
    if prefix.starts_with("00:1B:17") || prefix.starts_with("08:30:6B") || prefix.starts_with("D4:1D:71") { return "Palo Alto Networks"; }
    if prefix.starts_with("00:1A:8C") || prefix.starts_with("7C:5A:1C") { return "Sophos"; }

    // Switches / Routers
    if prefix.starts_with("00:00:0C") || prefix.starts_with("00:01:42") || prefix.starts_with("F4:4B:2A") || prefix.starts_with("70:69:79") { return "Cisco"; }
    if prefix.starts_with("48:8F:5A") || prefix.starts_with("64:D1:54") || prefix.starts_with("CC:2D:E0") || prefix.starts_with("D4:CA:6D") { return "MikroTik"; }
    if prefix.starts_with("00:27:22") || prefix.starts_with("74:83:C2") || prefix.starts_with("F0:9F:C2") { return "Ubiquiti UniFi"; }
    if prefix.starts_with("50:3E:AA") || prefix.starts_with("74:56:3C") || prefix.starts_with("F4:EC:38") || prefix.starts_with("AC:84:C6") || prefix.starts_with("C0:06:C3") { return "TP-Link"; }
    if prefix.starts_with("EC:43:F6") || prefix.starts_with("28:28:5D") { return "Keenetic"; }
    if prefix.starts_with("00:13:49") || prefix.starts_with("50:67:AE") { return "Zyxel"; }
    if prefix.starts_with("00:09:5B") || prefix.starts_with("A0:04:60") || prefix.starts_with("C4:04:15") { return "Netgear"; }
    if prefix.starts_with("00:15:F2") || prefix.starts_with("18:31:BF") || prefix.starts_with("AC:22:0B") { return "ASUS"; }

    // Enterprise / Servers
    if prefix.starts_with("00:14:22") || prefix.starts_with("14:FE:B5") || prefix.starts_with("B8:2A:72") || prefix.starts_with("F8:BC:12") { return "Dell Technologies"; }
    if prefix.starts_with("00:01:E6") || prefix.starts_with("28:92:4A") || prefix.starts_with("3C:D9:2B") || prefix.starts_with("C4:34:6B") { return "HPE / HP"; }
    if prefix.starts_with("00:25:90") || prefix.starts_with("00:30:48") || prefix.starts_with("AC:1F:6B") { return "Supermicro"; }

    // Printers
    if prefix.starts_with("00:00:85") || prefix.starts_with("70:8A:09") || prefix.starts_with("AC:64:17") { return "Canon"; }
    if prefix.starts_with("00:00:48") || prefix.starts_with("44:D2:44") || prefix.starts_with("AC:18:26") { return "Epson"; }
    if prefix.starts_with("00:80:77") || prefix.starts_with("30:05:5C") || prefix.starts_with("80:69:1A") { return "Brother"; }
    if prefix.starts_with("00:00:AA") || prefix.starts_with("00:08:74") { return "Xerox"; }
    if prefix.starts_with("00:00:87") || prefix.starts_with("40:B0:34") { return "Kyocera"; }

    // Storage / NAS
    if prefix.starts_with("00:11:32") { return "Synology NAS"; }
    if prefix.starts_with("00:08:9B") || prefix.starts_with("24:5E:BE") { return "QNAP NAS"; }

    // Virtualization
    if prefix.starts_with("00:05:69") || prefix.starts_with("00:0C:29") || prefix.starts_with("00:50:56") { return "VMware"; }
    if prefix.starts_with("08:00:27") { return "Oracle VirtualBox"; }

    // Mobile / Smart
    if prefix.starts_with("F4:0F:24") || prefix.starts_with("BC:52:B7") || prefix.starts_with("3C:D0:F8") || prefix.starts_with("A4:C3:61") || prefix.starts_with("88:66:5A") { return "Apple"; }
    if prefix.starts_with("08:D4:2B") || prefix.starts_with("34:23:87") || prefix.starts_with("84:25:DB") || prefix.starts_with("FC:03:9F") { return "Samsung"; }
    if prefix.starts_with("14:F6:5A") || prefix.starts_with("38:A4:ED") || prefix.starts_with("AC:C1:EE") { return "Xiaomi"; }
    if prefix.starts_with("00:1E:10") || prefix.starts_with("48:46:FB") || prefix.starts_with("E8:CD:2D") { return "Huawei"; }

    // IoT / Embedded
    if prefix.starts_with("18:FE:34") || prefix.starts_with("24:0A:C4") || prefix.starts_with("30:AE:A4") || prefix.starts_with("AC:67:B2") { return "Espressif (ESP)"; }
    if prefix.starts_with("B8:27:EB") || prefix.starts_with("DC:A6:32") || prefix.starts_with("E4:5F:01") { return "Raspberry Pi"; }

    // NIC
    if prefix.starts_with("00:02:B3") || prefix.starts_with("00:15:17") || prefix.starts_with("00:1E:67") { return "Intel"; }
    if prefix.starts_with("00:E0:4C") || prefix.starts_with("52:54:00") { return "Realtek"; }

    "Ağ Kartı"
}

/// Classify device based on IP, MAC, Vendor, and Open Ports
pub fn classify_device(
    ip: Ipv4Addr,
    vendor: &str,
    hostname: &str,
    ports: &[u16],
    has_smb: bool,
    has_rdp: bool,
) -> String {
    let host_lower = hostname.to_lowercase();
    let octets = ip.octets();
    let is_gateway = octets[3] == 1 || octets[3] == 254;

    // 1. IP Phone
    if vendor.contains("Yealink") || vendor.contains("Grandstream") || vendor.contains("Polycom")
        || ports.contains(&5060) || host_lower.contains("phone") || host_lower.contains("sip") || host_lower.contains("voip")
    {
        return format!("📞 IP Telefon (VoIP - {})", vendor);
    }

    // 2. Network Printer
    if ports.contains(&9100) || ports.contains(&515) || ports.contains(&631)
        || vendor.contains("Canon") || vendor.contains("Epson") || vendor.contains("Brother")
        || vendor.contains("Xerox") || vendor.contains("Kyocera")
        || host_lower.contains("printer") || host_lower.contains("yazici") || host_lower.contains("canon")
    {
        return format!("🖨️ Ağ Yazıcısı ({})", vendor);
    }

    // 3. Hardware Firewall
    if vendor.contains("Fortinet") || vendor.contains("Palo Alto") || vendor.contains("Sophos")
        || host_lower.contains("firewall") || host_lower.contains("utm")
    {
        return format!("🛡️ Donanım Güvenlik Duvarı ({})", vendor);
    }

    // 4. NAS Storage
    if vendor.contains("Synology") || vendor.contains("QNAP") || host_lower.contains("nas") || host_lower.contains("truenas") {
        return format!("💾 NAS Depolama Sunucusu ({})", vendor);
    }

    // 5. Virtual Machine
    if vendor.contains("VMware") || vendor.contains("VirtualBox") || host_lower.contains("vmware") || host_lower.contains("proxmox") {
        return format!("📦 Sanal Makine / VM ({})", vendor);
    }

    // 6. Router / Modem
    if is_gateway || vendor.contains("Keenetic") || vendor.contains("ASUS") || vendor.contains("Netgear") || vendor.contains("Zyxel")
        || (vendor.contains("TP-Link") && (ports.contains(&80) || ports.contains(&443)))
    {
        if is_gateway || ports.contains(&80) || ports.contains(&443) {
            return format!("🌐 Router / Modem / Gateway ({})", vendor);
        } else {
            return format!("🔀 Dağıtıcı / Switch ({})", vendor);
        }
    }

    // 7. Managed Switch
    if vendor.contains("Cisco") || vendor.contains("MikroTik") || vendor.contains("Ubiquiti") || host_lower.contains("switch") {
        return format!("🔀 Yönetilebilir Switch ({})", vendor);
    }

    // 8. Mobile Devices
    if vendor.contains("Apple") || vendor.contains("Samsung") || vendor.contains("Xiaomi") || vendor.contains("Huawei") {
        if !has_smb && !has_rdp && !ports.contains(&22) && !ports.contains(&80) {
            return format!("📱 Akıllı Telefon / Mobil ({})", vendor);
        }
    }

    // 9. IoT
    if vendor.contains("Espressif") || vendor.contains("Raspberry") || host_lower.contains("esp") || host_lower.contains("smart") {
        return format!("⚡ IoT / Akıllı Cihaz ({})", vendor);
    }

    // 10. Server
    if vendor.contains("Supermicro") || (vendor.contains("Dell") && (ports.contains(&22) || ports.contains(&3389)))
        || host_lower.contains("server") || host_lower.contains("dc-") || host_lower.contains("srv")
    {
        return format!("🖥️ Sunucu / Server ({})", vendor);
    }

    // 11. Windows PC
    if has_smb || has_rdp || host_lower.contains("desktop") || host_lower.contains("laptop") || host_lower.contains("pc") {
        let tag = if has_rdp { "RDP+SMB" } else { "SMB Aktif" };
        return format!("💻 Windows Bilgisayar ({} - {})", tag, vendor);
    }

    // 12. Linux
    if ports.contains(&22) {
        return format!("🐧 Linux / Unix Cihaz ({})", vendor);
    }

    format!("📡 Ağ Cihazı ({})", vendor)
}

/// Fast single host probe combining SendARP, NetBIOS, TCP Probing, and Classification
pub fn probe_host_fast(ip_str: &str) -> HostProbeResult {
    let mut result = HostProbeResult {
        ip_address: ip_str.to_string(),
        hostname: "-".to_string(),
        netbios_name: "-".to_string(),
        workgroup: "-".to_string(),
        mac_address: "-".to_string(),
        vendor: "-".to_string(),
        status: "⚪ Boşta (Kullanılabilir)".to_string(),
        status_code: "Idle".to_string(),
        latency: "-".to_string(),
        device_type: "Serbest IP".to_string(),
        open_ports: "-".to_string(),
        has_smb: false,
        has_rdp: false,
        has_http: false,
        has_ssh: false,
        is_online: false,
    };

    let ipv4: Ipv4Addr = match ip_str.parse() {
        Ok(addr) => addr,
        Err(_) => return result,
    };

    // Stage 1: SendARP check (Layer 2, < 1ms)
    let mac_opt = send_arp(ipv4);
    if mac_opt.is_none() {
        // Host is not answering ARP on local subnet -> immediately return Idle
        return result;
    }

    let mac = mac_opt.unwrap();
    result.is_online = true;
    result.status = "🟢 Açık (Online)".to_string();
    result.status_code = "Online".to_string();
    result.latency = "1 ms (ARP)".to_string();
    result.mac_address = mac.clone();

    // Stage 2: Vendor Lookup
    let vendor = lookup_mac_vendor(&mac);
    result.vendor = vendor.to_string();

    // Stage 3: NetBIOS Node Status query (UDP 137)
    let (nb_name, workgroup) = query_netbios(ip_str, 120);
    if !nb_name.is_empty() {
        result.netbios_name = nb_name.clone();
        result.hostname = nb_name;
    }
    if !workgroup.is_empty() {
        result.workgroup = workgroup;
    }

    // Stage 4: Fast TCP port test (445, 3389, 80, 443, 22, 5060, 9100)
    let test_ports = [445, 3389, 80, 443, 22, 5060, 9100];
    let open_ports = probe_tcp_ports(ip_str, &test_ports, 70);

    result.has_smb = open_ports.contains(&445);
    result.has_rdp = open_ports.contains(&3389);
    result.has_http = open_ports.contains(&80) || open_ports.contains(&443);
    result.has_ssh = open_ports.contains(&22);

    if !open_ports.is_empty() {
        result.open_ports = open_ports.iter().map(|p| p.to_string()).collect::<Vec<_>>().join(", ");
    }

    // Stage 5: Classification
    result.device_type = classify_device(ipv4, vendor, &result.hostname, &open_ports, result.has_smb, result.has_rdp);

    result
}

/// Flush local Windows DNS resolver cache
pub fn flush_dns() -> OperationResult {
    let (ok, stdout, stderr) = run_powershell("ipconfig /flushdns");
    OperationResult {
        success: ok,
        message: if ok { stdout } else { stderr },
    }
}

/// Reset Windows Winsock catalog
pub fn reset_winsock() -> OperationResult {
    let (ok, stdout, stderr) = run_powershell("netsh winsock reset");
    OperationResult {
        success: ok,
        message: if ok { stdout } else { stderr },
    }
}

/// Rebuild TCP/IP network stack
pub fn reset_tcpip() -> OperationResult {
    let (ok, stdout, stderr) = run_powershell("netsh int ip reset");
    OperationResult {
        success: ok,
        message: if ok { stdout } else { stderr },
    }
}
