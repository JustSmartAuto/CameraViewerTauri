use serde::{Deserialize, Serialize};
use std::sync::{Arc, Mutex, OnceLock};
use tauri::{command, State, WebviewWindow};

static MIRROR_STATE: OnceLock<Arc<MirrorState>> = OnceLock::new();

#[cfg(windows)]
mod win {
    pub use windows::core::w;
    pub use windows::core::BOOL;
    pub use windows::Win32::Foundation::{COLORREF, HWND, LPARAM, LRESULT, RECT, WPARAM};
    pub use windows::Win32::Graphics::Dwm::{
        DwmQueryThumbnailSourceSize, DwmRegisterThumbnail, DwmUnregisterThumbnail,
        DwmUpdateThumbnailProperties, DWM_THUMBNAIL_PROPERTIES, DWM_TNP_OPACITY,
        DWM_TNP_RECTDESTINATION, DWM_TNP_RECTSOURCE, DWM_TNP_VISIBLE,
    };
    pub use windows::Win32::System::LibraryLoader::GetModuleHandleW;
    pub use windows::Win32::UI::WindowsAndMessaging::{
        CreateWindowExW, DefWindowProcW, EnumWindows, GetClientRect, GetWindowLongPtrW,
        GetWindowTextLengthW, GetWindowTextW, GetWindowThreadProcessId, IsIconic, IsWindow,
        IsWindowVisible, RegisterClassW, SetLayeredWindowAttributes, SetWindowLongPtrW,
        SetWindowPos, ShowWindow, CS_HREDRAW, CS_VREDRAW, CW_USEDEFAULT, GWL_EXSTYLE, HWND_TOPMOST,
        LWA_ALPHA, SW_HIDE, SW_SHOW, SWP_FRAMECHANGED, SWP_NOACTIVATE, SWP_NOMOVE, SWP_NOSIZE,
        SWP_SHOWWINDOW, WM_ERASEBKGND, WM_PAINT, WM_SIZE, WNDCLASSW, WS_CLIPCHILDREN,
        WS_CLIPSIBLINGS, WS_EX_LAYERED, WS_EX_TRANSPARENT, WS_OVERLAPPEDWINDOW,
    };
}

#[cfg(windows)]
use win::*;

