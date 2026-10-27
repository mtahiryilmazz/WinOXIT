using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinoxIT.Services
{
    /// <summary>
    /// High-performance P/Invoke bridge to the native Rust engine (winoxit_core.dll).
    /// Provides ultra-fast SendARP, NetBIOS UDP 137 queries, TCP port scans, and MAC OUI vendor resolution.
    /// If winoxit_core.dll is missing or fails to load, gracefully falls back to managed C# routines.
    /// </summary>
    public static class RustCoreBridge
    {
        private const string DllName = "winoxit_core.dll";
        private static bool? _isAvailable = null;
        private static readonly object _lock = new();

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        [DllImport(DllName, EntryPoint = "winoxit_core_version", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr WinoxitCoreVersionNative();

        [DllImport(DllName, EntryPoint = "winoxit_probe_host_fast", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int WinoxitProbeHostFastNative(
            string ip,
            [Out] byte[] outBuf,
            uint maxLen);

        [DllImport(DllName, EntryPoint = "winoxit_lookup_vendor", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int WinoxitLookupVendorNative(
            string mac,
            [Out] byte[] outBuf,
            uint maxLen);

        /// <summary>
        /// Verifies whether the native winoxit_core.dll is loaded and responsive.
        /// </summary>
        public static bool IsAvailable()
        {
            if (_isAvailable.HasValue) return _isAvailable.Value;

            lock (_lock)
            {
                if (_isAvailable.HasValue) return _isAvailable.Value;

                try
                {
                    var ptr = WinoxitCoreVersionNative();
                    if (ptr != IntPtr.Zero)
                    {
                        var ver = Marshal.PtrToStringAnsi(ptr);
                        _isAvailable = !string.IsNullOrEmpty(ver);
                    }
                    else
                    {
                        _isAvailable = false;
                    }
                }
                catch
                {
                    _isAvailable = false;
                }

                return _isAvailable.Value;
            }
        }

        /// <summary>
        /// Reads the native core version string.
        /// </summary>
        public static string GetVersion()
        {
            if (!IsAvailable()) return "Managed C# Engine (Rust Core Unavailable)";
            try
            {
                var ptr = WinoxitCoreVersionNative();
                return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) ?? "Unknown" : "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        /// <summary>
        /// Probes a single host through native Rust core:
        /// Win32 SendARP -> OUI vendor lookup -> UDP 137 NetBIOS query -> TCP multi-port probe -> Device classification.
        /// Returns parsed LanHostNode or null if native call fails.
        /// </summary>
        public static LanHostNode? ProbeHostFast(string ip)
        {
            if (!IsAvailable()) return null;

            try
            {
                byte[] buffer = new byte[4096];
                int code = WinoxitProbeHostFastNative(ip, buffer, (uint)buffer.Length);
                if (code != 0) return null;

                int len = Array.IndexOf(buffer, (byte)0);
                if (len < 0) len = buffer.Length;

                string json = Encoding.UTF8.GetString(buffer, 0, len);
                if (string.IsNullOrWhiteSpace(json)) return null;

                var raw = JsonSerializer.Deserialize<RustHostProbeResult>(json);
                if (raw == null) return null;

                return new LanHostNode
                {
                    IPAddress = raw.ip_address ?? ip,
                    Hostname = string.IsNullOrEmpty(raw.hostname) ? "-" : raw.hostname,
                    NetBiosName = string.IsNullOrEmpty(raw.netbios_name) ? "-" : raw.netbios_name,
                    Workgroup = string.IsNullOrEmpty(raw.workgroup) ? "-" : raw.workgroup,
                    MACAddress = string.IsNullOrEmpty(raw.mac_address) ? "-" : raw.mac_address,
                    Vendor = string.IsNullOrEmpty(raw.vendor) ? "-" : raw.vendor,
                    Status = string.IsNullOrEmpty(raw.status) ? "⚪ Boşta (Kullanılabilir)" : raw.status,
                    StatusCode = string.IsNullOrEmpty(raw.status_code) ? "Idle" : raw.status_code,
                    Latency = string.IsNullOrEmpty(raw.latency) ? "-" : raw.latency,
                    DeviceType = string.IsNullOrEmpty(raw.device_type) ? "Serbest IP" : raw.device_type,
                    OpenPorts = string.IsNullOrEmpty(raw.open_ports) ? "-" : raw.open_ports,
                    HasSmb = raw.has_smb,
                    HasRdp = raw.has_rdp,
                    HasHttp = raw.has_http,
                    HasSsh = raw.has_ssh,
                    IsOnline = raw.is_online
                };
            }
            catch
            {
                return null;
            }
        }

        private class RustHostProbeResult
        {
            [JsonPropertyName("ip_address")]
            public string? ip_address { get; set; }

            [JsonPropertyName("hostname")]
            public string? hostname { get; set; }

            [JsonPropertyName("netbios_name")]
            public string? netbios_name { get; set; }

            [JsonPropertyName("workgroup")]
            public string? workgroup { get; set; }

            [JsonPropertyName("mac_address")]
            public string? mac_address { get; set; }

            [JsonPropertyName("vendor")]
            public string? vendor { get; set; }

            [JsonPropertyName("status")]
            public string? status { get; set; }

            [JsonPropertyName("status_code")]
            public string? status_code { get; set; }

            [JsonPropertyName("latency")]
            public string? latency { get; set; }

            [JsonPropertyName("device_type")]
            public string? device_type { get; set; }

            [JsonPropertyName("open_ports")]
            public string? open_ports { get; set; }

            [JsonPropertyName("has_smb")]
            public bool has_smb { get; set; }

            [JsonPropertyName("has_rdp")]
            public bool has_rdp { get; set; }

            [JsonPropertyName("has_http")]
            public bool has_http { get; set; }

            [JsonPropertyName("has_ssh")]
            public bool has_ssh { get; set; }

            [JsonPropertyName("is_online")]
            public bool is_online { get; set; }
        }
    }
}
