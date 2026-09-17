# CameraViewerTauri

工业相机 Web 界面显示工具，基于 Tauri 2.x + Vanilla HTML/JS/CSS 构建。

当前版本：**26.9.20**

开源仓库：<https://github.com/JustSmartAuto/CameraViewerTauri>

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
- **轻度视觉检测**（软件设置中开关启用）：三栏检测工作台——Monaco 编写 ECMAScript `inspect(context)` 检测脚本（boa_engine 0.22 执行）、声明式数据驱动 ROI 编辑器（EditRegion/EditCircle/EditPoint/EditLine/EditPolygon，画布拖拽与属性表双向联动，各区域灰度 mean/stdDev/min/max 自动注入脚本）、原图/效果图对比 + OK/NG 判定与测试控制台；脚本与控件持久化，5 秒超时保护
- **CogSocket 单元格设置**（相机设置中将连接模式切为 CogSocket 后，每台相机格出现"单元格"按钮）：纯前端 WebSocket 直连相机（hello/openSession/login 全自动，兼容 v3 `cam0/hmi` 与旧版 `system` 根路径，每帧自动回 ready、15s 保活）；表格列出所有可编辑单元格（整数/浮点带 min/max 校验、字符串带 maxLength、复选框、按钮），支持单行写入、批量写入、手动 JSON 写入（可写 EditRegion 等复杂对象）、写后自动回读 ✓/✗、离线编辑（关窗自动恢复在线）、手动触发与分级日志；每相机用户名/密码可保存（**明文存于 CameraConfig.json，仅限工控可信内网使用**，默认 admin/空密码）
- **主题切换**：明亮/暗黑主题
- **多语言**：中文/英文界面
- **配置位置可选 / 多实例**：默认配置保存在系统 AppData；可在"软件设置 → 配置文件位置"一键切换为便携模式（配置保存在程序同目录 `Configs/`，由 `portable.txt` 标记），拷贝程序文件夹即可运行独立实例；也支持命令行 `--config-dir <路径>` 指定配置目录启动
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
| `CameraConfig.json` | 相机数量、延时、各相机 URL、备注及锁定状态（26.9.20 起含 CogSocket 登录凭据，明文存储，仅限可信内网） |
| `TransformConfig.json` | 文件转存规则配置 |
| `CleanConfig.json` | 图片清理策略配置 |
| `CompressConfig.json` | 图像压缩参数配置 |
| `CompressCheckpoint.json` | 压缩进度检查点（断电续传） |
| `FtpConfig.json` | FTP 服务器配置（端口、TLS、用户等） |
| `NtpConfig.json` | NTP 时间服务器配置（端口等） |
| `JobxBackupConfig.json` | JOBX 相机备份配置 |
| `AppConfig.json` | 主题、语言等应用设置（含视觉检测功能开关） |
| `VisionConfig.json` | 视觉检测脚本、图片路径与 ROI 控件 |

## 版本记录

- **26.9.20**
  - 新增 CogSocket 模式单元格值设置（TODO #7）：相机设置连接模式切为 CogSocket 后，每台相机格出现"单元格"按钮，弹窗可连接相机（WebSocket 握手/openSession/login 全自动，兼容 v3 与旧版固件根路径，结果帧自动回 ready、15s 保活）并设置电子表格可编辑单元格
  - 支持整数/浮点（min/max 约束）、字符串（maxLength）、复选框、按钮五类可编辑单元格；单行写入、批量写入（setCellValues）、手动 JSON 写入（支持 EditRegion 等复杂对象）、写后约 600ms 自动回读校验、离线编辑（关窗自动恢复在线）、手动触发、时间戳分级日志
  - 每相机 CogSocket 用户名/密码可保存（Rust 新命令 `update_camera_cogsocket_auth`，CamConfigItem 新增两字段，旧配置 serde default 兼容）；**明文存于 CameraConfig.json，仅限工控可信内网**
  - SDK 用 Function 沙箱加载，规避与 Monaco AMD loader 的全局 define 冲突；协议时序经 Node mock 相机 harness（4 组用例）验证，无真机未做硬件联调
