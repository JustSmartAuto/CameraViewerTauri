# CameraViewerTauri 开发计划 (PLAN.md)

> 基于 [TODO.md](./TODO.md) 中未完成的待办事项整理的详细实现计划。
> 项目当前版本：26.9.20，技术栈：Tauri 2.x + Rust + Vanilla HTML/JS/CSS。

---

## 一、优先级与依赖分组

| 优先级 | 分组 | 包含任务 |
|-------|------|---------|
| P0 | 基础增强（低风险、可独立交付） | #6 网页中文化、#22 配置文件路径可选 |
| P1 | CogSocket / 取图功能组（相互关联） | #21 CogSocket 通信（传输层/会话地基已随 #7 落地 26.9.20，取图与完整相机控制待做）、#2 取图、#7 单元格值设置【已完成 2026-09-17，版本 26.9.20】 |
| P2 | 相机显示增强 | #19 上一时刻截图、#20 嵌套子区域、#12 GigE 协议相机支持 |
| P3 | 工业通信 | #4 EIP/IS8900 模拟通信 |
| P4 | 大型新功能 | #13 轻度视觉检测功能【已完成 2026-09-17，版本 26.9.19】 |

---

## 二、详细任务计划

### 任务 #6：Cognex HMI 网页英文替换为中文（网页语言跟随本软件语言）

- **目标**：
  1. 将主界面及 Cognex HMI 相关界面中残留的英文文本全部替换为中文（已存在 i18n 机制，需补齐缺失词条并检查硬编码英文）。
  2. **Cognex HMI 内嵌网页的语言跟随本软件语言**：当软件切换中/英文时，内嵌的 Cognex HMI 页面同步切换对应语言，保持界面语言一致。
- **范围**：
  - `src/i18n.js`：补全缺失的中文翻译键，确保所有 `t("...")` 引用都有对应中文文案。
  - `src/index.html`、`src/main.js`：检查是否有硬编码英文（如 placeholder、title、按钮文本），改为 `data-i18n` 或 `t()` 调用。
  - Cognex HMI 内嵌页面（iframe / CogSocket 加载的 HMI 页）：实现语言联动。
- **实现步骤**：
  1. 全局搜索 `t("` 收集所有引用键，对比 `i18n.js` 中 `zh` 对象，列出缺失键并补齐。
  2. 用 Grep 搜索 `src/` 下不含 `data-i18n` 的英文文本片段（按钮、label、placeholder、alert 字符串），逐个替换为 i18n 机制或直接中文文案。
  3. 实现 Cognex HMI 语言联动（二选一或组合）：
     - **URL 参数方式**：在加载 HMI 页的 iframe URL 中附加语言参数（如 `?lang=zh` / `?lang=en`），切换软件语言时重新设置 iframe.src。
     - **CogSocket 方式**：通过 CogSocket 向相机发送语言设置命令（PUT 对应单元格/资源），触发 HMI 页面刷新为目标语言。
  4. 在 `toggleLanguage()`（或语言切换入口）中，除了调用 `setLanguage(next)` 外，同步调用 HMI 语言切换逻辑，确保主界面与 HMI 页语言一致。
  5. 软件启动时根据 `appConfig.language` 初始化 HMI 页面语言。
- **验收标准**：
  - 切换到中文时主界面与 Cognex HMI 页均无英文残留；切换到英文时两者均显示英文。
  - 重启后 HMI 页面语言与软件保存的语言设置一致。

---

### 任务 #22：配置文件可选使用默认文件夹或跟随 exe文件　【已完成 2026-09-16】

- **目标**：允许配置文件存放在 exe 同目录（便携模式），便于启动多个不同实例而互不干扰。
- **实际方案**（替代原 `AppConfig.config_mode` 设想，规避"模式开关存在哪个目录"的鸡生蛋问题）：
  1. 启动时 `resolve_config_dir()` 按优先级解析：命令行 `--config-dir <路径>` > exe 同目录标记文件 `portable.txt`（便携模式，配置落在 `.\Configs`）> 系统默认 AppData（原行为）。
  2. 设置页"安全设置 → 配置文件位置"提供两个切换按钮（26.9.17 起已迁入"软件设置"标签页），切换时可选复制现有配置（递归合并、同名覆盖），写/删 `portable.txt` 后重启生效；新增 `restart_app` 命令。
  3. 多实例方式：拷贝整个程序文件夹（各带独立 Configs），或为同一 exe 创建带不同 `--config-dir` 参数的快捷方式。
  4. Program Files 等不可写目录会在写标记文件时返回明确错误；`--config-dir` 模式下界面切换按钮禁用。
