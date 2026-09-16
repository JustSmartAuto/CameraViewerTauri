use std::fs;
use std::io::Write;
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::Duration;

use chrono::Local;
use serde::{Deserialize, Serialize};
use std::io::Cursor;

use suppaftp::native_tls::TlsConnector;
use suppaftp::types::FileType;
use suppaftp::{FtpStream, NativeTlsConnector, NativeTlsFtpStream};
use tauri::State;

use crate::AppState;

// ==================== JOBX 备份数据模型 ====================

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct JobxCameraConfig {
    pub name: String,
    pub ip: String,
    pub ftp_port: u16,
    pub ftp_username: String,
    pub ftp_password: String,
    pub backup_directory: String,
    pub ftps_enabled: bool,
    pub trust_all_certs: bool,
}

impl Default for JobxCameraConfig {
    fn default() -> Self {
        Self {
            name: String::new(),
            ip: String::new(),
            ftp_port: 21,
            ftp_username: "admin".to_string(),
            ftp_password: String::new(),
            backup_directory: String::new(),
            ftps_enabled: false,
            trust_all_certs: true,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
pub struct JobxBackupConfig {
    pub cameras: Vec<JobxCameraConfig>,
}

#[derive(Debug, Clone, Serialize)]
pub struct JobxBackupLogEntry {
    pub timestamp: String,
    pub level: String,
    pub message: String,
}

#[derive(Debug, Clone, Serialize)]
pub struct JobxBackupResult {
    pub success: bool,
    pub message: String,
    pub backup_path: Option<String>,
}

// ==================== FTP 连接 ====================

fn translate_ftp_error(err: &str) -> String {
    let lower = err.to_lowercase();
    if lower.contains("connection closed without indication") {
        return format!(
            "备份失败: 连接被服务器意外关闭。可能原因：\n\
             1. 相机未启用 FTP/FTPS 服务\n\
             2. 连接端口不正确（Cognex 默认 FTP:21, FTPS:990）\n\
             3. 相机要求 FTPS 加密连接，请在相机配置中勾选\"使用 FTPS\"\n\
             4. 网络或防火墙阻止了连接\n\
             原始错误: {}",
            err
        );
    }
    if lower.contains("connection refused") {
        return format!(
            "备份失败: 连接被拒绝。请检查 IP 地址和端口是否正确，以及相机是否在线。\n原始错误: {}",
            err
        );
    }
    if lower.contains("timed out") || lower.contains("timeout") {
        return format!(
            "备份失败: 连接超时。请检查网络是否通畅，相机是否可达。\n原始错误: {}",
            err
        );
    }
    if lower.contains("unknown host") || lower.contains("unreachable") {
        return format!("备份失败: 无法连接到相机主机。请检查 IP 地址是否正确。\n原始错误: {}", err);
    }
    if lower.contains("ssl") || lower.contains("tls") || lower.contains("handshake") {
        return format!(
            "备份失败: TLS/SSL 握手失败。请检查 FTPS 配置是否正确，或尝试勾选\"信任所有 TLS 证书\"。\n原始错误: {}",
            err
        );
    }
    if lower.contains("login") || lower.contains("authentication") {
        return format!("备份失败: 认证失败。请检查用户名和密码是否正确。\n原始错误: {}", err);
    }
    format!("备份失败: {}", err)
}

fn build_tls_connector(trust_all: bool) -> Result<NativeTlsConnector, String> {
    let connector = TlsConnector::builder()
        .danger_accept_invalid_certs(trust_all)
        .danger_accept_invalid_hostnames(trust_all)
        .build()
        .map_err(|e| format!("创建 TLS 连接器失败: {}", e))?;
    Ok(NativeTlsConnector::from(connector))
}

enum FtpConnection {
    Plain(FtpStream),
    Secure(NativeTlsFtpStream),
}

impl FtpConnection {
    fn list(&mut self, path: Option<&str>) -> Result<Vec<String>, String> {
        match self {
            FtpConnection::Plain(s) => s.list(path).map_err(|e| e.to_string()),
            FtpConnection::Secure(s) => s.list(path).map_err(|e| e.to_string()),
        }
    }

    fn retr_as_buffer(&mut self, path: &str) -> Result<Cursor<Vec<u8>>, String> {
        match self {
            FtpConnection::Plain(s) => s.retr_as_buffer(path).map_err(|e| e.to_string()),
            FtpConnection::Secure(s) => s.retr_as_buffer(path).map_err(|e| e.to_string()),
        }
    }

    fn quit(self) {
        match self {
            FtpConnection::Plain(mut s) => {
                let _ = s.quit();
            }
            FtpConnection::Secure(mut s) => {
                let _ = s.quit();
            }
        }
    }
}

fn connect_ftp(camera: &JobxCameraConfig) -> Result<FtpConnection, String> {
    let addr = format!("{}:{}", camera.ip, camera.ftp_port);

    if camera.ftps_enabled {
        let mut stream =
            NativeTlsFtpStream::connect(&addr).map_err(|e| translate_ftp_error(&e.to_string()))?;
        {
            let tcp = stream.get_ref();
            let _ = tcp.set_read_timeout(Some(Duration::from_secs(30)));
            let _ = tcp.set_write_timeout(Some(Duration::from_secs(30)));
        }
        let connector = build_tls_connector(camera.trust_all_certs)?;
        stream = stream
            .into_secure(connector, &camera.ip)
            .map_err(|e| translate_ftp_error(&e.to_string()))?;
        stream
            .login(&camera.ftp_username, &camera.ftp_password)
            .map_err(|e| translate_ftp_error(&e.to_string()))?;
        stream
            .transfer_type(FileType::Binary)
            .map_err(|e| translate_ftp_error(&e.to_string()))?;
        Ok(FtpConnection::Secure(stream))
    } else {
        let mut stream = FtpStream::connect(&addr).map_err(|e| translate_ftp_error(&e.to_string()))?;
        {
            let tcp = stream.get_ref();
            let _ = tcp.set_read_timeout(Some(Duration::from_secs(30)));
            let _ = tcp.set_write_timeout(Some(Duration::from_secs(30)));
        }
        stream
            .login(&camera.ftp_username, &camera.ftp_password)
            .map_err(|e| translate_ftp_error(&e.to_string()))?;
        stream
            .transfer_type(FileType::Binary)
            .map_err(|e| translate_ftp_error(&e.to_string()))?;
        Ok(FtpConnection::Plain(stream))
    }
}

// ==================== 目录下载 ====================

fn parse_list_line(line: &str) -> Option<(String, bool)> {
    if line.starts_with("total") {
        return None;
    }
    let trimmed = line.trim();
    if trimmed.is_empty() {
        return None;
    }
    let parts: Vec<&str> = trimmed.split_whitespace().collect();
    if parts.len() < 9 {
        return None;
    }
    let name = parts[8..].join(" ");
    let is_dir = line.starts_with('d');
    if name == "." || name == ".." {
        return None;
    }
    Some((name, is_dir))
}

fn download_directory(
    stream: &mut FtpConnection,
    remote_path: &str,
    local_dir: &Path,
    state: &AppState,
    camera_name: &str,
) -> Result<usize, String> {
    fs::create_dir_all(local_dir).map_err(|e| format!("创建本地目录失败: {}", e))?;

    let list = stream
        .list(Some(remote_path))
        .map_err(|e| translate_ftp_error(&e.to_string()))?;

    let mut count = 0usize;
    let base_remote = remote_path.trim_end_matches('/');

    for line in list {
        let (name, is_dir) = match parse_list_line(&line) {
            Some(v) => v,
            None => continue,
        };

        let child_remote = if base_remote.is_empty() || base_remote == "/" {
            format!("/{}", name)
        } else {
            format!("{}/{}", base_remote, name)
        };

        if is_dir {
            let child_local = local_dir.join(&name);
            count += download_directory(stream, &child_remote, &child_local, state, camera_name)?;
        } else {
            let lower = name.to_lowercase();
            if lower.ends_with(".jobx") || lower.ends_with(".jobx.sig") {
                let local_file = local_dir.join(&name);
                match stream.retr_as_buffer(&child_remote) {
                    Ok(buffer) => {
                        match fs::File::create(&local_file) {
                            Ok(mut file) => {
                                if let Err(e) = file.write_all(&buffer.into_inner()) {
                                    state.add_jobx_log(
                                        "ERROR",
                                        &format!("[{}] 写入文件失败 {}: {}", camera_name, name, e),
                                    );
                                } else {
                                    count += 1;
                                }
                            }
                            Err(e) => {
                                state.add_jobx_log(
                                    "ERROR",
                                    &format!("[{}] 创建文件失败 {}: {}", camera_name, name, e),
                                );
                            }
                        }
                    }
                    Err(e) => {
                        state.add_jobx_log(
                            "ERROR",
                            &format!("[{}] 下载失败 {}: {}", camera_name, name, e),
                        );
                    }
                }
            }
        }
    }

    Ok(count)
}

fn determine_backup_dir(camera: &JobxCameraConfig, config_dir: &Path) -> PathBuf {
    if !camera.backup_directory.trim().is_empty() {
        PathBuf::from(&camera.backup_directory)
    } else {
        config_dir.to_path_buf()
    }
}

// ==================== 公开备份函数 ====================

pub fn backup_jobx_camera_internal(
    camera: &JobxCameraConfig,
    config_dir: &Path,
    state: &AppState,
) -> JobxBackupResult {
    state.add_jobx_log(
        "INFO",
        &format!(
            "开始备份: {} ({}) {}",
            camera.name,
            camera.ip,
            if camera.ftps_enabled { "[FTPS]" } else { "[FTP]" }
        ),
    );

    let backup_dir = determine_backup_dir(camera, config_dir);
    let timestamp = Local::now().format("%Y%m%d%H%M%S").to_string();
    let target_dir = backup_dir.join(&camera.name).join(&timestamp);

    let mut stream = match connect_ftp(camera) {
        Ok(s) => s,
        Err(e) => {
            state.add_jobx_log("ERROR", &format!("✗ {}: {}", camera.name, e));
            return JobxBackupResult {
                success: false,
                message: e,
                backup_path: None,
            };
        }
    };

    match download_directory(&mut stream, "/", &target_dir, state, &camera.name) {
        Ok(downloaded) => {
            stream.quit();
            if downloaded == 0 {
                let msg = "未找到任何 jobx 文件".to_string();
                state.add_jobx_log("WARN", &format!("✗ {}: {}", camera.name, msg));
                JobxBackupResult {
                    success: false,
                    message: msg,
                    backup_path: None,
                }
            } else {
                let target_str = target_dir.to_string_lossy().to_string();
                let msg = format!("备份成功，共下载 {} 个文件", downloaded);
                state.add_jobx_log("INFO", &format!("✓ {}: {} -> {}", camera.name, msg, target_str));
                JobxBackupResult {
                    success: true,
                    message: msg,
                    backup_path: Some(target_str),
                }
            }
        }
        Err(e) => {
            stream.quit();
            state.add_jobx_log("ERROR", &format!("✗ {}: {}", camera.name, e));
            JobxBackupResult {
                success: false,
                message: e,
                backup_path: None,
            }
        }
    }
}

// ==================== Tauri Commands ====================

#[tauri::command]
pub fn get_jobx_backup_config(state: State<Arc<AppState>>) -> JobxBackupConfig {
    state.jobx_backup_config.lock().unwrap().clone()
}

#[tauri::command]
pub fn set_jobx_backup_config(config: JobxBackupConfig, state: State<Arc<AppState>>) {
    *state.jobx_backup_config.lock().unwrap() = config;
    state.save_jobx_backup_config();
}

#[tauri::command]
pub fn add_jobx_camera(camera: JobxCameraConfig, state: State<Arc<AppState>>) {
    state.jobx_backup_config.lock().unwrap().cameras.push(camera);
    state.save_jobx_backup_config();
}

#[tauri::command]
pub fn update_jobx_camera(index: usize, camera: JobxCameraConfig, state: State<Arc<AppState>>) {
    let mut config = state.jobx_backup_config.lock().unwrap();
    if index < config.cameras.len() {
        config.cameras[index] = camera;
    }
    drop(config);
    state.save_jobx_backup_config();
}

#[tauri::command]
pub fn delete_jobx_camera(index: usize, state: State<Arc<AppState>>) {
    let mut config = state.jobx_backup_config.lock().unwrap();
    if index < config.cameras.len() {
        config.cameras.remove(index);
    }
    drop(config);
    state.save_jobx_backup_config();
}

#[tauri::command]
pub fn backup_jobx_camera(index: usize, state: State<Arc<AppState>>) -> JobxBackupResult {
    let camera = {
        let config = state.jobx_backup_config.lock().unwrap();
        config.cameras.get(index).cloned()
    };
    match camera {
        Some(camera) => backup_jobx_camera_internal(&camera, &state.config_dir, &state),
        None => JobxBackupResult {
            success: false,
            message: "未找到指定相机".to_string(),
            backup_path: None,
        },
    }
}

#[tauri::command]
pub fn backup_all_jobx_cameras(state: State<Arc<AppState>>) {
    let cameras = {
        let config = state.jobx_backup_config.lock().unwrap();
        config.cameras.clone()
    };
    let config_dir = state.config_dir.clone();
    let state_arc = Arc::clone(&state);

    std::thread::spawn(move || {
        for camera in cameras {
            backup_jobx_camera_internal(&camera, &config_dir, &state_arc);
            std::thread::sleep(Duration::from_millis(500));
        }
    });
}

#[tauri::command]
pub fn get_jobx_backup_logs(state: State<Arc<AppState>>) -> Vec<JobxBackupLogEntry> {
    state.jobx_log_buffer.lock().unwrap().clone()
}

#[tauri::command]
pub fn open_jobx_backup_dir(path: Option<String>, state: State<Arc<AppState>>) -> Result<(), String> {
    let dir = match path {
        Some(p) if !p.trim().is_empty() => PathBuf::from(p),
        _ => state.config_dir.clone(),
    };

    fs::create_dir_all(&dir).map_err(|e| format!("创建目录失败: {}", e))?;

    let dir_str = dir.to_string_lossy().to_string();
    std::process::Command::new("explorer")
        .arg(&dir_str)
        .spawn()
        .map_err(|e| format!("打开目录失败: {}", e))?;
    Ok(())
}