#[derive(Debug, Clone, Serialize)]
pub struct WindowInfo {
    pub handle: usize,
    pub title: String,
    pub class_name: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct MonitorInfo {
    pub index: i32,
    pub name: String,
    pub x: i32,
    pub y: i32,
    pub width: u32,
    pub height: u32,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Region {
    pub x: i32,
    pub y: i32,
    pub width: i32,
    pub height: i32,
}

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub enum FitMode {
    #[default]
    Original,
    Half,
    Quarter,
    Stretch,
}

pub struct MirrorState {
    thumbnail: Mutex<Option<isize>>,
    source_hwnd: Mutex<Option<usize>>,
    mirror_hwnd: Mutex<Option<usize>>,
    region: Mutex<Option<Region>>,
    opacity: Mutex<u8>,
    click_through: Mutex<bool>,
    fit_mode: Mutex<FitMode>,
}

impl MirrorState {
    pub fn new() -> Self {
        Self {
            thumbnail: Mutex::new(None),
            source_hwnd: Mutex::new(None),
            mirror_hwnd: Mutex::new(None),
            region: Mutex::new(None),
            opacity: Mutex::new(255),
            click_through: Mutex::new(false),
            fit_mode: Mutex::new(FitMode::Original),
        }
    }

    pub fn set_mirror_hwnd(&self, hwnd: usize) {
        *self.mirror_hwnd.lock().unwrap() = Some(hwnd);
    }
}

pub fn set_global_mirror_state(state: Arc<MirrorState>) {
    let _ = MIRROR_STATE.set(state);
}

#[command]
pub fn list_windows() -> Vec<WindowInfo> {
    let mut list = Vec::new();

    #[cfg(windows)]
    unsafe {
        let _ = EnumWindows(
            Some(enum_window_callback),
            LPARAM(&mut list as *mut Vec<WindowInfo> as isize),
        );
    }

    list
}

#[cfg(windows)]
unsafe extern "system" fn enum_window_callback(hwnd: HWND, lparam: LPARAM) -> BOOL {
    let list = &mut *(lparam.0 as *mut Vec<WindowInfo>);

    if IsWindowVisible(hwnd).as_bool() && !IsIconic(hwnd).as_bool() {
        let len = GetWindowTextLengthW(hwnd);
        if len > 0 {
            let mut buf = vec![0u16; len as usize + 1];
            let read = GetWindowTextW(hwnd, &mut buf);
            if read > 0 {
                let title = String::from_utf16_lossy(&buf[..read as usize]);
                let mut pid = 0u32;
                GetWindowThreadProcessId(hwnd, Some(&mut pid));
                list.push(WindowInfo {
                    handle: hwnd.0 as usize,
                    title: title.trim_end_matches('\0').to_string(),
                    class_name: format!("pid:{pid}"),
                });
            }
        }
    }

    true.into()
}

#[command]
pub fn get_monitors(window: WebviewWindow) -> Result<Vec<MonitorInfo>, String> {
    let monitors = window
        .available_monitors()
        .map_err(|e| format!("获取显示器列表失败: {e}"))?;

    Ok(monitors
        .into_iter()
        .enumerate()
        .map(|(idx, m)| MonitorInfo {
            index: idx as i32,
            name: m.name().cloned().unwrap_or_default(),
            x: m.position().x,
            y: m.position().y,
            width: m.size().width,
            height: m.size().height,
        })
        .collect())
}

#[command]
pub fn set_window_monitor(window: WebviewWindow, index: i32) -> Result<(), String> {
    let monitors = window
        .available_monitors()
        .map_err(|e| format!("获取显示器列表失败: {e}"))?;
    let monitor = monitors
        .get(index as usize)
        .ok_or_else(|| "指定的显示器索引无效".to_string())?;

    window
        .set_position(*monitor.position())
        .map_err(|e| format!("设置窗口位置失败: {e}"))?;
    window
        .set_size(*monitor.size())
        .map_err(|e| format!("设置窗口大小失败: {e}"))?;
    Ok(())
}

#[command]
pub fn set_mirror_window(
    state: State<MirrorState>,
    handle: usize,
    region: Option<Region>,
) -> Result<(), String> {
    #[cfg(windows)]
    unsafe {
        let src = HWND(handle as *mut std::ffi::c_void);
        if !IsWindow(Some(src)).as_bool() {
            return Err("无效的窗口句柄".into());
        }

        stop_mirror_internal(&state);

        let mirror_hwnd = state
            .mirror_hwnd
            .lock()
            .unwrap()
            .ok_or("镜像窗口未创建")?;

        let _ = ShowWindow(HWND(mirror_hwnd as *mut std::ffi::c_void), SW_SHOW);

        let thumb = DwmRegisterThumbnail(HWND(mirror_hwnd as *mut std::ffi::c_void), src)
            .map_err(|e| format!("DwmRegisterThumbnail 失败: {e:?}"))?;

        *state.thumbnail.lock().unwrap() = Some(thumb);
        *state.source_hwnd.lock().unwrap() = Some(handle);
        *state.region.lock().unwrap() = region;

        apply_thumbnail_properties(&state)?;
    }

    #[cfg(not(windows))]
    {
        let _ = (state, handle, region);
        return Err("仅支持 Windows".into());
    }

    Ok(())
}

#[command]
pub fn stop_mirror_window(state: State<MirrorState>) -> Result<(), String> {
    stop_mirror_internal(&state);
    Ok(())
}

fn stop_mirror_internal(state: &MirrorState) {
    #[cfg(windows)]
    unsafe {
        if let Some(thumb) = state.thumbnail.lock().unwrap().take() {
            let _ = DwmUnregisterThumbnail(thumb);
        }
        *state.source_hwnd.lock().unwrap() = None;
        if let Some(hwnd) = state.mirror_hwnd.lock().unwrap().as_ref() {
            let _ = ShowWindow(HWND(*hwnd as *mut std::ffi::c_void), SW_HIDE);
        }
    }
}

#[command]
pub fn set_mirror_opacity(state: State<MirrorState>, opacity: f64) -> Result<(), String> {
    #[cfg(windows)]
    unsafe {
        let alpha = (opacity.clamp(0.1, 1.0) * 255.0).round() as u8;
        *state.opacity.lock().unwrap() = alpha;

        let hwnd = get_mirror_hwnd(&state)?;
        ensure_layered(hwnd);
        SetLayeredWindowAttributes(hwnd, COLORREF(0), alpha, LWA_ALPHA).ok();

        apply_thumbnail_properties(&state)?;
    }
    Ok(())
}

#[command]
pub fn set_mirror_click_through(
    state: State<MirrorState>,
    enabled: bool,
) -> Result<(), String> {
    #[cfg(windows)]
    unsafe {
        *state.click_through.lock().unwrap() = enabled;
        let hwnd = get_mirror_hwnd(&state)?;
        let ex_style = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        let mut new_ex = ex_style;
        if enabled {
            new_ex |= WS_EX_TRANSPARENT.0 as isize;
            new_ex |= WS_EX_LAYERED.0 as isize;
        } else {
            new_ex &= !(WS_EX_TRANSPARENT.0 as isize);
        }
        if new_ex != ex_style {
            SetWindowLongPtrW(hwnd, GWL_EXSTYLE, new_ex);
            refresh_window_style(hwnd);
        }

        if enabled {
            let alpha = *state.opacity.lock().unwrap();
            ensure_layered(hwnd);
            SetLayeredWindowAttributes(hwnd, COLORREF(0), alpha, LWA_ALPHA).ok();
        }
    }
    Ok(())
}

#[command]
pub fn set_mirror_fit(state: State<MirrorState>, mode: String) -> Result<(), String> {
    let fit_mode = match mode.as_str() {
        "original" => FitMode::Original,
        "half" => FitMode::Half,
        "quarter" => FitMode::Quarter,
        "stretch" => FitMode::Stretch,
        _ => FitMode::Original,
    };
    *state.fit_mode.lock().unwrap() = fit_mode;
    #[cfg(windows)]
    unsafe {
        apply_thumbnail_properties(&state)?;
    }
    Ok(())
}

#[cfg(windows)]
unsafe fn get_mirror_hwnd(state: &MirrorState) -> Result<HWND, String> {
    state
        .mirror_hwnd
        .lock()
        .unwrap()
        .map(|h| HWND(h as *mut std::ffi::c_void))
        .ok_or("镜像窗口未创建".to_string())
}

#[cfg(windows)]
unsafe fn ensure_layered(hwnd: HWND) {
    let ex_style = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
    if (ex_style & (WS_EX_LAYERED.0 as isize)) == 0 {
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, ex_style | WS_EX_LAYERED.0 as isize);
        refresh_window_style(hwnd);
    }
}

#[cfg(windows)]
unsafe fn apply_thumbnail_properties(state: &MirrorState) -> Result<(), String> {
    let thumb = match *state.thumbnail.lock().unwrap() {
        Some(t) => t,
        None => return Ok(()),
    };

    let hwnd = get_mirror_hwnd(state)?;
    let _src = state.source_hwnd.lock().unwrap().ok_or("未设置源窗口")?;
    let region = state.region.lock().unwrap().clone();
    let fit_mode = *state.fit_mode.lock().unwrap();
    let opacity = *state.opacity.lock().unwrap();

    let mut client = RECT::default();
    GetClientRect(hwnd, &mut client).map_err(|e| format!("GetClientRect 失败: {e:?}"))?;

    let src_size = DwmQueryThumbnailSourceSize(thumb)
        .map_err(|e| format!("DwmQueryThumbnailSourceSize 失败: {e:?}"))?;

    let src_width = src_size.cx;
    let src_height = src_size.cy;

    let source_rect = region.map(|r| RECT {
        left: r.x,
        top: r.y,
        right: r.x + r.width,
        bottom: r.y + r.height,
    });

    let src_w = source_rect.map(|r| r.right - r.left).unwrap_or(src_width);
    let src_h = source_rect.map(|r| r.bottom - r.top).unwrap_or(src_height);

    let (dest_width, dest_height) = match fit_mode {
        FitMode::Original => (src_w, src_h),
        FitMode::Half => (src_w / 2, src_h / 2),
        FitMode::Quarter => (src_w / 4, src_h / 4),
        FitMode::Stretch => (client.right - client.left, client.bottom - client.top),
    };

    let mut dest_rect = RECT {
        left: 0,
        top: 0,
        right: dest_width,
        bottom: dest_height,
    };

    if fit_mode != FitMode::Stretch {
        let cx = (client.right - client.left) / 2;
        let cy = (client.bottom - client.top) / 2;
        dest_rect.left = cx - dest_width / 2;
        dest_rect.top = cy - dest_height / 2;
        dest_rect.right = dest_rect.left + dest_width;
        dest_rect.bottom = dest_rect.top + dest_height;
    }

    let mut props = DWM_THUMBNAIL_PROPERTIES::default();
    props.dwFlags = DWM_TNP_VISIBLE | DWM_TNP_RECTDESTINATION | DWM_TNP_OPACITY;
    if source_rect.is_some() {
        props.dwFlags |= DWM_TNP_RECTSOURCE;
        props.rcSource = source_rect.unwrap();
    }
    props.fVisible = true.into();
    props.opacity = opacity;
    props.rcDestination = dest_rect;

    DwmUpdateThumbnailProperties(thumb, &props)
        .map_err(|e| format!("DwmUpdateThumbnailProperties 失败: {e:?}"))?;

    Ok(())
}

#[cfg(windows)]
unsafe fn refresh_window_style(hwnd: HWND) {
    SetWindowPos(
        hwnd,
        Some(HWND_TOPMOST),
        0,
        0,
        0,
        0,
        SWP_NOMOVE | SWP_NOSIZE | SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOACTIVATE,
    )
    .ok();
}

#[cfg(windows)]
pub unsafe fn create_mirror_window() -> Result<HWND, String> {
    let instance = GetModuleHandleW(None).map_err(|e| format!("GetModuleHandleW 失败: {e:?}"))?;
    let class_name = w!("CameraViewerMirrorHost");

    let mut class = WNDCLASSW::default();
    class.lpfnWndProc = Some(mirror_wndproc);
    class.hInstance = instance.into();
    class.lpszClassName = class_name;
    class.style = CS_HREDRAW | CS_VREDRAW;

    RegisterClassW(&class);

    let hwnd = CreateWindowExW(
        WS_EX_LAYERED,
        class_name,
        w!("窗口镜像"),
        WS_OVERLAPPEDWINDOW | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
        CW_USEDEFAULT,
        CW_USEDEFAULT,
        800,
        600,
        None,
        None,
        Some(instance.into()),
        None,
    )
    .map_err(|e| format!("创建镜像窗口失败: {e:?}"))?;

    // 确保分层窗口默认可见
    let _ = SetLayeredWindowAttributes(hwnd, COLORREF(0), 255, LWA_ALPHA);

    Ok(hwnd)
}

#[cfg(windows)]
unsafe extern "system" fn mirror_wndproc(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    match msg {
        WM_PAINT | WM_ERASEBKGND => LRESULT(1),
        WM_SIZE => {
            if let Some(state) = MIRROR_STATE.get() {
                let _ = apply_thumbnail_properties(state);
            }
            LRESULT(0)
        }
        _ => DefWindowProcW(hwnd, msg, wparam, lparam),
    }
}