- **验收结果**：`cargo check` 通过；设置页显示当前模式/目录并可双向切换（含迁移与重启提示），中英文文案齐全。

---

### 任务 #21：CogSocket 通信协议数据传输

- **目标**：集成 CogSocket（康耐视 In-Sight Web SDK）协议，建立与相机的 WebSocket 通信通道，支持 GET/PUT/POST 与事件监听。
- **范围**：
  - 前端复用 `src/assets/cogsocket/cogsocket.js`（已存在 SDK 脚本）。
  - 新增 `src/cogsocket_manager.js`：封装连接管理、请求/响应、事件订阅。
  - 设置页新增"CogSocket 通信"子区域或标签页。
- **实现步骤**：
  1. 在 `index.html` 中引入 `cogsocket.js`（注意其 `require.js` 依赖方式，可能需调整加载顺序）。
  2. 封装 `CogSocketManager` 类：
     - `connect(host, port)` / `disconnect()`
     - `get(resource)` / `put(resource, value)` / `post(resource, data)`
     - `on(event, callback)` 事件订阅
  3. UI：输入相机 IP/端口，连接状态指示灯，请求/响应日志面板。
  4. 连接信息持久化到新配置文件 `CogSocketConfig.json`。
- **验收标准**：能连接到支持 CogSocket 的相机并完成一次 GET 请求，日志显示响应内容。

---

### 任务 #2：获取 $A$0 单元格图片和结果图 svg（取图设置）

- **目标**：通过 CogSocket 从相机电子表格中获取指定单元格（如 `$A$0`）的图片数据和结果图 SVG，并在界面显示/保存。
- **依赖**：任务 #21（CogSocket 通信通道）。
- **范围**：前端取图模块 + 后端图片保存命令。
- **实现步骤**：
  1. 在 CogSocket 模块基础上，实现 `getCellImage(cell)` 与 `getResultSvg(cell)`。
  2. 取图设置 UI：
     - 单元格地址输入框（默认 `$A$0`）。
     - "取图"按钮：获取图片并在预览区显示。
     - "取结果图"按钮：获取 SVG 并渲染。
     - 保存路径配置 + "保存到本地"按钮。
  3. 后端新增命令 `save_cog_image` / `save_cog_svg`（或复用现有文件写入），将 base64/Blob 数据写入磁盘。
  4. 取图参数（单元格地址、自动刷新间隔、保存目录）持久化到 `CogSocketConfig.json`。
- **验收标准**：输入单元格地址后能取到图片并预览，点击保存能在本地生成对应文件。

---

### 任务 #7：单元格值设置（参数设置页面）【已完成 2026-09-17，版本 26.9.20】

- **实际交付**：`src/cogsocket_manager.js`（WebSocket 会话：hello/openSession/login/resultChanged-ready/keepAlive/根路径兼容）+ `src/cogsocket_cells.js`（可编辑单元格表：单行 setCellValue、批量 setCellValues、手动 JSON 写入、写后 600ms 回读、离线编辑、手动触发、日志区），凭据经 `update_camera_cogsocket_auth` 按相机持久化。
- **与原计划差异**：协议实际用 `post sid/setCellValue`（非 PUT）；单元格行直接从 getLatestResult 的 editable 单元格生成，未做参数行增删与 `CogSocketConfig.json` 预设模板（可后续补充）。
- **验收标准**：达成——Node mock 相机 harness 断言写入帧 `["MyEditInt",10]`、批量帧 `[{...}]`、错误响应 reject；真机回读 ✓/✗ 在 UI 日志与回读列可见（真机联调待用户现场执行）。

---

### 任务 #19：显示相机上一时刻的截图

- **目标**：单个产品多次拍照场景下，能查看相机上一帧/上一时刻的截图。
- **范围**：前端相机格子 + 后端截图缓存。
- **实现步骤**：
  1. 后端新增截图缓存（环形缓冲区，每个相机保留最近 N 帧，默认 5 帧）。
     - 新增命令 `save_camera_snapshot(id, data)`：前端捕获 iframe 截图后上传。
     - `get_camera_snapshot(id, offset)`：offset=0 最新，-1 上一帧，依此类推。
  2. 前端在相机格子头部增加"上一帧"/"下一帧"按钮，点击切换显示历史截图。
  3. 截图来源：优先用 iframe 的 `drawImage`（受跨域限制时回退为从相机 URL 重新拉取）。
  4. 缓存大小可在相机显示设置中配置。
- **验收标准**：相机画面刷新后，点击"上一帧"能看到前一张截图，循环浏览不越界。

---

### 任务 #20："相机显示"支持嵌套子区域显示

