use axum::{extract::State as AxumState, Json};
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::net::SocketAddr;
use std::process::Stdio;
use std::sync::{Arc, Mutex};
use std::time::Duration;
use tauri::{command, State};

#[cfg(windows)]
use std::os::windows::process::CommandExt;

// ==================== 远程指令 HTTP 服务 ====================

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ExecRequest {
    pub command: String,
    #[serde(default = "default_timeout")]
    pub timeout: u64,
    #[serde(default)]
    pub work_dir: String,
}

fn default_timeout() -> u64 {
    30
}

#[derive(Debug, Clone, Serialize)]
pub struct ExecResult {
    pub status: String,
    pub stdout: String,
    pub stderr: String,
    pub exit_code: Option<i32>,
    pub timeout: bool,
    pub command: String,
}

#[derive(Debug, Clone, Serialize)]
pub struct RemoteCommandLogEntry {
    pub timestamp: String,
    pub level: String,
    pub message: String,
}

#[derive(Debug, Clone, Serialize)]
pub struct RemoteCommandStatus {
    pub is_running: bool,
    pub port: u16,
}

pub struct RemoteCommandState {
    running: Mutex<bool>,
    port: Mutex<u16>,
    log_buffer: Arc<Mutex<Vec<RemoteCommandLogEntry>>>,
    shutdown_tx: Mutex<Option<tokio::sync::oneshot::Sender<()>>>,
}

impl RemoteCommandState {
    pub fn new() -> Self {
        Self {
            running: Mutex::new(false),
            port: Mutex::new(10022),
            log_buffer: Arc::new(Mutex::new(Vec::new())),
            shutdown_tx: Mutex::new(None),
        }
    }

    fn add_log(&self, level: &str, message: &str) {
        let entry = RemoteCommandLogEntry {
            timestamp: chrono::Local::now().format("%H:%M:%S").to_string(),
            level: level.to_string(),
            message: message.to_string(),
        };
        let mut buffer = self.log_buffer.lock().unwrap();
        buffer.push(entry);
        if buffer.len() > 1000 {
            buffer.remove(0);
        }
    }
}

fn detect_shell() -> (String, Vec<String>, bool) {
    // 优先使用 PowerShell，其次 CMD
    let candidates = [
        ("powershell.exe", vec!["-NoProfile".to_string(), "-Command".to_string()], true),
        ("pwsh.exe", vec!["-NoProfile".to_string(), "-Command".to_string()], true),
        ("cmd.exe", vec!["/C".to_string()], false),
    ];

    for (shell, args, is_ps) in candidates {
        if std::process::Command::new(shell).arg("/?").output().is_ok() {
            return (shell.to_string(), args, is_ps);
        }
    }

    ("cmd.exe".to_string(), vec!["/C".to_string()], false)
}

fn decode_output(bytes: &[u8], is_cmd: bool) -> String {
    if is_cmd {
        // CMD 输出常用 GBK
        encoding_rs::GBK.decode(bytes).0.to_string()
    } else {
        String::from_utf8_lossy(bytes).to_string()
    }
}

async fn execute_shell(command: &str, timeout_sec: u64, work_dir: &str) -> ExecResult {
    let (shell, args, is_cmd) = detect_shell();

    let mut cmd = tokio::process::Command::new(&shell);
    for arg in &args {
        cmd.arg(arg);
    }
    cmd.arg(command);

    if !work_dir.is_empty() && std::path::Path::new(work_dir).exists() {
        cmd.current_dir(work_dir);
    }

    #[cfg(windows)]
    {
        cmd.creation_flags(0x08000000); // CREATE_NO_WINDOW
    }

    cmd.stdout(Stdio::piped());
    cmd.stderr(Stdio::piped());

    let start = std::time::Instant::now();
    let result = match tokio::time::timeout(Duration::from_secs(timeout_sec), cmd.output()).await {
        Ok(Ok(output)) => {
            let stdout = decode_output(&output.stdout, is_cmd);
            let stderr = decode_output(&output.stderr, is_cmd);
            ExecResult {
                status: if output.status.success() { "success".to_string() } else { "error".to_string() },
                stdout,
                stderr,
                exit_code: output.status.code(),
                timeout: false,
                command: command.to_string(),
            }
        }
        Ok(Err(e)) => ExecResult {
            status: "error".to_string(),
            stdout: String::new(),
            stderr: format!("启动进程失败: {}", e),
            exit_code: None,
            timeout: false,
            command: command.to_string(),
        },
        Err(_) => ExecResult {
            status: "error".to_string(),
            stdout: String::new(),
            stderr: format!("命令执行超时 ({}s)", timeout_sec),
            exit_code: None,
            timeout: true,
            command: command.to_string(),
        },
    };

    let elapsed = start.elapsed().as_millis();
    log::debug!("exec [{}] took {} ms", command, elapsed);
    result
}

