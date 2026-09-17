use std::collections::{HashMap, HashSet};
use std::fmt::Debug;
use std::fs;
use std::io;
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use chrono::{DateTime, Local};
use notify::{Config, Event, RecommendedWatcher, RecursiveMode, Watcher};
use serde::{Deserialize, Serialize};
use tauri::{webview::WebviewWindowBuilder, AppHandle, Emitter, Manager, State, WebviewUrl};
use tauri::menu::{Menu, MenuItem, PredefinedMenuItem};
use tauri::tray::TrayIconBuilder;

mod jobx_backup;
use jobx_backup::*;

mod display;
use display::*;

mod virtual_display;
use virtual_display::*;

mod debug_tools;
use debug_tools::*;

mod clean_script;
use clean_script::*;

mod vision;
use vision::*;

// ==================== 数据模型 ====================

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CamConfigItem {
    pub id: i32,
    pub ip: String,
    pub remark: String,
    #[serde(default)]
    pub locked: bool,
    /// CogSocket 登录用户名（默认 admin，仅 cogsocket 模式使用）
    #[serde(default)]
    pub cogsocket_user: String,
    /// CogSocket 登录密码（明文存于 CameraConfig.json，默认空）
    #[serde(default)]
    pub cogsocket_password: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CameraConfig {
    pub count: i32,
    pub delay: i32,
    pub items: Vec<CamConfigItem>,
}

impl Default for CameraConfig {
    fn default() -> Self {
        Self {
            count: 1,
            delay: 10,
            items: vec![],
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub enum EPartType {
    Name,
    DateTime,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PartItem {
    pub part_type: EPartType,
    pub source_index: i32,
    pub format: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TransformConfig {
    pub name: String,
    pub watch_path: String,
    pub source_spliter: String,
    pub folder_index: i32,
    pub file_name_index: i32,
    pub folder_spliter: String,
    pub folder_part_list: Vec<PartItem>,
    pub file_name_spliter: String,
    pub file_name_part_list: Vec<PartItem>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CleanConfig {
    pub is_enable: bool,
    pub folder_path: String,
    pub scan_interval: f64,
    pub is_clean_empty_folders: bool,
    pub is_enable_hold_days: bool,
    pub hold_days: i32,
    pub is_enable_remaining_space: bool,
    pub remaining_space: f64,
    pub delete_interval: i32,
    /// 高级版：启用后由用户脚本决定是否继续删除最旧目标
    #[serde(default)]
    pub is_advanced: bool,
    /// 高级版用户脚本（定义 evaluate(context)），为空时回退到默认 AND 逻辑
    #[serde(default)]
    pub delete_script: String,
    /// 数量条件开关（与 ImageCleanerAutoWeld 对齐，高级版 context 传入）
    #[serde(default)]
    pub is_enable_image_count: bool,
    /// 数量阈值
    #[serde(default)]
    pub image_count_threshold: i32,
}

impl Default for CleanConfig {
    fn default() -> Self {
        Self {
            is_enable: false,
            folder_path: String::new(),
            scan_interval: 2.0,
            is_clean_empty_folders: true,
            is_enable_hold_days: false,
            hold_days: 30,
            is_enable_remaining_space: true,
            remaining_space: 5.0,
            delete_interval: 5,
            is_advanced: false,
            delete_script: String::new(),
            is_enable_image_count: false,
            image_count_threshold: 3000,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CompressConfig {
    pub is_enable: bool,
    pub watch_path: String,
    pub output_path: String,
    pub quality: f32,
    pub speed: u8,
    pub delete_source: bool,
}

impl Default for CompressConfig {
    fn default() -> Self {
        Self {
            is_enable: false,
            watch_path: String::new(),
            output_path: String::new(),
            quality: 80.0,
            speed: 4,
            delete_source: false,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CompressCheckpoint {
    pub completed: Vec<String>,
}

impl Default for CompressCheckpoint {
    fn default() -> Self {
        Self { completed: vec![] }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CompressStatus {
    pub is_running: bool,
    pub is_paused: bool,
    pub total_files: i32,
    pub completed_files: i32,
}

// ==================== FTP 数据模型 ====================

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FtpUser {
    pub username: String,
    pub password: String,
    pub root_dir: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FtpConfig {
    pub is_enable: bool,
    pub port: u16,
    pub use_tls: bool,
    pub users: Vec<FtpUser>,
}

impl Default for FtpConfig {
    fn default() -> Self {
        Self {
            is_enable: false,
            port: 21,
            use_tls: false,
            users: vec![FtpUser {
                username: "A1".to_string(),
                password: "".to_string(),
                root_dir: "D:/CCD图片".to_string(),
            }],
        }
    }
}

#[derive(Debug, Clone, Serialize)]
pub struct FtpLogEntry {
    pub timestamp: String,
    pub level: String,
    pub message: String,
}

#[derive(Debug, Clone, Serialize)]
pub struct FtpStatus {
    pub is_running: bool,
}

// ==================== NTP 数据模型 ====================

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct NtpConfig {
    pub is_enable: bool,
    pub port: u16,
}

impl Default for NtpConfig {
    fn default() -> Self {
        Self {
            is_enable: false,
            port: 123,
        }
    }
}

#[derive(Debug, Clone, Serialize)]
pub struct NtpStatus {
    pub is_running: bool,
}

#[derive(Debug, Clone, Serialize)]
pub struct NtpLogEntry {
    pub timestamp: String,
    pub level: String,
    pub message: String,
}

// ==================== 全局状态 ====================

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AppConfig {
    pub language: String,
    pub theme: String,
    /// 相机连接模式：http / cogsocket / gige，默认 http。
    /// CogSocket/GigE 协议实现见 TODO #2/#12/#21，此处仅持久化用户选择。
    #[serde(default = "default_connection_mode")]
    pub connection_mode: String,
    /// 轻度视觉检测功能开关（TODO #13）：开启后主界面显示入口按钮与标签页。
    #[serde(default)]
    pub vision_inspection_enabled: bool,
}

fn default_connection_mode() -> String {
    "http".to_string()
}

impl Default for AppConfig {
    fn default() -> Self {
        Self {
            language: "zh".to_string(),
            theme: "light".to_string(),
            connection_mode: default_connection_mode(),
            vision_inspection_enabled: false,
        }
    }
}

pub struct AppState {
    camera_config: Mutex<CameraConfig>,
    transform_configs: Mutex<Vec<TransformConfig>>,
    clean_config: Mutex<CleanConfig>,
    compress_config: Mutex<CompressConfig>,
    ftp_config: Mutex<FtpConfig>,
    app_config: Mutex<AppConfig>,
    vision_config: Mutex<VisionConfig>,
    watcher_handles: Mutex<HashMap<String, RecommendedWatcher>>,
    cleaner_running: Mutex<bool>,
    compress_running: Mutex<bool>,
    compress_paused: Mutex<bool>,
    compress_total: Mutex<i32>,
    compress_completed: Mutex<i32>,
    compress_watcher: Mutex<Option<RecommendedWatcher>>,
    ftp_running: Mutex<bool>,
    ftp_log_buffer: Arc<Mutex<Vec<FtpLogEntry>>>,
    ntp_config: Mutex<NtpConfig>,
    ntp_running: Mutex<bool>,
    ntp_log_buffer: Arc<Mutex<Vec<NtpLogEntry>>>,
    pub(crate) jobx_backup_config: Mutex<JobxBackupConfig>,
    pub(crate) jobx_log_buffer: Arc<Mutex<Vec<JobxBackupLogEntry>>>,
    pub(crate) remote_command_state: Arc<RemoteCommandState>,
    pub(crate) config_dir: PathBuf,
    /// 配置目录模式：default（系统默认 AppData）/ portable（exe 同目录）/ custom（命令行 --config-dir）
    pub(crate) config_mode: String,
}

/// 便携模式标记文件名（放在 exe 同目录，存在即表示配置跟随 exe）
const PORTABLE_MARKER: &str = "portable.txt";
/// 便携模式下的配置子目录名
const PORTABLE_CONFIG_DIR: &str = "Configs";

/// 解析配置目录与模式（启动时、加载任何配置之前调用）
/// 优先级：命令行 `--config-dir <路径>` > exe 同目录标记文件 portable.txt > 系统默认 AppData
fn resolve_config_dir(app_handle: &AppHandle) -> (PathBuf, String) {
    // 1. 命令行参数：同一 exe 可用不同快捷方式启动多个独立实例
    let args: Vec<String> = std::env::args().collect();
    if let Some(pos) = args.iter().position(|a| a == "--config-dir") {
        if let Some(p) = args.get(pos + 1) {
            let dir = PathBuf::from(p);
            let _ = fs::create_dir_all(&dir);
            return (dir, "custom".to_string());
        }
    }

    // 2. exe 同目录的便携标记文件：整个程序文件夹拷贝走即得到独立实例
    if let Ok(exe) = std::env::current_exe() {
        if let Some(exe_parent) = exe.parent() {
            if exe_parent.join(PORTABLE_MARKER).exists() {
                let dir = exe_parent.join(PORTABLE_CONFIG_DIR);
                let _ = fs::create_dir_all(&dir);
                return (dir, "portable".to_string());
            }
        }
    }

    // 3. 系统默认目录（AppData\Local\<identifier>）
    let dir = app_handle
        .path()
        .app_local_data_dir()
        .unwrap_or_else(|_| PathBuf::from("./Configs"));
    let _ = fs::create_dir_all(&dir);
    (dir, "default".to_string())
}

/// exe 所在目录
fn exe_dir() -> Option<PathBuf> {
    std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(Path::to_path_buf))
}

/// 递归复制目录内容到目标目录（合并，同名文件覆盖）
fn copy_dir_contents(src: &Path, dst: &Path) -> io::Result<u64> {
    let mut count = 0u64;
    if !src.is_dir() {
        return Ok(0);
    }
    fs::create_dir_all(dst)?;
    for entry in fs::read_dir(src)? {
        let entry = entry?;
        let from = entry.path();
        let to = dst.join(entry.file_name());
        if from.is_dir() {
            count += copy_dir_contents(&from, &to)?;
        } else {
            fs::copy(&from, &to)?;
            count += 1;
        }
    }
    Ok(count)
}

impl AppState {
    pub fn new(app_handle: &AppHandle) -> Self {
        let (config_dir, config_mode) = resolve_config_dir(app_handle);

        let state = Self {
            camera_config: Mutex::new(CameraConfig::default()),
            transform_configs: Mutex::new(vec![]),
            clean_config: Mutex::new(CleanConfig::default()),
            compress_config: Mutex::new(CompressConfig::default()),
            ftp_config: Mutex::new(FtpConfig::default()),
            app_config: Mutex::new(AppConfig::default()),
            vision_config: Mutex::new(VisionConfig::default()),
            watcher_handles: Mutex::new(HashMap::new()),
            cleaner_running: Mutex::new(false),
            compress_running: Mutex::new(false),
            compress_paused: Mutex::new(false),
            compress_total: Mutex::new(0),
            compress_completed: Mutex::new(0),
            compress_watcher: Mutex::new(None),
            ftp_running: Mutex::new(false),
            ftp_log_buffer: Arc::new(Mutex::new(Vec::new())),
            ntp_config: Mutex::new(NtpConfig::default()),
            ntp_running: Mutex::new(false),
            ntp_log_buffer: Arc::new(Mutex::new(Vec::new())),
            jobx_backup_config: Mutex::new(JobxBackupConfig::default()),
            jobx_log_buffer: Arc::new(Mutex::new(Vec::new())),
            remote_command_state: Arc::new(RemoteCommandState::new()),
            config_dir,
            config_mode,
        };

        state.load_all_configs();
        state
    }

    fn config_path(&self, name: &str) -> PathBuf {
        self.config_dir.join(name)
    }

    fn checkpoint_path(&self) -> PathBuf {
        self.config_path("CompressCheckpoint.json")
    }

    fn load_all_configs(&self) {
        let cam_path = self.config_path("CameraConfig.json");
        if cam_path.exists() {
            if let Ok(content) = fs::read_to_string(&cam_path) {
                if let Ok(config) = serde_json::from_str::<CameraConfig>(&content) {
                    *self.camera_config.lock().unwrap() = config;
                }
            }
        }

        let trans_path = self.config_path("TransformConfig.json");
        if trans_path.exists() {
            if let Ok(content) = fs::read_to_string(&trans_path) {
                if let Ok(configs) = serde_json::from_str::<Vec<TransformConfig>>(&content) {
                    *self.transform_configs.lock().unwrap() = configs;
                }
            }
        }

        let clean_path = self.config_path("CleanConfig.json");
        if clean_path.exists() {
            if let Ok(content) = fs::read_to_string(&clean_path) {
                if let Ok(config) = serde_json::from_str::<CleanConfig>(&content) {
                    *self.clean_config.lock().unwrap() = config;
                }
            }
        }

        let compress_path = self.config_path("CompressConfig.json");
        if compress_path.exists() {
            if let Ok(content) = fs::read_to_string(&compress_path) {
                if let Ok(config) = serde_json::from_str::<CompressConfig>(&content) {
                    *self.compress_config.lock().unwrap() = config;
                }
            }
        }

        let ftp_path = self.config_path("FtpConfig.json");
        if ftp_path.exists() {
            if let Ok(content) = fs::read_to_string(&ftp_path) {
                if let Ok(config) = serde_json::from_str::<FtpConfig>(&content) {
                    *self.ftp_config.lock().unwrap() = config;
                }
            }
        }

        let ntp_path = self.config_path("NtpConfig.json");
        if ntp_path.exists() {
            if let Ok(content) = fs::read_to_string(&ntp_path) {
                if let Ok(config) = serde_json::from_str::<NtpConfig>(&content) {
                    *self.ntp_config.lock().unwrap() = config;
                }
            }
        }

        let app_path = self.config_path("AppConfig.json");
        if app_path.exists() {
            if let Ok(content) = fs::read_to_string(&app_path) {
                if let Ok(config) = serde_json::from_str::<AppConfig>(&content) {
                    *self.app_config.lock().unwrap() = config;
                }
            }
        }

        let vision_path = self.config_path("VisionConfig.json");
        if vision_path.exists() {
            if let Ok(content) = fs::read_to_string(&vision_path) {
                if let Ok(config) = serde_json::from_str::<VisionConfig>(&content) {
                    *self.vision_config.lock().unwrap() = config;
                }
            }
        }

        let jobx_path = self.config_path("JobxBackupConfig.json");
        if jobx_path.exists() {
            if let Ok(content) = fs::read_to_string(&jobx_path) {
                if let Ok(config) = serde_json::from_str::<JobxBackupConfig>(&content) {
                    *self.jobx_backup_config.lock().unwrap() = config;
                }
            }
        }
    }

    fn save_app_config(&self) {
        let config = self.app_config.lock().unwrap().clone();
        let path = self.config_path("AppConfig.json");
        if let Ok(json) = serde_json::to_string_pretty(&config) {
            fs::write(path, json).ok();
        }
    }

    fn save_vision_config(&self) {
        let config = self.vision_config.lock().unwrap().clone();
        let path = self.config_path("VisionConfig.json");
        if let Ok(json) = serde_json::to_string_pretty(&config) {
            fs::write(path, json).ok();
        }
    }

    fn save_camera_config(&self) {
        let config = self.camera_config.lock().unwrap().clone();
        let path = self.config_path("CameraConfig.json");
        if let Ok(json) = serde_json::to_string_pretty(&config) {
            fs::write(path, json).ok();
        }
    }

    fn save_transform_configs(&self) {
        let configs = self.transform_configs.lock().unwrap().clone();
        let path = self.config_path("TransformConfig.json");
        if let Ok(json) = serde_json::to_string_pretty(&configs) {
            fs::write(path, json).ok();
        }
    }

    fn save_clean_config(&self) {
        let config = self.clean_config.lock().unwrap().clone();
        let path = self.config_path("CleanConfig.json");
        if let Ok(json) = serde_json::to_string_pretty(&config) {
            fs::write(path, json).ok();
        }
    }

    fn save_compress_config(&self) {
        let config = self.compress_config.lock().unwrap().clone();
        let path = self.config_path("CompressConfig.json");
        if let Ok(json) = serde_json::to_string_pretty(&config) {
            fs::write(path, json).ok();
        }
    }

    fn save_ftp_config(&self) {
        let config = self.ftp_config.lock().unwrap().clone();
        let path = self.config_path("FtpConfig.json");
        if let Ok(json) = serde_json::to_string_pretty(&config) {
            fs::write(path, json).ok();
        }
    }

    fn save_ntp_config(&self) {
        let config = self.ntp_config.lock().unwrap().clone();
        let path = self.config_path("NtpConfig.json");
        if let Ok(json) = serde_json::to_string_pretty(&config) {
            fs::write(path, json).ok();
        }
    }

    fn load_checkpoint(&self) -> HashSet<String> {
        let path = self.checkpoint_path();
        if path.exists() {
            if let Ok(content) = fs::read_to_string(&path) {
                if let Ok(checkpoint) = serde_json::from_str::<CompressCheckpoint>(&content) {
                    return checkpoint.completed.into_iter().collect();
                }
            }
        }
        HashSet::new()
    }

    fn save_checkpoint(&self, completed: &HashSet<String>) {
        let checkpoint = CompressCheckpoint {
            completed: completed.iter().cloned().collect(),
        };
        let path = self.checkpoint_path();
        if let Ok(json) = serde_json::to_string_pretty(&checkpoint) {
            fs::write(path, json).ok();
        }
    }

    fn add_ftp_log(&self, level: &str, message: &str) {
        let entry = FtpLogEntry {
            timestamp: Local::now().format("%H:%M:%S").to_string(),
            level: level.to_string(),
            message: message.to_string(),
        };
        let mut buffer = self.ftp_log_buffer.lock().unwrap();
        buffer.push(entry.clone());
        if buffer.len() > 1000 {
            buffer.remove(0);
        }
    }

    fn add_ntp_log(&self, level: &str, message: &str) {
        let entry = NtpLogEntry {
            timestamp: Local::now().format("%H:%M:%S").to_string(),
            level: level.to_string(),
            message: message.to_string(),
        };
        let mut buffer = self.ntp_log_buffer.lock().unwrap();
        buffer.push(entry.clone());
        if buffer.len() > 1000 {
            buffer.remove(0);
        }
    }

    pub(crate) fn save_jobx_backup_config(&self) {
        let config = self.jobx_backup_config.lock().unwrap().clone();
        let path = self.config_path("JobxBackupConfig.json");
        if let Ok(json) = serde_json::to_string_pretty(&config) {
            fs::write(path, json).ok();
        }
    }

    pub(crate) fn add_jobx_log(&self, level: &str, message: &str) {
        let entry = JobxBackupLogEntry {
            timestamp: Local::now().format("%H:%M:%S").to_string(),
            level: level.to_string(),
            message: message.to_string(),
        };
        let mut buffer = self.jobx_log_buffer.lock().unwrap();
        buffer.push(entry.clone());
        if buffer.len() > 1000 {
            buffer.remove(0);
        }
    }
}

// ==================== Tauri Commands ====================

#[tauri::command]
fn get_camera_config(state: State<Arc<AppState>>) -> CameraConfig {
    state.camera_config.lock().unwrap().clone()
}

#[tauri::command]
fn set_camera_config(config: CameraConfig, state: State<Arc<AppState>>) {
    *state.camera_config.lock().unwrap() = config;
    state.save_camera_config();
}

#[tauri::command]
fn update_camera_config(count: i32, delay: i32, state: State<Arc<AppState>>) {
    let mut config = state.camera_config.lock().unwrap();
    let old_items: Vec<CamConfigItem> = config.items.clone();
    config.count = count;
    config.delay = delay;

    let mut new_items = Vec::with_capacity(count as usize);
    for i in 0..count {
        let item = old_items.iter().find(|x| x.id == i).cloned().unwrap_or(CamConfigItem {
            id: i,
            ip: String::new(),
            remark: String::new(),
            locked: false,
            cogsocket_user: String::new(),
            cogsocket_password: String::new(),
        });
        new_items.push(item);
    }
    config.items = new_items;
    drop(config);
    state.save_camera_config();
}

#[tauri::command]
fn update_camera_ip(id: i32, ip: String, remark: String, state: State<Arc<AppState>>) {
    let mut config = state.camera_config.lock().unwrap();
    if let Some(item) = config.items.iter_mut().find(|x| x.id == id) {
        item.ip = ip;
        item.remark = remark;
    } else {
        config.items.push(CamConfigItem {
            id,
            ip,
            remark,
            locked: false,
            cogsocket_user: String::new(),
            cogsocket_password: String::new(),
        });
    }
    drop(config);
    state.save_camera_config();
}

#[tauri::command]
fn update_camera_lock(id: i32, locked: bool, state: State<Arc<AppState>>) {
    let mut config = state.camera_config.lock().unwrap();
    if let Some(item) = config.items.iter_mut().find(|x| x.id == id) {
        item.locked = locked;
    } else {
        config.items.push(CamConfigItem {
            id,
            ip: String::new(),
            remark: String::new(),
            locked,
            cogsocket_user: String::new(),
            cogsocket_password: String::new(),
        });
    }
    drop(config);
    state.save_camera_config();
}

/// 保存某相机的 CogSocket 登录凭据（仅 cogsocket 模式使用，明文存配置）。
#[tauri::command]
fn update_camera_cogsocket_auth(
    id: i32,
    user: String,
    password: String,
    state: State<Arc<AppState>>,
) {
    let mut config = state.camera_config.lock().unwrap();
    if let Some(item) = config.items.iter_mut().find(|x| x.id == id) {
        item.cogsocket_user = user;
        item.cogsocket_password = password;
    } else {
        config.items.push(CamConfigItem {
            id,
            ip: String::new(),
            remark: String::new(),
            locked: false,
            cogsocket_user: user,
            cogsocket_password: password,
        });
    }
    drop(config);
    state.save_camera_config();
}

#[tauri::command]
fn get_transform_configs(state: State<Arc<AppState>>) -> Vec<TransformConfig> {
    state.transform_configs.lock().unwrap().clone()
}

#[tauri::command]
fn set_transform_configs(configs: Vec<TransformConfig>, state: State<Arc<AppState>>) {
    *state.transform_configs.lock().unwrap() = configs;
    state.save_transform_configs();
}

#[tauri::command]
fn add_transform_config(config: TransformConfig, state: State<Arc<AppState>>) {
    let mut configs = state.transform_configs.lock().unwrap();
    configs.push(config);
    drop(configs);
    state.save_transform_configs();
}

#[tauri::command]
fn delete_transform_config(name: String, state: State<Arc<AppState>>) {
    let mut configs = state.transform_configs.lock().unwrap();
    configs.retain(|c| c.name != name);
    drop(configs);
    state.save_transform_configs();
}

#[tauri::command]
fn get_clean_config(state: State<Arc<AppState>>) -> CleanConfig {
    state.clean_config.lock().unwrap().clone()
}

#[tauri::command]
fn set_clean_config(config: CleanConfig, state: State<Arc<AppState>>) {
    *state.clean_config.lock().unwrap() = config;
    state.save_clean_config();
}

#[tauri::command]
fn get_compress_config(state: State<Arc<AppState>>) -> CompressConfig {
    state.compress_config.lock().unwrap().clone()
}

#[tauri::command]
fn set_compress_config(config: CompressConfig, state: State<Arc<AppState>>) {
    *state.compress_config.lock().unwrap() = config;
    state.save_compress_config();
}

#[tauri::command]
fn get_compress_status(state: State<Arc<AppState>>) -> CompressStatus {
    CompressStatus {
        is_running: *state.compress_running.lock().unwrap(),
        is_paused: *state.compress_paused.lock().unwrap(),
        total_files: *state.compress_total.lock().unwrap(),
        completed_files: *state.compress_completed.lock().unwrap(),
    }
}

#[tauri::command]
fn pause_compress(state: State<Arc<AppState>>) {
    *state.compress_paused.lock().unwrap() = true;
}

#[tauri::command]
fn resume_compress(state: State<Arc<AppState>>) {
    *state.compress_paused.lock().unwrap() = false;
}

#[tauri::command]
fn get_ftp_config(state: State<Arc<AppState>>) -> FtpConfig {
    state.ftp_config.lock().unwrap().clone()
}

#[tauri::command]
fn set_ftp_config(config: FtpConfig, state: State<Arc<AppState>>) {
    *state.ftp_config.lock().unwrap() = config;
    state.save_ftp_config();
}

#[tauri::command]
fn get_ftp_status(state: State<Arc<AppState>>) -> FtpStatus {
    FtpStatus {
        is_running: *state.ftp_running.lock().unwrap(),
    }
}

#[tauri::command]
fn get_ftp_logs(state: State<Arc<AppState>>) -> Vec<FtpLogEntry> {
    state.ftp_log_buffer.lock().unwrap().clone()
}

#[tauri::command]
fn get_current_time() -> String {
    Local::now().format("%Y年%m月%d日  %H:%M:%S").to_string()
}

#[tauri::command]
fn get_app_version() -> String {
    env!("CARGO_PKG_VERSION").to_string()
}

#[tauri::command]
fn get_ntp_config(state: State<Arc<AppState>>) -> NtpConfig {
    state.ntp_config.lock().unwrap().clone()
}

#[tauri::command]
fn set_ntp_config(config: NtpConfig, state: State<Arc<AppState>>) {
    *state.ntp_config.lock().unwrap() = config;
    state.save_ntp_config();
}

#[tauri::command]
fn get_ntp_status(state: State<Arc<AppState>>) -> NtpStatus {
    NtpStatus {
        is_running: *state.ntp_running.lock().unwrap(),
    }
}

#[tauri::command]
fn get_ntp_logs(state: State<Arc<AppState>>) -> Vec<NtpLogEntry> {
    state.ntp_log_buffer.lock().unwrap().clone()
}

#[tauri::command]
fn get_app_config(state: State<Arc<AppState>>) -> AppConfig {
    state.app_config.lock().unwrap().clone()
}

#[tauri::command]
fn set_app_config(config: AppConfig, state: State<Arc<AppState>>) {
    *state.app_config.lock().unwrap() = config;
    state.save_app_config();
}

// ==================== 文件转存逻辑 ====================

fn run_transform_action(config: &TransformConfig) {
    let watch_path = &config.watch_path;
    if watch_path.is_empty() || !Path::new(watch_path).exists() {
        return;
    }

    let entries = match fs::read_dir(watch_path) {
        Ok(e) => e,
        Err(_) => return,
    };

    for entry in entries.flatten() {
        let path = entry.path();
        if !path.is_file() {
            continue;
        }

        let _file_name = match path.file_name().and_then(|n| n.to_str()) {
            Some(n) => n,
            None => continue,
        };

        let (stem, extension) = match path.file_stem().and_then(|s| s.to_str()) {
            Some(s) => (s, path.extension().and_then(|e| e.to_str()).unwrap_or("")),
            None => continue,
        };

        let parts: Vec<&str> = stem.split(&config.source_spliter).collect();
        if parts.len() <= config.folder_index as usize
            || parts.len() <= config.file_name_index as usize
        {
            continue;
        }

        let folder_parts: Vec<&str> = parts[config.folder_index as usize]
            .split(&config.folder_spliter)
            .collect();
        let file_parts: Vec<&str> = parts[config.file_name_index as usize]
            .split(&config.file_name_spliter)
            .collect();

        let mut target_dir = String::new();
        for fp in &config.folder_part_list {
            match fp.part_type {
                EPartType::Name => {
                    let idx = fp.source_index as usize;
                    if idx < folder_parts.len() {
                        if idx == 0 && target_dir.is_empty() {
                            target_dir.push_str(folder_parts[idx]);
                            target_dir.push(':');
                        } else {
                            target_dir.push_str(folder_parts[idx]);
                        }
                    }
                }
                EPartType::DateTime => {
                    target_dir.push_str(&Local::now().format(&fp.format).to_string());
                }
            }
            target_dir.push('\\');
        }

        let mut target_file = String::new();
        for fp in &config.file_name_part_list {
            match fp.part_type {
                EPartType::Name => {
                    let idx = fp.source_index as usize;
                    if idx < file_parts.len() {
                        target_file.push_str(file_parts[idx]);
                    }
                }
                EPartType::DateTime => {
                    target_file.push_str(&Local::now().format(&fp.format).to_string());
                }
            }
            target_file.push_str(&config.file_name_spliter);
        }
        if !target_file.is_empty() {
            target_file.truncate(target_file.len() - config.file_name_spliter.len());
        }
        if !extension.is_empty() {
            target_file.push('.');
            target_file.push_str(extension);
        }

        let target_path = Path::new(&target_dir).join(&target_file);
        if let Some(parent) = target_path.parent() {
            fs::create_dir_all(parent).ok();
        }
        if target_path.exists() {
            fs::remove_file(&target_path).ok();
        }
        fs::rename(&path, &target_path).ok();
    }
}

// ==================== 文件监控 ====================

#[tauri::command]
fn start_file_watchers(state: State<Arc<AppState>>) {
    let configs = state.transform_configs.lock().unwrap().clone();
    let mut handles = state.watcher_handles.lock().unwrap();

    handles.clear();

    for config in configs {
        let watch_path = config.watch_path.clone();
        if watch_path.is_empty() || !Path::new(&watch_path).exists() {
            continue;
        }

        let config_clone = config.clone();
        let mut watcher = match RecommendedWatcher::new(
            move |res: Result<Event, notify::Error>| {
                if let Ok(event) = res {
                    if matches!(event.kind, notify::EventKind::Create(_)) {
                        std::thread::sleep(Duration::from_millis(500));
                        run_transform_action(&config_clone);
                    }
                }
            },
            Config::default(),
        ) {
            Ok(w) => w,
            Err(_) => continue,
        };

        watcher.watch(Path::new(&watch_path), RecursiveMode::NonRecursive).ok();
        handles.insert(config.name, watcher);
    }
}

#[tauri::command]
fn stop_file_watchers(state: State<Arc<AppState>>) {
    let mut handles = state.watcher_handles.lock().unwrap();
    handles.clear();
}

// ==================== 图片清理 ====================

#[tauri::command]
fn start_cleaner(app: AppHandle, state: State<Arc<AppState>>) {
    let config = state.clean_config.lock().unwrap().clone();
    if !config.is_enable {
        return;
    }

    {
        let mut running = state.cleaner_running.lock().unwrap();
        *running = true;
    }

    let state_clone = Arc::clone(&state);
    let app_clone = app.clone();

    std::thread::spawn(move || {
        let interval_hours = config.scan_interval.max(0.1);
        let sleep_secs = (interval_hours * 3600.0) as u64;

        loop {
            std::thread::sleep(Duration::from_secs(sleep_secs));

            let is_running = *state_clone.cleaner_running.lock().unwrap();
            if !is_running {
                break;
            }

            let cfg = state_clone.clean_config.lock().unwrap().clone();
            if cfg.is_enable {
                clean_once(&cfg, &app_clone);
            }
        }
    });
}

#[tauri::command]
fn stop_cleaner(state: State<Arc<AppState>>) {
    let mut running = state.cleaner_running.lock().unwrap();
    *running = false;
}

fn clean_once(config: &CleanConfig, _app: &AppHandle) {
    let folder_path = &config.folder_path;
    if folder_path.is_empty() || !Path::new(folder_path).exists() {
        return;
    }

    let mut files = Vec::new();
    collect_files(folder_path, &mut files, config.is_clean_empty_folders);

    files.sort_by(|a, b| {
        let a_time = a.metadata().ok().and_then(|m| m.created().ok());
        let b_time = b.metadata().ok().and_then(|m| m.created().ok());
        a_time.cmp(&b_time)
    });

    let path_root = Path::new(folder_path)
        .components()
        .next()
        .map(|c| c.as_os_str().to_string_lossy().to_string())
        .unwrap_or_default();

    // 高级版：用 boa_engine 沙箱执行用户脚本判定（每删一个重新评估，直到脚本返回 false）
    // 脚本为空或返回非布尔值时回退到默认 AND 逻辑，保持向后兼容
    let advanced_enabled = config.is_advanced && !config.delete_script.trim().is_empty();
    if advanced_enabled {
        let now = Local::now();
        let now_ms = now.timestamp_millis() as f64;
        let storage_time_seconds = (config.hold_days as f64) * 86400.0;
        let threshold_ms = now_ms - storage_time_seconds * 1000.0;

        for i in 0..files.len() {
            std::thread::sleep(Duration::from_secs(config.delete_interval.max(0) as u64));

            // 每次迭代重新评估：剩余目标数与已过期数都会随删除递减
            let remaining = (files.len() - i) as i64;
            let expired_count = files[i..]
                .iter()
                .filter_map(|f| f.metadata().ok()?.created().ok())
                .filter(|t| {
                    (DateTime::<Local>::from(*t).timestamp_millis() as f64) < threshold_ms
                })
                .count() as i64;
            let free_space_gb = get_disk_space(&path_root).map(|(_, f)| f).unwrap_or(0.0);

            let ctx = CleanScriptContext {
                now_ms,
                path: folder_path.clone(),
                cleanup_mode: "Image".to_string(),
                storage_time_seconds,
                expired_count,
                image_count: remaining,
                image_count_enabled: config.is_enable_image_count,
                image_count_threshold: config.image_count_threshold as i64,
                free_space_gb,
                disk_space_enabled: config.is_enable_remaining_space,
                disk_space_threshold_gb: config.remaining_space,
            };

            let result = evaluate_decision(&config.delete_script, &ctx);
            match result.decision {
                Some(true) => {
                    fs::remove_file(&files[i]).ok();
                }
                Some(false) => break, // 脚本要求停止
                None => break, // 脚本出错或返回非布尔值，静默停止（后台任务惯例）
            }
        }
    } else {
        // 旧 OR 行为（零回归）：hold_days 满足 OR remaining_space 满足则删除
        for file in &files {
            std::thread::sleep(Duration::from_secs(config.delete_interval.max(0) as u64));

            let created = match file.metadata().and_then(|m| m.created()) {
                Ok(t) => DateTime::<Local>::from(t),
                Err(_) => continue,
            };

            if config.is_enable_hold_days {
                let days = (Local::now() - created).num_days();
                if days > config.hold_days as i64 {
                    fs::remove_file(file).ok();
                    continue;
                }
            }

            if config.is_enable_remaining_space {
                if let Some((_, free_gb)) = get_disk_space(&path_root) {
                    if free_gb < config.remaining_space {
                        fs::remove_file(file).ok();
                    }
                }
            }
        }
    }

    if config.is_clean_empty_folders {
        clean_empty_dirs(folder_path);
    }
}

fn collect_files(dir: &str, files: &mut Vec<PathBuf>, recursive: bool) {
    let entries = match fs::read_dir(dir) {
        Ok(e) => e,
        Err(_) => return,
    };

    for entry in entries.flatten() {
        let path = entry.path();
        if path.is_file() {
            files.push(path);
        } else if recursive && path.is_dir() {
            collect_files(path.to_str().unwrap_or(""), files, recursive);
        }
    }
}

fn clean_empty_dirs(dir: &str) {
    loop {
        let mut empty_dirs = Vec::new();
        find_empty_dirs(dir, &mut empty_dirs);
        if empty_dirs.is_empty() {
            break;
        }
        for d in empty_dirs {
            fs::remove_dir(d).ok();
        }
    }
}

fn find_empty_dirs(dir: &str, empty_dirs: &mut Vec<PathBuf>) {
    let entries = match fs::read_dir(dir) {
        Ok(e) => e,
        Err(_) => return,
    };

    for entry in entries.flatten() {
        let path = entry.path();
        if path.is_dir() {
            find_empty_dirs(path.to_str().unwrap_or(""), empty_dirs);
            if let Ok(mut sub) = fs::read_dir(&path) {
                if sub.next().is_none() {
                    empty_dirs.push(path);
                }
            }
        }
    }
}

#[cfg(target_os = "windows")]
fn get_disk_space(disk_name: &str) -> Option<(f64, f64)> {
    use std::os::windows::ffi::OsStrExt;

    let wide: Vec<u16> = std::ffi::OsStr::new(disk_name)
        .encode_wide()
        .chain(Some(0))
        .collect();

    let mut free_bytes = 0u64;
    let mut total_bytes = 0u64;
    let mut total_free = 0u64;

    unsafe {
        if windows_sys::Win32::Storage::FileSystem::GetDiskFreeSpaceExW(
            wide.as_ptr(),
            &mut free_bytes,
            &mut total_bytes,
            &mut total_free,
        ) != 0
        {
            let total_gb = total_bytes as f64 / (1024.0 * 1024.0 * 1024.0);
            let free_gb = free_bytes as f64 / (1024.0 * 1024.0 * 1024.0);
            return Some((total_gb, free_gb));
        }
    }
    None
}

#[cfg(not(target_os = "windows"))]
fn get_disk_space(_disk_name: &str) -> Option<(f64, f64)> {
    None
}

// ==================== 图像压缩 ====================

fn is_image_file(path: &Path) -> bool {
    if let Some(ext) = path.extension() {
        let ext = ext.to_string_lossy().to_lowercase();
        matches!(ext.as_str(), "bmp" | "png" | "jpg" | "jpeg" | "webp")
    } else {
        false
    }
}

fn compress_image_to_avif(
    input_path: &Path,
    output_path: &Path,
    quality: f32,
    speed: u8,
) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    use image::ImageReader;
    use ravif::{Encoder, Img, RGBA8};

    let img = ImageReader::open(input_path)?.decode()?;
    let rgba = img.to_rgba8();
    let (width, height) = (rgba.width() as usize, rgba.height() as usize);

    let pixels: Vec<RGBA8> = rgba
        .pixels()
        .map(|p| RGBA8::new(p[0], p[1], p[2], p[3]))
        .collect();

    let enc = Encoder::new()
        .with_quality(quality.clamp(1.0, 100.0))
        .with_speed(speed.clamp(1, 10))
        .with_num_threads(Some(4));

    let img_ref = Img::new(pixels.as_slice(), width, height);
    let result = enc.encode_rgba(img_ref)?;

    fs::write(output_path, result.avif_file)?;
    Ok(())
}

fn scan_compress_files(watch_path: &str) -> Vec<PathBuf> {
    let mut files = Vec::new();
    if watch_path.is_empty() || !Path::new(watch_path).exists() {
        return files;
    }
    let entries = match fs::read_dir(watch_path) {
        Ok(e) => e,
        Err(_) => return files,
    };
    for entry in entries.flatten() {
        let path = entry.path();
        if path.is_file() && is_image_file(&path) {
            files.push(path);
        }
    }
    files
}

fn run_compress_once(state: &Arc<AppState>) {
    let config = state.compress_config.lock().unwrap().clone();
    if !config.is_enable || config.watch_path.is_empty() {
        return;
    }

    let _watch_path = Path::new(&config.watch_path);
    let output_path = if config.output_path.is_empty() {
        config.watch_path.clone()
    } else {
        config.output_path.clone()
    };

    let mut completed = state.load_checkpoint();
    let files = scan_compress_files(&config.watch_path);
    let total = files.len() as i32;
    *state.compress_total.lock().unwrap() = total;

    let mut done_count = 0i32;
    for file in &files {
        loop {
            let paused = *state.compress_paused.lock().unwrap();
            if !paused {
                break;
            }
            std::thread::sleep(Duration::from_millis(500));
        }

        let file_str = file.to_string_lossy().to_string();
        if completed.contains(&file_str) {
            done_count += 1;
            *state.compress_completed.lock().unwrap() = done_count;
            continue;
        }

        let file_name = match file.file_stem().and_then(|s| s.to_str()) {
            Some(s) => s,
            None => continue,
        };
        let out_file = Path::new(&output_path).join(format!("{}.avif", file_name));

        match compress_image_to_avif(file, &out_file, config.quality, config.speed) {
            Ok(_) => {
                completed.insert(file_str);
                state.save_checkpoint(&completed);
                done_count += 1;
                *state.compress_completed.lock().unwrap() = done_count;
                if config.delete_source {
                    fs::remove_file(file).ok();
                }
            }
            Err(_) => {}
        }
    }
}

#[tauri::command]
fn start_compress_watcher(state: State<Arc<AppState>>) {
    let config = state.compress_config.lock().unwrap().clone();
    if !config.is_enable || config.watch_path.is_empty() {
        return;
    }

    {
        let mut running = state.compress_running.lock().unwrap();
        *running = true;
        let mut paused = state.compress_paused.lock().unwrap();
        *paused = false;
    }

    let state_clone = Arc::clone(&state);

    std::thread::spawn(move || {
        run_compress_once(&state_clone);

        let watch_path = config.watch_path.clone();
        let state_watcher = Arc::clone(&state_clone);

        let mut watcher = match RecommendedWatcher::new(
            move |res: Result<Event, notify::Error>| {
                if let Ok(event) = res {
                    if matches!(event.kind, notify::EventKind::Create(_)) {
                        std::thread::sleep(Duration::from_millis(500));
                        run_compress_once(&state_watcher);
                    }
                }
            },
            Config::default(),
        ) {
            Ok(w) => w,
            Err(_) => return,
        };

        watcher.watch(Path::new(&watch_path), RecursiveMode::NonRecursive).ok();
        *state_clone.compress_watcher.lock().unwrap() = Some(watcher);

        loop {
            std::thread::sleep(Duration::from_secs(5));
            let is_running = *state_clone.compress_running.lock().unwrap();
            if !is_running {
                break;
            }
        }
    });
}

#[tauri::command]
fn stop_compress_watcher(state: State<Arc<AppState>>) {
    {
        let mut running = state.compress_running.lock().unwrap();
        *running = false;
    }
    {
        let mut watcher = state.compress_watcher.lock().unwrap();
        *watcher = None;
    }
    {
        let mut total = state.compress_total.lock().unwrap();
        *total = 0;
    }
    {
        let mut completed = state.compress_completed.lock().unwrap();
        *completed = 0;
    }
}

// ==================== FTP 服务器 ====================

use async_trait::async_trait;
use libunftp::ServerBuilder;
use libunftp::notification::{DataEvent, DataListener, EventMeta, PresenceEvent, PresenceListener};
use unftp_core::auth::{Authenticator, AuthenticationError, Credentials, Principal, UserDetail};
use unftp_core::storage::{Error as FtpError, ErrorKind as FtpErrorKind, Fileinfo, Metadata as FtpMetadata, StorageBackend};
use unftp_sbe_fs::{Filesystem, Meta as FtpFsMeta};

/// 自动递归创建父目录的 FTP 文件系统后端
#[derive(Debug)]
struct AutoCreateFilesystem {
    inner: Filesystem,
    root: PathBuf,
}

impl AutoCreateFilesystem {
    fn new<P: Into<PathBuf>>(root: P) -> io::Result<Self> {
        let root = root.into();
        let inner = Filesystem::new(&root)?;
        Ok(Self { inner, root })
    }
}

fn strip_prefixes(path: &Path) -> &Path {
    if path == Path::new("/") {
        Path::new(".")
    } else {
        path.strip_prefix("/").unwrap_or(path)
    }
}

#[async_trait]
impl<User: UserDetail> StorageBackend<User> for AutoCreateFilesystem {
    type Metadata = FtpFsMeta;

    fn enter(&mut self, user_detail: &User) -> io::Result<()> {
        self.inner.enter(user_detail)
    }

    fn supported_features(&self) -> u32 {
        <Filesystem as StorageBackend<User>>::supported_features(&self.inner)
    }

    async fn metadata<P: AsRef<Path> + Send + Debug>(
        &self,
        user: &User,
        path: P,
    ) -> unftp_core::storage::Result<Self::Metadata> {
        self.inner.metadata(user, path).await
    }

    async fn list<P: AsRef<Path> + Send + Debug>(
        &self,
        user: &User,
        path: P,
    ) -> unftp_core::storage::Result<Vec<Fileinfo<PathBuf, Self::Metadata>>>
    where
        <Self as StorageBackend<User>>::Metadata: FtpMetadata,
    {
        self.inner.list(user, path).await
    }

    async fn get<P: AsRef<Path> + Send + Debug>(
        &self,
        user: &User,
        path: P,
        start_pos: u64,
    ) -> unftp_core::storage::Result<Box<dyn tokio::io::AsyncRead + Send + Sync + Unpin>> {
        self.inner.get(user, path, start_pos).await
    }

    async fn put<P: AsRef<Path> + Send + Debug, R: tokio::io::AsyncRead + Send + Sync + Unpin + 'static>(
        &self,
        user: &User,
        input: R,
        path: P,
        start_pos: u64,
    ) -> unftp_core::storage::Result<u64> {
        let stripped = strip_prefixes(path.as_ref());
        let abs = self.root.join(stripped);
        if let Some(parent) = abs.parent() {
            if !parent.starts_with(&self.root) {
                return Err(FtpError::from(FtpErrorKind::PermanentFileNotAvailable));
            }
            tokio::fs::create_dir_all(parent)
                .await
                .map_err(|e| FtpError::new(FtpErrorKind::LocalError, e))?;
        }
        self.inner.put(user, input, path, start_pos).await
    }

    async fn del<P: AsRef<Path> + Send + Debug>(
        &self,
        user: &User,
        path: P,
    ) -> unftp_core::storage::Result<()> {
        self.inner.del(user, path).await
    }

    async fn mkd<P: AsRef<Path> + Send + Debug>(
        &self,
        user: &User,
        path: P,
    ) -> unftp_core::storage::Result<()> {
        self.inner.mkd(user, path).await
    }

    async fn rename<P: AsRef<Path> + Send + Debug>(
        &self,
        user: &User,
        from: P,
        to: P,
    ) -> unftp_core::storage::Result<()> {
        self.inner.rename(user, from, to).await
    }

    async fn rmd<P: AsRef<Path> + Send + Debug>(
        &self,
        user: &User,
        path: P,
    ) -> unftp_core::storage::Result<()> {
        self.inner.rmd(user, path).await
    }

    async fn cwd<P: AsRef<Path> + Send + Debug>(
        &self,
        user: &User,
        path: P,
    ) -> unftp_core::storage::Result<()> {
        self.inner.cwd(user, path).await
    }
}

#[derive(Debug)]
struct SimpleAuthenticator {
    users: HashMap<String, String>,
}

#[async_trait]
impl Authenticator for SimpleAuthenticator {
    async fn authenticate(
        &self,
        username: &str,
        creds: &Credentials,
    ) -> Result<Principal, AuthenticationError> {
        if let Some(password) = self.users.get(username) {
            if password.is_empty() {
                return Ok(Principal { username: username.to_string() });
            }
            if let Some(ref provided) = creds.password {
                if provided == password {
                    return Ok(Principal { username: username.to_string() });
                }
            }
        }
        Err(AuthenticationError::BadPassword)
    }
}

#[derive(Debug)]
struct FtpLogListener {
    log_buffer: Arc<Mutex<Vec<FtpLogEntry>>>,
}

impl FtpLogListener {
    fn new(log_buffer: Arc<Mutex<Vec<FtpLogEntry>>>) -> Self {
        Self { log_buffer }
    }

    fn add_log(&self, level: &str, message: &str) {
        let entry = FtpLogEntry {
            timestamp: Local::now().format("%H:%M:%S").to_string(),
            level: level.to_string(),
            message: message.to_string(),
        };
        {
            let mut buffer = self.log_buffer.lock().unwrap();
            buffer.push(entry);
            if buffer.len() > 1000 {
                buffer.remove(0);
            }
        }
    }
}

#[async_trait]
impl PresenceListener for FtpLogListener {
    async fn receive_presence_event(&self, event: PresenceEvent, meta: EventMeta) {
        let msg = match event {
            PresenceEvent::LoggedIn => {
                format!("[{}] 用户 '{}' 登录", meta.trace_id, meta.username)
            }
            PresenceEvent::LoggedOut => {
                format!("[{}] 用户 '{}' 退出", meta.trace_id, meta.username)
            }
        };
        self.add_log("INFO", &msg);
    }
}

#[async_trait]
impl DataListener for FtpLogListener {
    async fn receive_data_event(&self, event: DataEvent, meta: EventMeta) {
        let msg = match event {
            DataEvent::Put { path, bytes } => {
                format!("[{}] 上传 '{}' ({} bytes)", meta.trace_id, path, bytes)
            }
            DataEvent::Got { path, bytes } => {
                format!("[{}] 下载 '{}' ({} bytes)", meta.trace_id, path, bytes)
            }
            DataEvent::Deleted { path } => {
                format!("[{}] 删除 '{}'", meta.trace_id, path)
            }
            DataEvent::MadeDir { path } => {
                format!("[{}] 创建目录 '{}'", meta.trace_id, path)
            }
            DataEvent::RemovedDir { path } => {
                format!("[{}] 删除目录 '{}'", meta.trace_id, path)
            }
            DataEvent::Renamed { from, to } => {
                format!("[{}] 重命名 '{}' -> '{}'", meta.trace_id, from, to)
            }
        };
        self.add_log("INFO", &msg);
    }
}

#[tauri::command]
fn start_ftp_server(app: AppHandle, state: State<Arc<AppState>>) {
    let config = state.ftp_config.lock().unwrap().clone();
    if !config.is_enable {
        return;
    }

    {
        let mut running = state.ftp_running.lock().unwrap();
        if *running {
            return;
        }
        *running = true;
    }

    let mut users = HashMap::new();
    for user in &config.users {
        users.insert(user.username.clone(), user.password.clone());
    }

    let root_dir = config
        .users
        .first()
        .map(|u| u.root_dir.clone())
        .unwrap_or_else(|| "D:/CCD图片".to_string());
    let root_path = PathBuf::from(root_dir);
    fs::create_dir_all(&root_path).ok();

    let log_buffer = state.ftp_log_buffer.clone();
    let _app_handle = app.clone();
    let port = config.port;
    let state_clone = Arc::clone(&state);

    std::thread::spawn(move || {
        let rt = match tokio::runtime::Runtime::new() {
            Ok(rt) => rt,
            Err(e) => {
                state_clone.add_ftp_log("ERROR", &format!("创建 tokio runtime 失败: {}", e));
                let mut running = state_clone.ftp_running.lock().unwrap();
                *running = false;
                return;
            }
        };

        rt.block_on(async {
            let addr = format!("0.0.0.0:{}", port);
            let log_buffer_presence = log_buffer.clone();
            let log_buffer_data = log_buffer.clone();

            let presence_listener: Arc<dyn PresenceListener> = Arc::new(FtpLogListener::new(log_buffer_presence));
            let data_listener: Arc<dyn DataListener> = Arc::new(FtpLogListener::new(log_buffer_data));

            let server = ServerBuilder::new(Box::new(move || {
                AutoCreateFilesystem::new(&root_path).unwrap()
            }))
            .greeting("Welcome to CameraViewer FTP Server")
            .authenticator(Arc::new(SimpleAuthenticator { users }))
            .passive_ports(50000..=65535)
            .idle_session_timeout(600)
            .notify_presence(presence_listener)
            .notify_data(data_listener)
            .build()
            .unwrap();

            state_clone.add_ftp_log(
                "INFO",
                &format!("FTP 服务器启动在端口 {}", port),
            );

            if let Err(e) = server.listen(&addr).await {
                state_clone.add_ftp_log("ERROR", &format!("FTP 服务器错误: {}", e));
            }

            let mut running = state_clone.ftp_running.lock().unwrap();
            *running = false;
        });
    });
}

#[tauri::command]
fn stop_ftp_server(state: State<Arc<AppState>>) {
    let mut running = state.ftp_running.lock().unwrap();
    *running = false;
}

// ==================== NTP 服务器 ====================

const NTP_EPOCH_DELTA: i64 = 2208988800;

fn ntp_timestamp_utc8() -> (u32, u32) {
    use chrono::Utc;
    let now = Utc::now() + chrono::Duration::hours(8);
    let seconds = (now.timestamp() + NTP_EPOCH_DELTA) as u32;
    let fraction = ((now.timestamp_subsec_nanos() as u64) << 32) / 1_000_000_000;
    (seconds, fraction as u32)
}

fn encode_ntp_timestamp(buf: &mut [u8], offset: usize, seconds: u32, fraction: u32) {
    buf[offset] = (seconds >> 24) as u8;
    buf[offset + 1] = (seconds >> 16) as u8;
    buf[offset + 2] = (seconds >> 8) as u8;
    buf[offset + 3] = seconds as u8;
    buf[offset + 4] = (fraction >> 24) as u8;
    buf[offset + 5] = (fraction >> 16) as u8;
    buf[offset + 6] = (fraction >> 8) as u8;
    buf[offset + 7] = fraction as u8;
}

fn run_ntp_server(port: u16, state: Arc<AppState>) {
    use std::net::UdpSocket;

    let addr = format!("0.0.0.0:{}", port);
    let socket = match UdpSocket::bind(&addr) {
        Ok(s) => s,
        Err(e) => {
            state.add_ntp_log(
                "ERROR",
                &format!("NTP 服务器绑定端口 {} 失败: {}", port, e),
            );
            let mut running = state.ntp_running.lock().unwrap();
            *running = false;
            return;
        }
    };

    if let Err(e) = socket.set_read_timeout(Some(Duration::from_millis(200))) {
        state.add_ntp_log("ERROR", &format!("NTP 服务器设置超时失败: {}", e));
    }

    state.add_ntp_log("INFO", &format!("NTP 服务器启动在端口 {}", port));

    let mut buf = [0u8; 1024];
    loop {
        let is_running = *state.ntp_running.lock().unwrap();
        if !is_running {
            break;
        }

        match socket.recv_from(&mut buf) {
            Ok((len, src)) => {
                if len < 48 {
                    continue;
                }

                let client_mode = buf[0] & 0x07;
                if client_mode != 3 {
                    // 仅响应客户端模式请求
                    continue;
                }

                let (ref_secs, ref_frac) = ntp_timestamp_utc8();
                let (recv_secs, recv_frac) = ntp_timestamp_utc8();
                let (tx_secs, tx_frac) = ntp_timestamp_utc8();

                let mut resp = [0u8; 48];
                // LI=0, VN=3, Mode=4 (server)
                resp[0] = 0x1C;
                resp[1] = 1; // stratum
                resp[2] = 0; // poll
                resp[3] = 0xFA; // precision ~ -6
                // root delay
                resp[4] = 0;
                resp[5] = 0;
                resp[6] = 0;
                resp[7] = 0;
                // root dispersion
                resp[8] = 0;
                resp[9] = 0;
                resp[10] = 0;
                resp[11] = 0;
                // reference id "LOCL"
                resp[12] = b'L';
                resp[13] = b'O';
                resp[14] = b'C';
                resp[15] = b'L';

                encode_ntp_timestamp(&mut resp, 16, ref_secs, ref_frac);
                // origin timestamp = client transmit timestamp (bytes 40-47 of request)
                resp[24..32].copy_from_slice(&buf[40..48]);
                encode_ntp_timestamp(&mut resp, 32, recv_secs, recv_frac);
                encode_ntp_timestamp(&mut resp, 40, tx_secs, tx_frac);

                if let Err(e) = socket.send_to(&resp, src) {
                    state.add_ntp_log("ERROR", &format!("NTP 响应发送失败: {}", e));
                } else {
                    state.add_ntp_log("INFO", &format!("NTP 收到来自 {} 的请求", src));
                }
            }
            Err(e) => {
                if e.kind() != std::io::ErrorKind::WouldBlock
                    && e.kind() != std::io::ErrorKind::TimedOut
                {
                    state.add_ntp_log("ERROR", &format!("NTP 接收失败: {}", e));
                }
            }
        }
    }

    state.add_ntp_log("INFO", "NTP 服务器已停止");
}

#[tauri::command]
fn start_ntp_server(state: State<Arc<AppState>>) {
    let config = state.ntp_config.lock().unwrap().clone();
    if !config.is_enable {
        return;
    }

    {
        let mut running = state.ntp_running.lock().unwrap();
        if *running {
            return;
        }
        *running = true;
    }

    let port = config.port;
    let state_clone = Arc::clone(&state);

    std::thread::spawn(move || {
        run_ntp_server(port, state_clone);
    });
}

#[tauri::command]
fn stop_ntp_server(state: State<Arc<AppState>>) {
    let mut running = state.ntp_running.lock().unwrap();
    *running = false;
}

#[tauri::command]
fn test_ntp_server(host_port: String) -> String {
    use std::process::Command;

    let output = Command::new("w32tm")
        .args(&[
            "/stripchart",
            &format!("/computer:{}", host_port),
            "/samples:1",
            "/dataonly",
        ])
        .output();

    match output {
        Ok(out) => {
            let stdout = String::from_utf8_lossy(&out.stdout).trim().to_string();
            let stderr = String::from_utf8_lossy(&out.stderr).trim().to_string();
            if !stderr.is_empty() {
                format!("{}", stderr)
            } else if !stdout.is_empty() {
                stdout
            } else {
                "w32tm 没有返回输出".to_string()
            }
        }
        Err(e) => format!("执行 w32tm 失败: {}", e),
    }
}

// ==================== 配置存储位置 ====================

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct ConfigStorageInfo {
    /// default | portable | custom
    mode: String,
    /// 当前实际使用的配置目录
    config_dir: String,
    /// 系统默认目录（AppData）
    default_dir: String,
    /// 便携模式目录（exe 同目录\Configs）
    portable_dir: String,
    /// exe 所在目录
    exe_dir: String,
    /// 便携标记文件是否存在
    marker_exists: bool,
}

#[tauri::command]
fn get_config_storage_info(
    app_handle: AppHandle,
    state: State<Arc<AppState>>,
) -> ConfigStorageInfo {
    let exe = exe_dir();
    let portable_dir = exe
        .as_ref()
        .map(|d| d.join(PORTABLE_CONFIG_DIR))
        .unwrap_or_default();
    let marker_exists = exe
        .as_ref()
        .map(|d| d.join(PORTABLE_MARKER).exists())
        .unwrap_or(false);
    let default_dir = app_handle
        .path()
        .app_local_data_dir()
        .unwrap_or_default();

    ConfigStorageInfo {
        mode: state.config_mode.clone(),
        config_dir: state.config_dir.to_string_lossy().to_string(),
        default_dir: default_dir.to_string_lossy().to_string(),
        portable_dir: portable_dir.to_string_lossy().to_string(),
        exe_dir: exe
            .map(|d| d.to_string_lossy().to_string())
            .unwrap_or_default(),
        marker_exists,
    }
}

/// 切换配置存储位置。
/// portable=true：在 exe 同目录创建 portable.txt 标记，配置改用 .\Configs；
/// portable=false：删除标记，恢复系统默认目录。
/// migrate=true 时将当前配置目录内容复制到目标目录（合并、同名覆盖）。
/// 切换需重启程序后生效。
#[tauri::command]
fn switch_config_storage(
    app_handle: AppHandle,
    state: State<Arc<AppState>>,
    portable: bool,
    migrate: bool,
) -> Result<String, String> {
    if state.config_mode == "custom" {
        return Err(
            "当前配置目录由命令行参数 --config-dir 指定，无法通过界面切换，请修改启动参数后重启"
                .to_string(),
        );
    }

    let exe = exe_dir().ok_or_else(|| "无法获取程序所在目录".to_string())?;
    let marker = exe.join(PORTABLE_MARKER);

    if portable {
        if state.config_mode == "portable" && marker.exists() {
            return Err("当前已经是便携模式".to_string());
        }
        let target = exe.join(PORTABLE_CONFIG_DIR);
        fs::create_dir_all(&target)
            .map_err(|e| format!("创建程序目录下 Configs 文件夹失败（可能没有写入权限）: {e}"))?;

        let mut copied = 0u64;
        if migrate {
            copied = copy_dir_contents(&state.config_dir, &target)
                .map_err(|e| format!("复制配置文件失败: {e}"))?;
        }

        // 写入标记文件本身即可验证 exe 目录可写
        const MARKER_NOTE: &str = concat!(
            "此文件存在时，CameraViewerTauri 使用程序同目录的 Configs 文件夹保存配置（便携模式）。\n",
            "删除本文件后重启程序，配置将恢复保存到系统默认目录（AppData）。\n",
            "Portable mode marker: while this file exists, configs are stored in .\\Configs next to the executable.\n",
            "Delete this file and restart to restore the default storage location.\n"
        );
        fs::write(&marker, MARKER_NOTE)
            .map_err(|e| format!("写入便携标记文件失败（程序目录可能不可写）: {e}"))?;

        Ok(format!(
            "已切换为便携模式，配置目录：{}\n{}重启程序后生效。",
            target.display(),
            if migrate {
                format!("已复制 {copied} 个配置文件。\n")
            } else {
                String::new()
            }
        ))
    } else {
        if state.config_mode == "default" && !marker.exists() {
            return Err("当前已经使用默认目录".to_string());
        }
        let default_dir = app_handle
            .path()
            .app_local_data_dir()
            .map_err(|e| format!("获取系统默认目录失败: {e}"))?;
        fs::create_dir_all(&default_dir)
            .map_err(|e| format!("创建默认目录失败: {e}"))?;

        let mut copied = 0u64;
        if migrate && state.config_mode == "portable" {
            copied = copy_dir_contents(&state.config_dir, &default_dir)
                .map_err(|e| format!("复制配置文件失败: {e}"))?;
        }

        if marker.exists() {
            fs::remove_file(&marker)
                .map_err(|e| format!("删除便携标记文件失败: {e}"))?;
        }

        Ok(format!(
            "已切换为系统默认目录：{}\n{}重启程序后生效。",
            default_dir.display(),
            if migrate && copied > 0 {
                format!("已复制 {copied} 个配置文件。\n")
            } else {
                String::new()
            }
        ))
    }
}

#[tauri::command]
fn restart_app(app_handle: AppHandle) {
    app_handle.restart();
}

// ==================== 启动画面（官方 splashscreen 方式，Tauri 2） ====================

/// 主界面就绪后由前端调用：关闭 splashscreen 独立窗口并显示主窗口。
/// 幂等：窗口不存在或操作失败均忽略，重复调用安全。
#[tauri::command]
async fn close_splashscreen(app: AppHandle) {
    if let Some(splash) = app.get_webview_window("splashscreen") {
        let _ = splash.close();
    }
    if let Some(main_win) = app.get_webview_window("main") {
        let _ = main_win.show();
        let _ = main_win.set_focus();
    }
}

/// 打开设置页时调用：动态创建设置加载独立窗口（与启动画面相同的 splashscreen 技术）。
/// 窗口为 420×240 无边框、置顶、不进任务栏、屏幕居中的纯静态页 settings-splash.html。
/// 同步命令：窗口创建必须在主线程完成；幂等，窗口已存在时直接返回。
#[tauri::command]
fn show_settings_splash(app: AppHandle) -> Result<(), String> {
    if app.get_webview_window("settings-splash").is_some() {
        return Ok(());
    }
    WebviewWindowBuilder::new(
        &app,
        "settings-splash",
        WebviewUrl::App("settings-splash.html".into()),
    )
    .title("")
    .inner_size(420.0, 240.0)
    .resizable(false)
    .decorations(false)
    .always_on_top(true)
    .skip_taskbar(true)
    .center()
    // 窗口原生底色与 settings-splash.html 的 html/body 背景色一致（#1b1b1f），
    // 消除窗口创建到 webview 首帧绘制之间的白屏期（与主窗、启动画面同色）
    .background_color(tauri::webview::Color(0x1b, 0x1b, 0x1f, 0xff))
    .build()
    .map(|_| ())
    .map_err(|e| format!("创建设置加载窗口失败: {e}"))
}

/// 设置状态全部加载完成后由前端调用：关闭设置加载窗口并把焦点交还主窗口。幂等。
#[tauri::command]
async fn close_settings_splash(app: AppHandle) {
    if let Some(win) = app.get_webview_window("settings-splash") {
        let _ = win.close();
    }
    if let Some(main_win) = app.get_webview_window("main") {
        let _ = main_win.set_focus();
    }
}

// ==================== 防火墙控制 ====================

#[tauri::command]
fn get_firewall_status() -> Result<bool, String> {
    use std::process::Command;

    let output = Command::new("netsh")
        .args(&["advfirewall", "show", "allprofiles", "state"])
        .output()
        .map_err(|e| format!("查询防火墙状态失败: {}", e))?;

    let stdout = String::from_utf8_lossy(&output.stdout);
    let mut any_on = false;
    let mut any_off = false;
    for line in stdout.lines() {
        let lower = line.to_lowercase();
        if lower.contains("state") || lower.contains("状态") {
            if lower.contains("on") {
                any_on = true;
            } else if lower.contains("off") {
                any_off = true;
            }
        }
    }
    Ok(any_on && !any_off)
}

#[tauri::command]
fn set_firewall_status(enabled: bool) -> Result<String, String> {
    use elevated_command::Command as ElevatedCommand;
    use std::process::Command as StdCommand;
    use std::{thread, time::Duration};

    let state = if enabled { "on" } else { "off" };
    let mut cmd = StdCommand::new("netsh");
    cmd.args(&["advfirewall", "set", "allprofiles", "state", state]);

    // 使用 elevated-command crate 触发系统 UAC 提权弹窗
    let elevated = ElevatedCommand::new(cmd);
    let output = elevated
        .output()
        .map_err(|e| format!("启动提权进程失败: {}", e))?;

    // Windows ShellExecuteW 返回值 > 32 表示成功启动提升后的进程
    let code = output.status.code().unwrap_or(0);
    if code <= 32 {
        return Err("UAC 提权失败或用户取消了权限请求".to_string());
    }

    // 等待并轮询防火墙状态，最多 6 秒
    let expected = enabled;
    for _ in 0..30 {
        thread::sleep(Duration::from_millis(200));
        if let Ok(current) = get_firewall_status() {
            if current == expected {
                return Ok(format!("防火墙已{}", if enabled { "开启" } else { "关闭" }));
            }
        }
    }

    Err("防火墙状态切换超时，请手动刷新后查看".to_string())
}

// ==================== UAC 控制 ====================

#[tauri::command]
fn get_uac_status() -> Result<bool, String> {
    use winreg::enums::*;
    use winreg::RegKey;

    let hklm = RegKey::predef(HKEY_LOCAL_MACHINE);
    let key = hklm
        .open_subkey_with_flags(
            r"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
            KEY_READ,
        )
        .map_err(|e| format!("打开 UAC 注册表项失败: {}", e))?;
    let value: u32 = key
        .get_value("EnableLUA")
        .map_err(|e| format!("读取 EnableLUA 失败: {}", e))?;
    Ok(value != 0)
}

#[tauri::command]
fn set_uac_status(enabled: bool) -> Result<String, String> {
    use elevated_command::Command as ElevatedCommand;
    use std::process::Command as StdCommand;
    use std::{thread, time::Duration};

    let value = if enabled { "1" } else { "0" };
    let mut cmd = StdCommand::new("reg.exe");
    cmd.args(&[
        "add",
        r"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
        "/v",
        "EnableLUA",
        "/t",
        "REG_DWORD",
        "/d",
        value,
        "/f",
    ]);

    // 使用 elevated-command crate 触发系统 UAC 提权弹窗
    let elevated = ElevatedCommand::new(cmd);
    let output = elevated
        .output()
        .map_err(|e| format!("启动提权进程失败: {}", e))?;

    // Windows ShellExecuteW 返回值 > 32 表示成功启动提升后的进程
    let code = output.status.code().unwrap_or(0);
    if code <= 32 {
        return Err("UAC 提权失败或用户取消了权限请求".to_string());
    }

    // 等待并轮询注册表状态，最多 6 秒
    let expected = enabled;
    for _ in 0..30 {
        thread::sleep(Duration::from_millis(200));
        if let Ok(current) = get_uac_status() {
            if current == expected {
                return Ok(format!(
                    "UAC 弹窗已{}，需要【重启电脑】才能完全生效。",
                    if enabled { "开启" } else { "关闭" }
                ));
            }
        }
    }

    Err("UAC 状态切换超时，请手动刷新后查看".to_string())
}

// ==================== 主入口 ====================

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_dialog::init())
        .setup(|app| {
            let state = Arc::new(AppState::new(&app.handle()));
            app.manage(state.clone());

            // 主窗口改为代码创建（而非 tauri.conf.json 静态声明），以便通过
            // initialization_script 把 HMI 网页 i18n 脚本注入主 webview 的所有 frame
            // （含跨域相机 iframe；同源策略禁止父页面改写跨域 DOM，宿主层注入不受此限）。
            // 窗口参数与原 conf.json 声明保持一致：1200x800、最大化、居中、初始隐藏。
            WebviewWindowBuilder::new(
                app.handle(),
                "main",
                WebviewUrl::App("index.html".into()),
            )
            .title("CameraViewerTauri")
            .inner_size(1200.0, 800.0)
            .resizable(true)
            .fullscreen(false)
            .maximized(true)
            .center()
            .visible(false)
            .background_color(tauri::webview::Color(0x1b, 0x1b, 0x1f, 0xff))
            .initialization_script(include_str!("../../src/assets/hmi-i18n.js"))
            .build()?;

            // 启动画面兜底：若前端因异常始终未调用 close_splashscreen，
            // 15 秒后强制显示主窗口并关闭 splashscreen，避免界面永久不可见
            {
                let handle = app.handle().clone();
                std::thread::spawn(move || {
                    std::thread::sleep(Duration::from_secs(15));
                    if let Some(splash) = handle.get_webview_window("splashscreen") {
                        if splash.is_visible().unwrap_or(false) {
                            eprintln!("[splash] 启动超时 15s，强制关闭启动画面");
                            let _ = splash.close();
                        }
                    }
                    if let Some(main_win) = handle.get_webview_window("main") {
                        let _ = main_win.show();
                    }
                });
            }

            // 创建窗口镜像状态与镜像窗口
            #[cfg(windows)]
            {
                let mirror_state = Arc::new(MirrorState::new());
                display::set_global_mirror_state(mirror_state.clone());
                app.manage(mirror_state.clone());
                match unsafe { display::create_mirror_window() } {
                    Ok(hwnd) => {
                        mirror_state.set_mirror_hwnd(hwnd.0 as usize);
                    }
                    Err(e) => {
                        eprintln!("创建镜像窗口失败: {e}");
                    }
                }
            }
            #[cfg(not(windows))]
            {
                app.manage(Arc::new(MirrorState::new()));
            }

            // 创建虚拟显示器状态
            app.manage(Arc::new(VirtualDisplayState::new()));

            // 创建远程指令状态
            app.manage(state.remote_command_state.clone());

            // 创建托盘图标与右键菜单
            let show_i = MenuItem::with_id(app.handle(), "show", "显示界面", true, None::<&str>)?;
            let about_i = MenuItem::with_id(app.handle(), "about", "关于", true, None::<&str>)?;
            let quit_i = MenuItem::with_id(app.handle(), "quit", "退出", true, None::<&str>)?;
            let menu = Menu::with_items(
                app.handle(),
                &[
                    &show_i,
                    &about_i,
                    &PredefinedMenuItem::separator(app.handle())?,
                    &quit_i,
                ],
            )?;

            let mut tray_builder = TrayIconBuilder::with_id("main-tray")
                .menu(&menu)
                .on_menu_event(move |app, event| match event.id().as_ref() {
                    "show" => {
                        if let Some(window) = app.get_webview_window("main") {
                            let _ = window.show();
                            let _ = window.set_focus();
                        }
                    }
                    "about" => {
                        if let Some(window) = app.get_webview_window("main") {
                            let _ = window.show();
                            let _ = window.set_focus();
                        }
                        let _ = app.emit("show_about", ());
                    }
                    "quit" => {
                        app.exit(0);
                    }
                    _ => {}
                });
            if let Some(icon) = app.default_window_icon().cloned() {
                tray_builder = tray_builder.icon(icon);
            }
            tray_builder.build(app.handle())?;

            // 启动文件监控
            let state_clone = state.clone();
            std::thread::spawn(move || {
                std::thread::sleep(Duration::from_secs(1));
                let configs = state_clone.transform_configs.lock().unwrap().clone();
                let mut handles = state_clone.watcher_handles.lock().unwrap();
                for config in configs {
                    let watch_path = config.watch_path.clone();
                    if watch_path.is_empty() || !Path::new(&watch_path).exists() {
                        continue;
                    }
                    let config_clone = config.clone();
                    if let Ok(mut watcher) = RecommendedWatcher::new(
                        move |res: Result<Event, notify::Error>| {
                            if let Ok(event) = res {
                                if matches!(event.kind, notify::EventKind::Create(_)) {
                                    std::thread::sleep(Duration::from_millis(500));
                                    run_transform_action(&config_clone);
                                }
                            }
                        },
                        Config::default(),
                    ) {
                        watcher.watch(Path::new(&watch_path), RecursiveMode::NonRecursive).ok();
                        handles.insert(config.name, watcher);
                    }
                }
            });

            // 启动清理器
            let app_handle = app.handle().clone();
            let state_clone = state.clone();
            std::thread::spawn(move || {
                let config = state_clone.clean_config.lock().unwrap().clone();
                if !config.is_enable {
                    return;
                }
                let interval_hours = config.scan_interval.max(0.1);
                let sleep_secs = (interval_hours * 3600.0) as u64;
                loop {
                    std::thread::sleep(Duration::from_secs(sleep_secs));
                    let is_running = *state_clone.cleaner_running.lock().unwrap();
                    if !is_running {
                        break;
                    }
                    let cfg = state_clone.clean_config.lock().unwrap().clone();
                    if cfg.is_enable {
                        clean_once(&cfg, &app_handle);
                    }
                }
            });

            // 启动图像压缩监控
            let state_clone = state.clone();
            std::thread::spawn(move || {
                std::thread::sleep(Duration::from_secs(2));
                let config = state_clone.compress_config.lock().unwrap().clone();
                if !config.is_enable || config.watch_path.is_empty() {
                    return;
                }

                {
                    let mut running = state_clone.compress_running.lock().unwrap();
                    *running = true;
                }

                run_compress_once(&state_clone);

                let watch_path = config.watch_path.clone();
                let state_watcher = Arc::clone(&state_clone);

                let mut watcher = match RecommendedWatcher::new(
                    move |res: Result<Event, notify::Error>| {
                        if let Ok(event) = res {
                            if matches!(event.kind, notify::EventKind::Create(_)) {
                                std::thread::sleep(Duration::from_millis(500));
                                run_compress_once(&state_watcher);
                            }
                        }
                    },
                    Config::default(),
                ) {
                    Ok(w) => w,
                    Err(_) => return,
                };

                watcher.watch(Path::new(&watch_path), RecursiveMode::NonRecursive).ok();
                *state_clone.compress_watcher.lock().unwrap() = Some(watcher);

                loop {
                    std::thread::sleep(Duration::from_secs(5));
                    let is_running = *state_clone.compress_running.lock().unwrap();
                    if !is_running {
                        break;
                    }
                }
            });

            // 启动 FTP 服务器
            let _app_handle = app.handle().clone();
            let state_clone = state.clone();
            std::thread::spawn(move || {
                std::thread::sleep(Duration::from_secs(3));
                let config = state_clone.ftp_config.lock().unwrap().clone();
                if !config.is_enable {
                    return;
                }

                {
                    let mut running = state_clone.ftp_running.lock().unwrap();
                    if *running {
                        return;
                    }
                    *running = true;
                }

                let mut users = HashMap::new();
                for user in &config.users {
                    users.insert(user.username.clone(), user.password.clone());
                }

                let root_dir = config
                    .users
                    .first()
                    .map(|u| u.root_dir.clone())
                    .unwrap_or_else(|| "D:/CCD图片".to_string());
                let root_path = PathBuf::from(root_dir);
                fs::create_dir_all(&root_path).ok();

                let log_buffer = state_clone.ftp_log_buffer.clone();
                let port = config.port;
                let state_ftp = Arc::clone(&state_clone);

                let rt = match tokio::runtime::Runtime::new() {
                    Ok(rt) => rt,
                    Err(e) => {
                        state_ftp.add_ftp_log("ERROR", &format!("创建 tokio runtime 失败: {}", e));
                        let mut running = state_ftp.ftp_running.lock().unwrap();
                        *running = false;
                        return;
                    }
                };

                rt.block_on(async {
                    let addr = format!("0.0.0.0:{}", port);
                    let log_buffer_presence = log_buffer.clone();
                    let log_buffer_data = log_buffer.clone();

                    let presence_listener: Arc<dyn PresenceListener> = Arc::new(FtpLogListener::new(log_buffer_presence));
                    let data_listener: Arc<dyn DataListener> = Arc::new(FtpLogListener::new(log_buffer_data));

                    let server = ServerBuilder::new(Box::new(move || {
                        AutoCreateFilesystem::new(&root_path).unwrap()
                    }))
                    .greeting("Welcome to CameraViewer FTP Server")
                    .authenticator(Arc::new(SimpleAuthenticator { users }))
                    .passive_ports(50000..=65535)
                    .idle_session_timeout(600)
                    .notify_presence(presence_listener)
                    .notify_data(data_listener)
                    .build()
                    .unwrap();

                    state_ftp.add_ftp_log(
                        "INFO",
                        &format!("FTP 服务器启动在端口 {}", port),
                    );

                    if let Err(e) = server.listen(&addr).await {
                        state_ftp.add_ftp_log("ERROR", &format!("FTP 服务器错误: {}", e));
                    }

                    let mut running = state_ftp.ftp_running.lock().unwrap();
                    *running = false;
                });
            });

            // 启动 NTP 服务器
            let state_clone = state.clone();
            std::thread::spawn(move || {
                std::thread::sleep(Duration::from_secs(4));
                let config = state_clone.ntp_config.lock().unwrap().clone();
                if !config.is_enable {
                    return;
                }

                {
                    let mut running = state_clone.ntp_running.lock().unwrap();
                    if *running {
                        return;
                    }
                    *running = true;
                }

                let port = config.port;
                run_ntp_server(port, state_clone);
            });

            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            get_camera_config,
            set_camera_config,
            update_camera_config,
            update_camera_ip,
            update_camera_lock,
            update_camera_cogsocket_auth,
            get_transform_configs,
            set_transform_configs,
            add_transform_config,
            delete_transform_config,
            get_clean_config,
            set_clean_config,
            get_compress_config,
            set_compress_config,
            get_compress_status,
            pause_compress,
            resume_compress,
            start_compress_watcher,
            stop_compress_watcher,
            get_ftp_config,
            set_ftp_config,
            get_ftp_status,
            get_ftp_logs,
            start_ftp_server,
            stop_ftp_server,
            get_ntp_config,
            set_ntp_config,
            get_ntp_status,
            get_ntp_logs,
            start_ntp_server,
            stop_ntp_server,
            test_ntp_server,
            get_firewall_status,
            set_firewall_status,
            get_uac_status,
            set_uac_status,
            get_config_storage_info,
            switch_config_storage,
            restart_app,
            close_splashscreen,
            show_settings_splash,
            close_settings_splash,
            list_windows,
            get_monitors,
            set_window_monitor,
            set_mirror_window,
            stop_mirror_window,
            set_mirror_opacity,
            set_mirror_click_through,
            set_mirror_fit,
            get_virtual_display_status,
            install_virtual_display_driver,
            list_virtual_displays,
            add_virtual_display,
            remove_virtual_display,
            start_remote_command_server,
            stop_remote_command_server,
            get_remote_command_status,
            get_remote_command_logs,
            get_local_subnet_info,
            scan_lan_devices,
            get_current_time,
            get_app_version,
            get_app_config,
            set_app_config,
            start_file_watchers,
            stop_file_watchers,
            start_cleaner,
            stop_cleaner,
            test_clean_script,
            get_default_clean_script,
            sample_clean_context,
            get_vision_config,
            set_vision_config,
            get_default_vision_script,
            load_vision_image,
            run_vision_inspection,
            get_jobx_backup_config,
            set_jobx_backup_config,
            add_jobx_camera,
            update_jobx_camera,
            delete_jobx_camera,
            backup_jobx_camera,
            backup_all_jobx_cameras,
            get_jobx_backup_logs,
            open_jobx_backup_dir,
        ])
        .on_window_event(|window, event| {
            if let tauri::WindowEvent::CloseRequested { api, .. } = event {
                api.prevent_close();
                let _ = window.hide();
            }
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
