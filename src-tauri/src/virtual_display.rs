use serde::Serialize;
use std::ffi::{c_void, CStr};
use std::ptr;
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::Duration;
use tauri::{command, State};

#[cfg(windows)]
mod win {
    pub use windows::core::{GUID, PCSTR};
    pub use windows::Win32::Devices::DeviceAndDriverInstallation::{
        CM_Get_DevNode_Status, SetupDiDestroyDeviceInfoList, SetupDiEnumDeviceInfo,
        SetupDiEnumDeviceInterfaces, SetupDiGetClassDevsA, SetupDiGetDeviceInterfaceDetailA,
        SetupDiGetDeviceRegistryPropertyA, CM_DEVNODE_STATUS_FLAGS, CM_PROB, CR_SUCCESS,
        DIGCF_DEVICEINTERFACE, DIGCF_PRESENT, DN_DRIVER_LOADED, DN_HAS_PROBLEM, DN_STARTED,
        SP_DEVICE_INTERFACE_DATA, SP_DEVICE_INTERFACE_DETAIL_DATA_A, SP_DEVINFO_DATA,
        SPDRP_HARDWAREID,
    };
    pub use windows::Win32::Foundation::{
        CloseHandle, GENERIC_READ, GENERIC_WRITE, HANDLE, HWND,
    };
    pub use windows::Win32::Storage::FileSystem::{
        CreateFileA, FILE_ATTRIBUTE_NORMAL, FILE_FLAGS_AND_ATTRIBUTES, FILE_FLAG_NO_BUFFERING,
        FILE_FLAG_OVERLAPPED, FILE_FLAG_WRITE_THROUGH, FILE_SHARE_MODE, FILE_SHARE_READ,
        FILE_SHARE_WRITE, OPEN_EXISTING,
    };
    pub use windows::Win32::System::IO::{DeviceIoControl, GetOverlappedResultEx, OVERLAPPED};
    pub use windows::Win32::System::Threading::CreateEventA;
}

#[cfg(windows)]
use win::*;

const VDD_ADAPTER_GUID: GUID = GUID::from_values(
    0x00b41627,
    0x04c4,
    0x429e,
    [0xa2, 0x6e, 0x02, 0x65, 0xcf, 0x50, 0xc8, 0xfa],
);

const VDD_CLASS_GUID: GUID = GUID::from_values(
    0x4d36e968,
    0xe325,
    0x11ce,
    [0xbf, 0xc1, 0x08, 0x00, 0x2b, 0xe1, 0x03, 0x18],
);

const VDD_HARDWARE_ID: &str = "Root\\Parsec\\VDA";
const VDD_MAX_DISPLAYS: usize = 8;

const VDD_IOCTL_ADD: u32 = 0x0022e004;
const VDD_IOCTL_REMOVE: u32 = 0x0022a008;
const VDD_IOCTL_UPDATE: u32 = 0x0022a00c;

const PARSEC_VDD_SETUP_BYTES: &[u8] =
    include_bytes!(concat!(env!("CARGO_MANIFEST_DIR"), "/binaries/parsec-vdd-0.41.0.0.exe"));

#[derive(Debug, Clone, Serialize)]
pub struct VirtualDisplayStatus {
    pub installed: bool,
    pub running: bool,
    pub message: String,
}

pub struct VirtualDisplayState {
    vdd: Mutex<isize>,
    displays: Mutex<Vec<i32>>,
    updater_running: Mutex<bool>,
}

impl VirtualDisplayState {
    pub fn new() -> Self {
        Self {
            vdd: Mutex::new(0),
            displays: Mutex::new(Vec::new()),
            updater_running: Mutex::new(false),
        }
    }
}

#[cfg(windows)]
fn vdd_handle(state: &Arc<VirtualDisplayState>) -> HANDLE {
    let v = *state.vdd.lock().unwrap();
    if v == 0 {
        HANDLE(ptr::null_mut())
    } else {
        HANDLE(v as *mut c_void)
    }
}

#[cfg(windows)]
fn set_vdd_handle(state: &Arc<VirtualDisplayState>, handle: HANDLE) {
    let v = if handle.is_invalid() { 0 } else { handle.0 as isize };
    *state.vdd.lock().unwrap() = v;
}