async fn exec_handler(
    AxumState(state): AxumState<Arc<RemoteCommandState>>,
    Json(req): Json<ExecRequest>,
) -> Json<ExecResult> {
    let command = req.command.trim().to_string();
    let timeout = if req.timeout > 0 { req.timeout } else { 30 };
    state.add_log("INFO", &format!("收到远程命令: {}", command));
    let result = execute_shell(&command, timeout, &req.work_dir).await;
    if result.status == "success" {
        state.add_log("INFO", &format!("命令执行成功: {}", command));
    } else {
        state.add_log("WARN", &format!("命令执行失败: {} - {}", command, result.stderr));
    }
    axum::Json(result)
}

async fn health_handler() -> Json<serde_json::Value> {
    Json(serde_json::json!({
        "status": "ok",
        "shell": detect_shell().0,
        "time": chrono::Local::now().to_rfc3339(),
    }))
}

fn build_app(state: Arc<RemoteCommandState>) -> axum::Router {
    use axum::routing::{get, post};
    use tower_http::cors::{Any, CorsLayer};

    axum::Router::new()
        .route("/health", get(health_handler))
        .route("/exec", post(exec_handler))
        .layer(CorsLayer::new().allow_origin(Any).allow_methods(Any).allow_headers(Any))
        .with_state(state)
}

#[command]
pub fn start_remote_command_server(port: u16, state: State<Arc<RemoteCommandState>>) -> Result<String, String> {
    {
        let running = state.running.lock().unwrap();
        if *running {
            return Ok(format!("远程指令服务已在端口 {} 运行", state.port.lock().unwrap()));
        }
    }

    *state.port.lock().unwrap() = port;
    *state.running.lock().unwrap() = true;
    state.add_log("INFO", &format!("远程指令服务即将启动在端口 {}", port));

    let state_clone = Arc::clone(&state);
    std::thread::spawn(move || {
        let rt = match tokio::runtime::Runtime::new() {
            Ok(rt) => rt,
            Err(e) => {
                state_clone.add_log("ERROR", &format!("创建 tokio runtime 失败: {}", e));
                *state_clone.running.lock().unwrap() = false;
                return;
            }
        };

        rt.block_on(async {
            let addr: SocketAddr = format!("0.0.0.0:{}", port).parse().unwrap();
            let app = build_app(state_clone.clone());
            let listener = match tokio::net::TcpListener::bind(&addr).await {
                Ok(l) => l,
                Err(e) => {
                    state_clone.add_log("ERROR", &format!("绑定端口 {} 失败: {}", port, e));
                    *state_clone.running.lock().unwrap() = false;
                    return;
                }
            };

            state_clone.add_log("INFO", &format!("远程指令服务已启动: http://{}/", addr));

            let (tx, rx) = tokio::sync::oneshot::channel::<()>();
            *state_clone.shutdown_tx.lock().unwrap() = Some(tx);

            let server = axum::serve(listener, app).with_graceful_shutdown(async {
                let _ = rx.await;
            });

            if let Err(e) = server.await {
                state_clone.add_log("ERROR", &format!("远程指令服务错误: {}", e));
            }

            *state_clone.running.lock().unwrap() = false;
            state_clone.add_log("INFO", "远程指令服务已停止");
        });
    });

    Ok(format!("远程指令服务正在启动，端口 {}", port))
}

