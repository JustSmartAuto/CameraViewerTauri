# CameraViewerTauri

工业相机 Web 界面显示工具，基于 Tauri 2.x + Vanilla HTML/JS/CSS 构建。

当前版本：**26.9.16**

## 功能特性

- **相机显示**：支持 1-16 画面网格布局，可配置延时刷新，支持锁定/解锁（锁定状态持久化到配置文件，重启后自动恢复）、最大化、备注等功能
- **文件转存**：监听文件夹，按自定义规则自动解析文件名并转存到目标目录
- **图片清理**：按保留天数或剩余磁盘空间策略自动清理过期图片
- **图像压缩**：监听文件夹自动将 BMP/PNG/JPEG 压缩为 AVIF 格式，支持断电续传、暂停/继续
- **FTP 服务器**：内置 FTP 服务，可配置端口、TLS、多用户及根目录；上传文件时自动递归创建目标目录（类似 FileZilla）
- **NTP 时间服务器**：内置 NTP 服务，可为局域网设备提供时间同步
- **JOBX 作业备份**：配置相机 FTP 信息，手动或批量备份 JOBX 相机文件到本地目录
- **安全设置**：一键开启/关闭 Windows 防火墙；一键开启/关闭 UAC 弹窗（修改注册表后需重启生效）
- **显示设置**：
  - **窗口镜像**：使用 Windows DWM 缩略图技术，将指定窗口实时镜像到独立的悬浮窗口，支持不透明度、点击穿透、1:1 / 1/2 / 1/4 / 拉伸缩放
  - **指定显示器**：获取系统显示器列表，将主窗口移动到指定显示器（适配工控机多显示器场景）
  - **虚拟显示器**：基于 Parsec VDD 添加/移除虚拟显示器，内置驱动安装包，可静默安装驱动，解决分屏器无法区分多显示器的问题
- **调试工具**：
  - **远程指令服务**：内嵌 HTTP 服务，提供 `/exec` 执行远程 Shell 命令、`/health` 健康检查，支持 PowerShell/CMD 自动探测、超时控制、日志记录
  - **扫描局域网设备**：打开设置页时静默自动检测本机子网（仅填入输入框，失败不弹窗），优先调用 `nmap.exe`（sidecar 方式，放置于可执行文件同目录或系统 PATH），回退为并发 Ping 扫描 `/24` 网段并解析 ARP 表，以表格形式展示在线/离线设备（类似 Advanced IP Scanner）
- **主题切换**：明亮/暗黑主题
- **多语言**：中文/英文界面
- **托盘驻留**：点击关闭按钮最小化到系统托盘，右键托盘图标可选择“显示界面 / 关于 / 退出”

## 技术栈

- **前端**：Vanilla HTML/JS/CSS（无框架依赖）
- **后端**：Rust + Tauri 2.x
- **图像压缩**：ravif (AVIF 编码) + image (BMP/PNG/JPEG 解码)
- **文件监控**：notify crate

## 开发环境

- [Rust](https://rustup.rs/) (建议最新稳定版)
- [Node.js](https://nodejs.org/) (用于 Tauri CLI)
- [VS Code](https://code.visualstudio.com/) + [Tauri 插件](https://marketplace.visualstudio.com/items?itemName=tauri-apps.tauri-vscode) + [rust-analyzer](https://marketplace.visualstudio.com/items?itemName=rust-lang.rust-analyzer)

## 构建

### 开发模式

```bash
cd CameraViewerTauri/src-tauri
cargo tauri dev
```

### 生产构建（带时间戳后缀）

```bash
cd CameraViewerTauri
bash build-with-timestamp.sh
```

或直接构建：

```bash
cd CameraViewerTauri/src-tauri
cargo build --release
```

构建产物位于 `src-tauri/target/release/camera-viewer-tauri.exe`

### 离线编译（使用 cargo vendor）

项目已配置 `cargo vendor`，在无网络环境下：

```bash
cd CameraViewerTauri/src-tauri
cargo build --release --offline
```

vendor 目录位于 `src-tauri/vendor/`，包含所有依赖 crate。

## 项目结构

```
CameraViewerTauri/
├── src/                          # 前端代码
│   ├── index.html               # 主页面（设置弹窗、关于页面等）
│   ├── main.js                  # 前端逻辑
│   ├── styles.css               # 样式（含明亮/暗黑主题）
│   ├── i18n.js                  # 国际化（zh/en）
│   └── assets/                  # 图标资源
├── src-tauri/                   # Rust 后端
│   ├── src/
│   │   ├── main.rs             # 入口
│   │   ├── lib.rs              # 核心逻辑（命令、文件监控、图像压缩等）
│   │   ├── display.rs          # 窗口镜像与显示器管理
│   │   ├── virtual_display.rs  # 基于 Parsec VDD 的虚拟显示器管理
│   │   └── debug_tools.rs      # 远程指令服务与局域网设备扫描
│   ├── binaries/               # 嵌入式第三方二进制（Parsec VDD 安装包）
│   ├── Cargo.toml              # Rust 依赖
│   ├── tauri.conf.json         # Tauri 配置
│   ├── capabilities/           # 权限配置
│   └── vendor/                 # cargo vendor 离线依赖
└── build-with-timestamp.sh     # 带时间戳构建脚本
```

## 配置说明

配置文件自动保存在系统应用数据目录（Windows: `%LOCALAPPDATA%/com.camera.helper/`）

| 配置文件 | 说明 |
|---------|------|
| `CameraConfig.json` | 相机数量、延时、各相机 URL、备注及锁定状态 |
| `TransformConfig.json` | 文件转存规则配置 |
| `CleanConfig.json` | 图片清理策略配置 |
| `CompressConfig.json` | 图像压缩参数配置 |
| `CompressCheckpoint.json` | 压缩进度检查点（断电续传） |
| `FtpConfig.json` | FTP 服务器配置（端口、TLS、用户等） |
| `NtpConfig.json` | NTP 时间服务器配置（端口等） |
| `JobxBackupConfig.json` | JOBX 相机备份配置 |
| `AppConfig.json` | 主题、语言等应用设置 |

## 版本记录

- **26.9.16**
  - 修复相机网格"锁定/解锁"按钮点击无反应的问题（前端调用签名错位），并将锁定状态持久化到 `CameraConfig.json`，重启后自动恢复
  - 修复打开设置页时弹出"扫描局域网设备失败"的问题：子网探测改为静默预填，失败不弹窗；同时修复中文 Windows 下 `ipconfig` 输出中 IPv4 带 `(首选)` 后缀导致子网解析失败的问题
- **26.7.31 / 26.7.25** 及更早版本：功能迭代（FTP 自动递归创建目录、显示设置、调试工具、Advance IP Scanner 复刻、安全设置、JOBX 备份、窗口镜像、虚拟显示器等）

## 许可证

MIT License