#[command]
pub fn get_virtual_display_status() -> VirtualDisplayStatus {
    #[cfg(windows)]
    {
        let status = query_device_status(&VDD_CLASS_GUID, VDD_HARDWARE_ID);
        let (installed, running, message) = match status {
            DeviceStatus::Ok => (true, true, "Parsec VDD 运行正常".to_string()),
            DeviceStatus::Disabled => (true, false, "Parsec VDD 已禁用".to_string()),
            DeviceStatus::RestartRequired => (true, false, "Parsec VDD 需要重启".to_string()),
            DeviceStatus::DriverError => (true, false, "Parsec VDD 驱动错误".to_string()),
            DeviceStatus::NotInstalled => (false, false, "Parsec VDD 未安装".to_string()),
            _ => (true, false, "Parsec VDD 状态异常".to_string()),
        };
        VirtualDisplayStatus {
            installed,
            running,
            message,
        }
    }
    #[cfg(not(windows))]
    {
        VirtualDisplayStatus {
            installed: false,
            running: false,
            message: "仅支持 Windows".to_string(),
        }
    }
}

#[command]
pub fn install_virtual_display_driver() -> Result<String, String> {
    #[cfg(windows)]
    {
        let temp_path = std::env::temp_dir().join("parsec-vdd-setup.exe");
        std::fs::write(&temp_path, PARSEC_VDD_SETUP_BYTES)
            .map_err(|e| format!("解压安装包失败: {e}"))?;

        match std::process::Command::new(&temp_path).arg("/S").status() {
            Ok(status) => {
                if status.success() {
                    Ok("Parsec VDD 驱动安装成功，请刷新状态查看。".to_string())
                } else {
                    Err(format!("安装程序退出码: {status:?}"))
                }
            }
            Err(e) => Err(format!("运行安装程序失败: {e}")),
        }
    }
    #[cfg(not(windows))]
    {
        Err("仅支持 Windows".to_string())
    }
}

#[command]
pub fn list_virtual_displays(state: State<Arc<VirtualDisplayState>>) -> Vec<i32> {
    state.displays.lock().unwrap().clone()
}

#[command]
pub fn add_virtual_display(state: State<Arc<VirtualDisplayState>>) -> Result<i32, String> {
    #[cfg(windows)]
    {
        ensure_vdd_open(&state)?;
        start_updater_thread(&state);

        let mut displays = state.displays.lock().unwrap();
        if displays.len() >= VDD_MAX_DISPLAYS {
            return Err("已达到最大虚拟显示器数量 (8)".to_string());
        }

        let vdd = vdd_handle(&state);
        let index = vdd_add_display(vdd);
        if index < 0 || index as usize >= VDD_MAX_DISPLAYS {
            return Err("添加虚拟显示器失败".to_string());
        }

        if displays.len() <= index as usize {
            displays.resize(index as usize + 1, -1);
        }
        displays[index as usize] = index;

        Ok(index)
    }
    #[cfg(not(windows))]
    {
        let _ = state;
        Err("仅支持 Windows".to_string())
    }
}

#[command]
pub fn remove_virtual_display(
    state: State<Arc<VirtualDisplayState>>,
    index: i32,
) -> Result<(), String> {
    #[cfg(windows)]
    {
        ensure_vdd_open(&state)?;

        let vdd = vdd_handle(&state);
        vdd_remove_display(vdd, index);

        let mut displays = state.displays.lock().unwrap();
        displays.retain(|&x| x != index);

        if displays.is_empty() {
            *state.updater_running.lock().unwrap() = false;
            close_device_handle(vdd);
            set_vdd_handle(&state, HANDLE(ptr::null_mut()));
        }

        Ok(())
    }
    #[cfg(not(windows))]
    {
        let _ = (state, index);
        Err("仅支持 Windows".to_string())
    }
}

#[cfg(windows)]
fn start_updater_thread(state: &Arc<VirtualDisplayState>) {
    let mut running = state.updater_running.lock().unwrap();
    if *running {
        return;
    }
    *running = true;
    drop(running);

    let state_clone = Arc::clone(state);
    thread::spawn(move || {
        loop {
            {
                let r = *state_clone.updater_running.lock().unwrap();
                if !r {
                    break;
                }
                let vdd = vdd_handle(&state_clone);
                if !vdd.is_invalid() {
                    vdd_update(vdd);
                }
            }
            thread::sleep(Duration::from_millis(100));
        }
    });
}