#[command]
pub fn stop_remote_command_server(state: State<Arc<RemoteCommandState>>) -> Result<String, String> {
    {
        let running = state.running.lock().unwrap();
        if !*running {
            return Ok("远程指令服务未运行".to_string());
        }
    }

    if let Some(tx) = state.shutdown_tx.lock().unwrap().take() {
        let _ = tx.send(());
    }
    *state.running.lock().unwrap() = false;
    state.add_log("INFO", "远程指令服务停止请求已发送");
    Ok("远程指令服务已停止".to_string())
}

#[command]
pub fn get_remote_command_status(state: State<Arc<RemoteCommandState>>) -> RemoteCommandStatus {
    RemoteCommandStatus {
        is_running: *state.running.lock().unwrap(),
        port: *state.port.lock().unwrap(),
    }
}

#[command]
pub fn get_remote_command_logs(state: State<Arc<RemoteCommandState>>) -> Vec<RemoteCommandLogEntry> {
    state.log_buffer.lock().unwrap().clone()
}

// ==================== 局域网设备扫描 ====================

#[derive(Debug, Clone, Serialize)]
pub struct LanDevice {
    pub ip: String,
    pub mac: Option<String>,
    pub hostname: Option<String>,
    pub status: String, // online / offline
    pub vendor: Option<String>,
    pub ports: Vec<String>,
}

#[derive(Debug, Clone, Serialize)]
pub struct LanScanResult {
    pub subnet: String,
    pub devices: Vec<LanDevice>,
    pub elapsed_ms: u128,
    pub message: String,
}

/// 去除 ipconfig 输出中 IP/掩码后面的本地化后缀，例如
/// 中文系统的 "(首选)" 或英文系统的 "(Preferred)"。
fn strip_ip_suffix(raw: &str) -> String {
    let v = raw.trim();
    v.split(['(', '（'])
        .next()
        .map(|s| s.trim().to_string())
        .unwrap_or_else(|| v.to_string())
}

fn get_local_subnet() -> Result<String, String> {
    // 使用 ipconfig 获取本机 IP 和子网掩码
    let output = std::process::Command::new("ipconfig")
        .output()
        .map_err(|e| format!("执行 ipconfig 失败: {}", e))?;
    let text = String::from_utf8_lossy(&output.stdout);

    let mut ip: Option<String> = None;
    let mut mask: Option<String> = None;

    for line in text.lines() {
        let lower = line.to_lowercase();
        if lower.contains("ipv4") {
            if let Some(v) = line.split(':').nth(1) {
                ip = Some(strip_ip_suffix(v));
            }
        } else if lower.contains("子网掩码") || lower.contains("subnet mask") {
            if let Some(v) = line.split(':').nth(1) {
                mask = Some(strip_ip_suffix(v));
            }
        }

        if ip.is_some() && mask.is_some() {
            break;
        }
    }

    let ip = ip.ok_or("无法获取本机 IPv4 地址")?;
    let mask = mask.ok_or("无法获取子网掩码")?;

    let ip_parts: Vec<u8> = ip
        .split('.')
        .map(|s| s.parse().unwrap_or(0))
        .collect();
    let mask_parts: Vec<u8> = mask
        .split('.')
        .map(|s| s.parse().unwrap_or(0))
        .collect();

    if ip_parts.len() != 4 || mask_parts.len() != 4 {
        return Err("解析 IP/掩码失败".to_string());
    }

    let network: Vec<u8> = ip_parts
        .iter()
        .zip(mask_parts.iter())
        .map(|(a, b)| a & b)
        .collect();

    let cidr = mask_parts.iter().map(|b| b.count_ones() as u8).sum::<u8>();
    Ok(format!("{}.{}.{}.{}/{}", network[0], network[1], network[2], network[3], cidr))
}

fn parse_arp_table() -> HashMap<String, String> {
    let mut map = HashMap::new();
    if let Ok(output) = std::process::Command::new("arp").arg("-a").output() {
        let text = String::from_utf8_lossy(&output.stdout);
        for line in text.lines() {
            let parts: Vec<&str> = line.split_whitespace().collect();
            if parts.len() >= 2 {
                let ip = parts[0]
                    .trim_start_matches('(')
                    .trim_end_matches(')')
                    .to_string();
                let mac = parts[1].to_string();
                if ip.parse::<std::net::Ipv4Addr>().is_ok() && mac.contains(':') {
                    map.insert(ip, mac);
                }
            }
        }
    }
    map
}

