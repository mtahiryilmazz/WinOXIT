pub mod modules;

use std::ffi::CStr;
use std::os::raw::c_char;
use std::ptr;
use modules::network;

/// Returns version string of WinoxIT Native Rust Core
#[no_mangle]
pub extern "C" fn winoxit_core_version() -> *const c_char {
    static VERSION: &[u8] = b"WinoxIT Native Core v2.1-rust-x64\0";
    VERSION.as_ptr() as *const c_char
}

/// Helper function to safely copy a Rust string into a caller-provided C char buffer
unsafe fn copy_to_c_buf(src: &str, out_buf: *mut c_char, max_len: u32) -> i32 {
    if out_buf.is_null() || max_len == 0 {
        return -1;
    }
    let bytes = src.as_bytes();
    let copy_len = std::cmp::min(bytes.len(), (max_len - 1) as usize);
    ptr::copy_nonoverlapping(bytes.as_ptr() as *const c_char, out_buf, copy_len);
    *out_buf.add(copy_len) = 0; // Null-terminator
    0
}

/// Fast probes a single host (SendARP, NetBIOS UDP 137, TCP 445/3389/80/443/etc., OUI vendor, classification)
/// Outputs serialized JSON representation of HostProbeResult into out_buf.
/// Returns 0 on success, negative error code on failure.
#[no_mangle]
pub extern "C" fn winoxit_probe_host_fast(ip_ptr: *const c_char, out_buf: *mut c_char, max_len: u32) -> i32 {
    if ip_ptr.is_null() || out_buf.is_null() || max_len == 0 {
        return -1;
    }

    let ip_str = match unsafe { CStr::from_ptr(ip_ptr) }.to_str() {
        Ok(s) => s.trim(),
        Err(_) => return -2,
    };

    let result = network::probe_host_fast(ip_str);
    let json_str = match serde_json::to_string(&result) {
        Ok(s) => s,
        Err(_) => return -3,
    };

    unsafe { copy_to_c_buf(&json_str, out_buf, max_len) }
}

/// Queries NetBIOS over UDP port 137 (RFC 1001/1002)
#[no_mangle]
pub extern "C" fn winoxit_query_netbios(
    ip_ptr: *const c_char,
    out_name: *mut c_char,
    max_name: u32,
    out_group: *mut c_char,
    max_group: u32,
) -> i32 {
    if ip_ptr.is_null() {
        return -1;
    }

    let ip_str = match unsafe { CStr::from_ptr(ip_ptr) }.to_str() {
        Ok(s) => s.trim(),
        Err(_) => return -2,
    };

    let (nb_name, workgroup) = network::query_netbios(ip_str, 120);

    unsafe {
        if !out_name.is_null() && max_name > 0 {
            let _ = copy_to_c_buf(&nb_name, out_name, max_name);
        }
        if !out_group.is_null() && max_group > 0 {
            let _ = copy_to_c_buf(&workgroup, out_group, max_group);
        }
    }

    0
}

/// Fast OUI prefix lookup for MAC vendor
#[no_mangle]
pub extern "C" fn winoxit_lookup_vendor(mac_ptr: *const c_char, out_buf: *mut c_char, max_len: u32) -> i32 {
    if mac_ptr.is_null() || out_buf.is_null() || max_len == 0 {
        return -1;
    }

    let mac_str = match unsafe { CStr::from_ptr(mac_ptr) }.to_str() {
        Ok(s) => s.trim(),
        Err(_) => return -2,
    };

    let vendor = network::lookup_mac_vendor(mac_str);
    unsafe { copy_to_c_buf(vendor, out_buf, max_len) }
}