- **目标**：在单个相机格子内支持嵌套多个子区域（如画中画、分屏显示多个 ROI 子画面）。
- **范围**：前端 `renderCameraGrid` / `createCameraCell` 重构。
- **实现步骤**：
  1. 扩展 `CamConfigItem`：新增 `sub_regions: Vec<SubRegion>`，每个 SubRegion 含 `x, y, w, h, url, label`。
  2. `createCameraCell` 中渲染主画面 + 多个绝对定位的子区域 iframe。
  3. 新增子区域编辑面板（在格子右键或设置按钮中）：添加/删除/调整子区域位置与 URL。
  4. 子区域配置持久化到 `CameraConfig.json`（后端 `CamConfigItem` 需同步加字段，注意 `#[serde(default)]` 兼容旧配置）。
- **验收标准**：能在一个相机格子内配置并显示多个子区域，重启后布局恢复。

---

### 任务 #12：GigE 协议相机支持

- **目标**：通过 GigE Vision 协议直接接入工业相机（如基恩士、巴斯勒等），获取实时图像，替代/补充当前 iframe 方式。
- **范围**：后端新增 `gige.rs` 模块 + 前端图像渲染。
- **实现步骤**：
  1. 调研可用 Rust GigE Vision 绑定/实现（如通过 FFI 调用厂商 GigE Vision SDK DLL，或基于 UDP 自行实现 GVCP/GVSP 协议）。
  2. 新增 `gige.rs`：
     - 枚举可用相机（`discover_cameras` 命令，基于 GVCP DISCOVERY 广播）。
     - 打开相机、配置采集参数（IP、Packet Size、Stream Channel）、启动/停止采集。
     - 接收 GVSP 流数据包，重组为图像帧，通过 Tauri event 推送到前端（`emit("gige_frame", { id, data })`）。
  3. 前端相机格子新增"采集源"切换：iframe（URL）/ GigE 相机。
  4. GigE 相机列表与所选相机持久化到 `CameraConfig.json`（`CamConfigItem` 新增 `source_type`、`gige_id` 字段，`#[serde(default)]` 兼容旧配置）。
  5. 如需厂商 SDK，将对应 DLL 作为资源打包（`tauri.conf.json` 的 `bundle.resources`）。
- **风险**：GigE Vision 协议较复杂（GVCP 控制 + GVSP 流传输 + 丢包重传），若自行实现需充分测试；建议优先评估厂商 SDK FFI 方案。图像传输性能需评估（大分辨率高帧率可能需要共享内存而非事件推送）。
- **验收标准**：能枚举到 GigE 相机并在格子中显示实时画面，帧率可接受。

---

### 任务 #4：工控机模拟 (Hsl Communication) EIP(EtherNet/IP) 的 IS8900 相机通信

- **目标**：让工控机模拟 IS8900 相机的 EtherNet/IP 通信（参考 Hsl Communication 库的协议实现方式），直接向基恩士 PLC 发送数据，并提供发送数据表格界面。
- **范围**：后端新增 EIP 协议栈模块 + 前端数据表格界面。
- **实现步骤**：
  1. 调研 IS8900 相机与基恩士 PLC 的 EIP 通信协议细节（CIP 对象、Assembly Instance 映射），可参考 Hsl Communication（C# 工业通信库）中 EtherNet/IP 的实现思路，在 Rust 中复刻相应报文构造。
  2. 后端新增 `eip.rs`（或引入 Rust 生态中 `cip` / `ethernet-ip` 相关 crate）：
     - 实现 EIP 适配器角色（模拟相机）。
     - 监听 PLC 连接，处理 CIP 显式报文与 I/O 隐式报文。
     - 提供命令 `set_eip_output_data(data)`：前端写入表格后，后端作为 Output Assembly 数据供 PLC 读取。
  3. 前端新增"EIP 数据表格"标签页：
     - 可配置的发送数据表格（地址 / 数据类型 / 值 / 说明）。
     - "应用"按钮将表格数据序列化后通过 `set_eip_output_data` 下发。
     - 连接状态与通信日志面板。
  4. EIP 配置（IP、端口、Assembly 映射、表格模板）持久化到 `EipConfig.json`。
- **风险**：EIP 协议实现复杂，需真实 PLC 联调；隐式 I/O 报文需保证实时性。
- **验收标准**：PLC 能连接到工控机并读到表格中配置的数据。

---

### 任务 #13：轻度视觉检测功能【已完成 2026-09-17，版本 26.9.19】