fn ping_host(ip: &str, timeout_ms: u32) -> bool {
    #[cfg(windows)]
    {
        std::process::Command::new("ping")
            .args(&["-n", "1", "-w", &timeout_ms.to_string(), ip])
            .creation_flags(0x08000000)
            .output()
            .map(|o| {
                let text = String::from_utf8_lossy(&o.stdout).to_lowercase();
                text.contains("ttl=") || text.contains("回复") || text.contains("reply")
            })
            .unwrap_or(false)
    }
    #[cfg(not(windows))]
    {
        std::process::Command::new("ping")
            .args(&["-c", "1", "-W", &(timeout_ms / 1000).max(1).to_string(), ip])
            .output()
            .map(|o| o.status.success())
            .unwrap_or(false)
    }
}

fn resolve_hostname(ip: &str) -> Option<String> {
    let output = std::process::Command::new("nslookup")
        .arg(ip)
        .output()
        .ok()?;
    let text = String::from_utf8_lossy(&output.stdout);
    for line in text.lines() {
        let lower = line.to_lowercase();
        if lower.contains("name:") || lower.contains("名称") {
            if let Some(name) = line.split(':').nth(1) {
                let name = name.trim().trim_end_matches('.').to_string();
                if !name.is_empty() {
                    return Some(name);
                }
            }
        }
    }
    None
}

fn find_nmap_binary() -> Option<std::path::PathBuf> {
    // 1. 优先查找与可执行文件同目录的 nmap.exe（sidecar 方式）
    if let Ok(exe) = std::env::current_exe() {
        if let Some(dir) = exe.parent() {
            let sidecar = dir.join("nmap.exe");
            if sidecar.exists() {
                return Some(sidecar);
            }
        }
    }

    // 2. 查找 PATH 环境变量
    if let Ok(path) = std::env::var("PATH") {
        for dir in path.split(';') {
            let p = std::path::PathBuf::from(dir).join("nmap.exe");
            if p.exists() {
                return Some(p);
            }
        }
    }

    // 3. 常见安装目录
    for dir in [
        r"C:\Program Files (x86)\Nmap",
        r"C:\Program Files\Nmap",
    ] {
        let p = std::path::PathBuf::from(dir).join("nmap.exe");
        if p.exists() {
            return Some(p);
        }
    }

    None
}

fn run_nmap_scan(subnet: &str, timeout_ms: u32) -> Option<Vec<LanDevice>> {
    let nmap = find_nmap_binary()?;
    let mut cmd = std::process::Command::new(&nmap);
    cmd.args(&["-sn", "-T4", "--max-rtt-timeout", &format!("{}ms", timeout_ms), subnet]);
    #[cfg(windows)]
    cmd.creation_flags(0x08000000);

    let output = cmd.output().ok()?;
    let text = String::from_utf8_lossy(&output.stdout);
    let mut devices = Vec::new();
    let mut current_ip: Option<String> = None;
    let mut current_hostname: Option<String> = None;

    for line in text.lines() {
        let line = line.trim();
        if line.starts_with("Nmap scan report for ") {
            let rest = &line["Nmap scan report for ".len()..];
            // 格式: hostname (ip) 或 ip
            if let Some(start) = rest.find('(') {
                let host = rest[..start].trim().to_string();
                let ip = rest[start + 1..rest.len() - 1].to_string();
                current_hostname = Some(host);
                current_ip = Some(ip);
            } else {
                current_hostname = None;
                current_ip = Some(rest.to_string());
            }
        } else if line.starts_with("MAC Address: ") {
            let rest = &line["MAC Address: ".len()..];
            let parts: Vec<&str> = rest.splitn(2, ' ').collect();
            let mac = parts.first().map(|s| s.to_string());
            let vendor = parts.get(1).map(|s| s.trim_start_matches('(').trim_end_matches(')').to_string());
            if let Some(ip) = current_ip.take() {
                devices.push(LanDevice {
                    ip,
                    mac,
                    hostname: current_hostname.take(),
                    status: "online".to_string(),
                    vendor,
                    ports: vec![],
                });
            }
        }
    }

    Some(devices)
}