#[cfg(windows)]
fn ensure_vdd_open(state: &Arc<VirtualDisplayState>) -> Result<(), String> {
    let vdd = vdd_handle(state);
    if !vdd.is_invalid() {
        return Ok(());
    }

    let status = query_device_status(&VDD_CLASS_GUID, VDD_HARDWARE_ID);
    if status != DeviceStatus::Ok {
        return Err(format!("Parsec VDD 不可用: {:?}", status));
    }

    let handle = open_device_handle(&VDD_ADAPTER_GUID);
    if handle.is_invalid() {
        return Err("无法打开 Parsec VDD 设备句柄".to_string());
    }

    set_vdd_handle(state, handle);
    Ok(())
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[cfg(windows)]
enum DeviceStatus {
    Ok,
    Disabled,
    RestartRequired,
    DriverError,
    NotInstalled,
    Unknown,
}

#[cfg(windows)]
fn query_device_status(class_guid: &GUID, device_id: &str) -> DeviceStatus {
    let mut status = DeviceStatus::NotInstalled;

    unsafe {
        let dev_info = match SetupDiGetClassDevsA(
            Some(class_guid),
            PCSTR(ptr::null()),
            Some(HWND(ptr::null_mut())),
            DIGCF_PRESENT,
        ) {
            Ok(info) => info,
            Err(_) => return status,
        };

        let mut dev_info_data = SP_DEVINFO_DATA {
            cbSize: std::mem::size_of::<SP_DEVINFO_DATA>() as u32,
            ClassGuid: GUID::default(),
            DevInst: 0,
            Reserved: 0,
        };

        let mut found_prop = false;
        let mut device_index = 0u32;

        while SetupDiEnumDeviceInfo(dev_info, device_index, &mut dev_info_data).is_ok() {
            let mut required_size = 0u32;
            let _ = SetupDiGetDeviceRegistryPropertyA(
                dev_info,
                &dev_info_data,
                SPDRP_HARDWAREID,
                None,
                None,
                Some(&mut required_size),
            );

            if required_size > 0 {
                let mut prop_buffer = vec![0u8; required_size as usize];
                let mut reg_data_type = 0u32;

                if SetupDiGetDeviceRegistryPropertyA(
                    dev_info,
                    &dev_info_data,
                    SPDRP_HARDWAREID,
                    Some(&mut reg_data_type),
                    Some(&mut prop_buffer),
                    Some(&mut required_size),
                )
                .is_ok()
                {
                    if reg_data_type == 1 || reg_data_type == 7 {
                        let mut offset = 0usize;
                        loop {
                            let cp = prop_buffer[offset..].as_ptr() as *const i8;
                            if cp.is_null() || *cp == 0 {
                                break;
                            }
                            let len = CStr::from_ptr(cp).to_bytes().len();
                            if offset + len >= prop_buffer.len() {
                                break;
                            }
                            let id = CStr::from_ptr(cp).to_string_lossy();
                            if id == device_id {
                                found_prop = true;
                                let mut dev_status = CM_DEVNODE_STATUS_FLAGS(0);
                                let mut problem_num = CM_PROB(0);

                                if CM_Get_DevNode_Status(
                                    &mut dev_status,
                                    &mut problem_num,
                                    dev_info_data.DevInst,
                                    0,
                                ) != CR_SUCCESS
                                {
                                    status = DeviceStatus::NotInstalled;
                                } else {
                                    status = parse_device_status(dev_status, problem_num);
                                }
                                break;
                            }
                            offset += len + 1;
                        }
                    }
                }
            }

            if found_prop {
                break;
            }
            device_index += 1;
        }

        let _ = SetupDiDestroyDeviceInfoList(dev_info);
    }

    status
}

#[cfg(windows)]
unsafe fn parse_device_status(
    dev_status: CM_DEVNODE_STATUS_FLAGS,
    problem_num: CM_PROB,
) -> DeviceStatus {
    use windows::Win32::Devices::DeviceAndDriverInstallation::{
        CM_PROB_DISABLED, CM_PROB_DISABLED_SERVICE, CM_PROB_FAILED_POST_START,
        CM_PROB_HARDWARE_DISABLED, CM_PROB_NEED_RESTART,
    };

    if (dev_status.0 & (DN_DRIVER_LOADED.0 | DN_STARTED.0)) != 0 {
        DeviceStatus::Ok
    } else if (dev_status.0 & DN_HAS_PROBLEM.0) != 0 {
        match problem_num {
            CM_PROB_NEED_RESTART => DeviceStatus::RestartRequired,
            CM_PROB_DISABLED | CM_PROB_HARDWARE_DISABLED => DeviceStatus::Disabled,
            CM_PROB_DISABLED_SERVICE => DeviceStatus::Disabled,
            _ => {
                if problem_num == CM_PROB_FAILED_POST_START {
                    DeviceStatus::DriverError
                } else {
                    DeviceStatus::Unknown
                }
            }
        }
    } else {
        DeviceStatus::Unknown
    }
}

#[cfg(windows)]
fn open_device_handle(interface_guid: &GUID) -> HANDLE {
    unsafe {
        let dev_info = match SetupDiGetClassDevsA(
            Some(interface_guid),
            PCSTR(ptr::null()),
            Some(HWND(ptr::null_mut())),
            DIGCF_PRESENT | DIGCF_DEVICEINTERFACE,
        ) {
            Ok(info) => info,
            Err(_) => return HANDLE(ptr::null_mut()),
        };

        let mut dev_interface = SP_DEVICE_INTERFACE_DATA {
            cbSize: std::mem::size_of::<SP_DEVICE_INTERFACE_DATA>() as u32,
            InterfaceClassGuid: GUID::default(),
            Flags: 0,
            Reserved: 0,
        };

        let mut i = 0u32;
        while SetupDiEnumDeviceInterfaces(dev_info, None, interface_guid, i, &mut dev_interface)
            .is_ok()
        {
            let mut detail_size = 0u32;
            let _ = SetupDiGetDeviceInterfaceDetailA(
                dev_info,
                &dev_interface,
                None,
                0,
                Some(&mut detail_size),
                None,
            );

            let layout =
                std::alloc::Layout::from_size_align(detail_size as usize, 8).unwrap_or_else(|_| {
                    std::alloc::Layout::from_size_align(1, 1).unwrap()
                });
            let detail = std::alloc::alloc(layout) as *mut SP_DEVICE_INTERFACE_DETAIL_DATA_A;
            if detail.is_null() {
                i += 1;
                continue;
            }
            (*detail).cbSize = std::mem::size_of::<SP_DEVICE_INTERFACE_DETAIL_DATA_A>() as u32;

            if SetupDiGetDeviceInterfaceDetailA(
                dev_info,
                &dev_interface,
                Some(detail),
                detail_size,
                Some(&mut detail_size),
                None,
            )
            .is_ok()
            {
                let path = CStr::from_ptr((*detail).DevicePath.as_ptr());
                let handle = CreateFileA(
                    PCSTR(path.as_ptr() as *const u8),
                    GENERIC_READ.0 | GENERIC_WRITE.0,
                    FILE_SHARE_MODE(FILE_SHARE_READ.0 | FILE_SHARE_WRITE.0),
                    None,
                    OPEN_EXISTING,
                    FILE_FLAGS_AND_ATTRIBUTES(
                        FILE_ATTRIBUTE_NORMAL.0
                            | FILE_FLAG_NO_BUFFERING.0
                            | FILE_FLAG_OVERLAPPED.0
                            | FILE_FLAG_WRITE_THROUGH.0,
                    ),
                    Some(HANDLE(ptr::null_mut())),
                );

                std::alloc::dealloc(detail as *mut u8, layout);

                if let Ok(h) = handle {
                    if !h.is_invalid() {
                        let _ = SetupDiDestroyDeviceInfoList(dev_info);
                        return h;
                    }
                }
            } else {
                std::alloc::dealloc(detail as *mut u8, layout);
            }

            i += 1;
        }

        let _ = SetupDiDestroyDeviceInfoList(dev_info);
    }

    HANDLE(ptr::null_mut())
}

#[cfg(windows)]
fn close_device_handle(handle: HANDLE) {
    if !handle.is_invalid() {
        unsafe {
            let _ = CloseHandle(handle);
        }
    }
}

#[cfg(windows)]
fn vdd_ioctl(vdd: HANDLE, code: u32, data: Option<&[u8]>) -> u32 {
    unsafe {
        if vdd.is_invalid() {
            return 0;
        }

        let mut in_buffer = [0u8; 32];
        if let Some(d) = data {
            let len = d.len().min(in_buffer.len());
            in_buffer[..len].copy_from_slice(&d[..len]);
        }

        let mut overlapped = OVERLAPPED::default();
        overlapped.hEvent = match CreateEventA(None, false, false, None) {
            Ok(h) => h,
            Err(_) => return 0,
        };

        let mut out_buffer = 0u32;
        let mut transferred = 0u32;

        let _ = DeviceIoControl(
            vdd,
            code,
            Some(in_buffer.as_ptr() as *const c_void),
            in_buffer.len() as u32,
            Some(&mut out_buffer as *mut u32 as *mut c_void),
            std::mem::size_of::<u32>() as u32,
            None,
            Some(&mut overlapped),
        );

        if GetOverlappedResultEx(vdd, &mut overlapped, &mut transferred, 5000, false).is_err() {
            let _ = CloseHandle(overlapped.hEvent);
            return 0;
        }

        let _ = CloseHandle(overlapped.hEvent);
        out_buffer
    }
}

#[cfg(windows)]
fn vdd_update(vdd: HANDLE) {
    vdd_ioctl(vdd, VDD_IOCTL_UPDATE, None);
}

#[cfg(windows)]
fn vdd_add_display(vdd: HANDLE) -> i32 {
    let idx = vdd_ioctl(vdd, VDD_IOCTL_ADD, None);
    vdd_update(vdd);
    idx as i32
}

#[cfg(windows)]
fn vdd_remove_display(vdd: HANDLE, index: i32) {
    let idx = index as u16;
    let be = ((idx & 0xFF) << 8) | ((idx >> 8) & 0xFF);
    let data = be.to_ne_bytes();
    vdd_ioctl(vdd, VDD_IOCTL_REMOVE, Some(&data));
    vdd_update(vdd);
}