> **实际落地方案**（与原设想的差异）：脚本引擎由 quickjs-rust 改为纯 Rust 的 **boa_engine 0.22**（GitHub 不可达时 crates.io 可用，26.9.18 图片清理高级版已引入）；编辑器直接复用已集成的 Monaco 0.56 AMD loader；不暴露过程式 `loadImage/drawROI` API，改为**声明式数据驱动**契约：
> - 入口：软件设置新增"功能开关"分区，`AppConfig.vision_inspection_enabled`（`#[serde(default)]`）持久化，启用后工具栏才出现按钮，进入/退出不新建窗口，与相机网格视图互切。
> - 左栏 `src/vision.js`：Monaco 编辑 `inspect(context)`；中栏：控件表 + 属性表 + 画布双向联动，五种控件 EditRegion/EditCircle/EditPoint/EditLine/EditPolygon（像素几何在 Rust 端计算灰度 `count/mean/stdDev/min/max` 注入 context）；右栏：原图/效果图双 canvas（base64 data URL 无跨域），脚本返回 `{pass,message,overlays,metrics}` 声明式叠加，控制台捕获 console.log/info/warn/error。
> - 后端 `src-tauri/src/vision.rs`：5 个命令，VisionConfig.json 持久化脚本/图片路径/控件；`JSON.stringify` 桥接取回结果（同 clean_script.rs）；boa 0.22 无中断钩子，脚本放工作线程 + 5s `recv_timeout` 超时保护。
> - 验证：cargo check 0 警告、6 个 Rust 单测全部通过（base64/多边形/五种 ROI 统计/boa 端到端）、node --check、默认脚本 OK/NG/空 ROI/异常 harness 全通过。

- **目标**：内置一个轻量视觉检测工作台，三栏布局：
  - 左栏：ECMAScript 代码编辑区（用户编写检测脚本，脚本引擎使用 [quickjs-rust](https://github.com/Lewin671/quickjs-rust)）
  - 中栏：自定义控件区（声明式数据驱动，类似电子表格的可交互控件，用例为 ROI 编辑器等）
  - 右栏：图像显示区 + 日志显示区
- **范围**：前端新增独立标签页/窗口 + 后端图像与脚本执行支持。
- **实现步骤**：
  1. 前端新增"视觉检测"标签页，三栏布局：
     - 左栏：代码编辑器（可引入轻量编辑器如 CodeMirror，或用 textarea + 语法高亮）。
     - 中栏：控件区，由脚本声明式生成（按钮、输入框、ROI 矩形拖拽控件等）。
     - 右栏：Canvas 图像显示 + 日志输出。
  2. 设计脚本 API：
     - `loadImage(source)` / `showImage(img)` / `drawROI(x,y,w,h)`
     - 控件注册：`addButton(name, onClick)` / `addInput(name, default, onChange)`
     - `log(msg)` 输出到日志区。
  3. 后端：
     - 引入 [quickjs-rust](https://github.com/Lewin671/quickjs-rust) 作为脚本执行引擎，在 Rust 侧执行用户脚本以获得图像处理性能；将图像数据与控件事件注入 JS 上下文。
     - 提供图像获取命令（从相机/文件/GigE）。
     - 保存/加载检测脚本到 `VisionScripts/` 目录。
  4. 控件区用例：ROI 编辑器——可拖拽调整矩形区域，脚本读取其坐标做检测。
- **风险**：工作量大，需分阶段交付（先接通 quickjs-rust 脚本执行 + 基本控件，再完善图像处理 API）；quickjs-rust 的 API 需充分调研以确定嵌入方式与对象暴露模型。
- **验收标准**：能在编辑区写脚本加载图片、在中栏生成 ROI 控件、右栏显示图像并输出检测日志。

---

## 三、通用约定

- **配置兼容**：所有新增配置字段使用 `#[serde(default)]`，确保旧配置文件可正常加载。
- **持久化**：新增配置一律走 `AppState::config_path()` 保存，遵循现有 `save_*_config` 模式。
- **i18n**：新增界面文本必须同时提供中英文词条（`src/i18n.js`）。
- **构建验证**：每个任务完成后执行 `cargo check`（或 `cargo build`）确认无编译错误，再运行 `build-with-timestamp.sh` 产出测试包。

---

## 四、建议交付顺序

1. #6 网页中文化（最快，改善体验）
2. #22 配置文件路径可选（基础能力，影响后续多实例调试）
3. #21 CogSocket 通信 → #2 取图 → #7 单元格值设置（按依赖链推进）
4. #19 上一时刻截图（独立小功能）
5. #20 嵌套子区域（相机显示增强）
6. #12 GigE 协议相机支持（需硬件联调，周期较长）
7. #4 EIP 通信（参考 Hsl Communication 实现，需 PLC 联调，周期较长）
8. #13 轻度视觉检测（最大功能，建议最后单独排期）