fn extract_base_ip(subnet: &str) -> Option<(String, u8)> {
    let parts: Vec<&str> = subnet.split('/').collect();
    if parts.len() != 2 {
        return None;
    }
    let cidr: u8 = parts[1].parse().ok()?;
    let ip_parts: Vec<&str> = parts[0].split('.').collect();
    if ip_parts.len() != 4 {
        return None;
    }
    Some((format!("{}.{}.{}.", ip_parts[0], ip_parts[1], ip_parts[2]), cidr))
}

#[command]
pub fn get_local_subnet_info() -> Result<String, String> {
    get_local_subnet()
}

#[command]
pub fn scan_lan_devices(subnet: Option<String>, timeout_ms: Option<u32>) -> Result<LanScanResult, String> {
    let start = std::time::Instant::now();
    let subnet = subnet.unwrap_or_else(|| get_local_subnet().unwrap_or_else(|_| "192.168.1.0/24".to_string()));
    let timeout_ms = timeout_ms.unwrap_or(800);

    // 优先使用 nmap（如果存在），否则回退到并发 ping + ARP
    if let Some(nmap_devices) = run_nmap_scan(&subnet, timeout_ms) {
        let count = nmap_devices.len();
        return Ok(LanScanResult {
            subnet,
            devices: nmap_devices,
            elapsed_ms: start.elapsed().as_millis(),
            message: format!("nmap 扫描完成，发现 {} 台设备", count),
        });
    }

    let (base, cidr) = extract_base_ip(&subnet).ok_or("无法解析子网")?;

    // 仅扫描 /24 子网，避免过大范围
    if cidr != 24 {
        return Ok(LanScanResult {
            subnet,
            devices: vec![],
            elapsed_ms: start.elapsed().as_millis(),
            message: "仅支持 /24 子网扫描".to_string(),
        });
    }

    let arp_table = parse_arp_table();
    let mut devices = Vec::new();

    // 并发 ping 扫描
    let rt = tokio::runtime::Runtime::new().map_err(|e| e.to_string())?;
    let results = rt.block_on(async {
        let mut tasks = Vec::new();
        for i in 1..255 {
            let ip = format!("{}{}", base, i);
            let arp = arp_table.get(&ip).cloned();
            let t = timeout_ms;
            tasks.push(tokio::spawn(async move {
                let online = ping_host(&ip, t);
                (ip, online, arp)
            }));
        }
        let mut results = Vec::with_capacity(tasks.len());
        for task in tasks {
            if let Ok(r) = task.await {
                results.push(r);
            }
        }
        results
    });

    for (ip, online, mac) in results {
        // 只保留在线设备或在 ARP 表中的设备
        if !online && mac.is_none() {
            continue;
        }
        let hostname = if online { resolve_hostname(&ip) } else { None };
        devices.push(LanDevice {
            ip,
            mac,
            hostname,
            status: if online { "online".to_string() } else { "offline".to_string() },
            vendor: None,
            ports: vec![],
        });
    }

    devices.sort_by(|a, b| {
        let a_parts: Vec<u8> = a.ip.split('.').map(|s| s.parse().unwrap_or(0)).collect();
        let b_parts: Vec<u8> = b.ip.split('.').map(|s| s.parse().unwrap_or(0)).collect();
        a_parts.cmp(&b_parts)
    });

    let count = devices.len();
    Ok(LanScanResult {
        subnet,
        devices,
        elapsed_ms: start.elapsed().as_millis(),
        message: format!("扫描完成，发现 {} 台设备", count),
    })
}

// ==================== nmap 扩展入口（预留） ====================
// 如需启用 nmap，请将 nmap 可执行文件放入 src-tauri/binaries/ 目录，
// 并在此补充 include_bytes! 与对应调用逻辑。