- **26.9.19**
  - 新增轻度视觉检测功能（TODO #13）：在"软件设置 → 功能开关"启用后，主界面出现"视觉检测"按钮，进入三栏工作台（左：Monaco ECMAScript 编辑器；中：电子表格式声明式 ROI 控件区；右：原图/效果图 + 脚本测试控制台），开关持久化到 AppConfig.json
  - 新增后端 `vision.rs`：5 个 Tauri 命令，boa_engine 0.22 执行用户 `inspect(context)` 脚本并以 `JSON.stringify` 取回 `{pass,message,overlays,metrics}`；image crate 按区域/圆/点/直线/多边形几何计算灰度统计（越界裁剪、像素去重、射线法多边形、线段采样）；图片校验解码后转 base64 data URL 供前端 canvas 使用；独立工作线程 + 5 秒 recv_timeout 防止死循环脚本卡死
  - ROI 编辑器支持五种控件的画布拖拽（矩形四角、圆半径、线端点、多边形顶点增删）与属性表数值双向同步；脚本返回的 overlays 声明式叠加渲染到效果图；控制台支持时间戳与 log/info/warn/error/metric 分级着色
  - VisionConfig.json 持久化脚本、图片路径与控件数据；前端新增 vision.js/vision.svg，i18n 中英双语 38 个键，样式跟随深/浅主题
  - 脚本编写说明见 [PROGRAMING.md](PROGRAMING.md)（`inspect(context)` 视觉检测契约与图片清理 `evaluate(context)` 契约、示例与排错）
- **26.9.18**
  - 图片清理标签页新增"高级版（脚本判定）"开关：复刻 ImageCleanerAutoWeld 的 `delete_conditions.js` 功能，用户可在 Monaco 编辑器中编写 `evaluate(context)` 脚本自定义删除判定逻辑，context 包含 `nowMs/path/cleanupMode/storageTimeSeconds/expiredCount/imageCount/imageCountEnabled/imageCountThreshold/freeSpaceGb/diskSpaceEnabled/diskSpaceThresholdGb` 共 11 个字段（驼峰命名，与 ImageCleanerAutoWeld 对齐）
  - 脚本引擎使用 `boa_engine` 0.22.0（纯 Rust ECMAScript 引擎，crates.io），原计划使用的 `qjs_runtime`（Lewin671/quickjs-rust）因 GitHub 网络不可达改用 boa_engine 作为 fallback
  - 高级版开启时每删一个最旧目标重新评估脚本判定，关闭时保留旧 OR 行为零回归；内置默认脚本与 ImageCleanerAutoWeld 的 AND 逻辑一致
  - 前端集成 Monaco Editor 0.56.0（AMD loader 按需加载），支持中/英 i18n、主题联动、载入默认脚本、测试脚本执行
- **26.9.17**
  - 新增"软件设置"标签页：集中提供界面语言（中文/English）、界面主题（浅色/深色）切换，配置文件位置选项从"安全设置"迁入；与主界面工具栏按钮双向联动、实时生效
  - 点击"设置"立即弹出独立加载窗口（splashscreen 技术：旋转齿轮 + 进度条 + 毫秒计时），各设置项状态并行加载，消除点击后长时间无响应
  - 启动时按 Tauri 官方 splashscreen 方式显示独立启动窗口（进度条 + 实时毫秒计时器），主窗口就绪后自动切换，全程无白屏
  - 相机 HMI 网页（/pages/hmi/）支持自动翻译：宿主层注入文本替换脚本，界面语言跟随软件（中/英）实时切换并可还原，不影响设备名、作业数据与检测结果
  - 新增配置文件位置可选（软件设置页）：默认保存在系统 AppData，可一键切换为便携模式（配置跟随程序目录 `Configs/`，由 `portable.txt` 标记），并支持命令行 `--config-dir <路径>`，方便启动多个互不干扰的实例；切换时可选择迁移现有配置
  - "关于"弹窗新增开源仓库链接，点击调用系统默认浏览器打开
- **26.9.16**
  - 修复 NTP 服务器测试报错（前端参数名 `host_port`/`timeout_ms` 不符合 Tauri camelCase 约定），并顺带修复局域网扫描自定义超时时间不生效的问题
  - 修复相机网格"锁定/解锁"按钮点击无反应的问题（前端调用签名错位），并将锁定状态持久化到 `CameraConfig.json`，重启后自动恢复
  - 修复打开设置页时弹出"扫描局域网设备失败"的问题：子网探测改为静默预填，失败不弹窗；同时修复中文 Windows 下 `ipconfig` 输出中 IPv4 带 `(首选)` 后缀导致子网解析失败的问题
- **26.7.31 / 26.7.25** 及更早版本：功能迭代（FTP 自动递归创建目录、显示设置、调试工具、Advance IP Scanner 复刻、安全设置、JOBX 备份、窗口镜像、虚拟显示器等）

## 许可证

MIT License
