# 更新日志 / CHANGELOG

> 记录规则：按**小时**记录当日修改，每条注明涉及文件与打包产物，时间取自文件修改时间与构建产物时间戳。
> 版本号规则：`年.月.日`（如 26.9.16 = 2026-09-16）。

---

## 2026-09-17　版本 26.9.20

### 14:23　新功能：CogSocket 模式单元格值设置（TODO #7，含 #21 传输层地基）

- **会话传输层**（纯前端 WebSocket，不加 Rust ws 依赖，方案依据 CogSocket&WebApi.md 第 18 节）：
  - 新增 [src/cogsocket_manager.js](src/cogsocket_manager.js)：`ws://host:port/ws` 连接（8s 超时）→ `post @/hello` → GET info 自动识别根路径（v3 `cam0/hmi`，失败回退旧版 `system`）→ `openSession`（`$type:HmiSessionInfo, cellNames:['A0:Z599'], enableQueuedResults:true, includeCustomView:true`，与参考软件 `显示软件=20260321` 一致）→ `login[user,password,false]`（locked 即报错）→ 监听 `resultChanged`，**每帧立即 `post sid/ready`** 防止相机停帧 → 15s keepAlive；请求统一 10s 超时、错误消息附带协议错误码；断开时 `dispose` 会话、清定时器、关 socket。
  - 业务方法：`getAllCellNames`/`getLatestResult`/`setCell(name,value)`（setCellValue 数组参数）/`setCells(map)`（setCellValues 参数为包一层 map 的数组）/`setSoftOnline`/`manualTrigger`/`getJobName`/`getState`。
  - SDK 加载：[src/assets/cogsocket/cogsocket.js](src/assets/cogsocket/cogsocket.js) 是 AMD/Node 双 shim，与 Monaco 自带 AMD loader 冲突，改用 `fetch + new Function` 注入伪 module 沙箱加载，零全局 define/require 污染。
- **单元格设置弹窗** 新增 [src/cogsocket_cells.js](src/cogsocket_cells.js) + [src/index.html](src/index.html) `#cogsocket-modal`：
  - 连接栏：目标 host:port（从相机格 URL 解析，默认 80 端口）、用户名/密码（默认 admin/空）、保存凭据、连接/断开、状态灯（灰/黄/绿/红）、作业名显示。
  - 工具栏 + 表格：拉取 getLatestResult 过滤 `editable===true`，按 `$type` 渲染 EditInt/Float（number 输入，显示并校验 min/max）、EditString（text + maxLength）、CheckBox（勾选框）、Button（"执行"按钮，二次确认）；单行"写入"后约 600ms 自动回读显示当前值；"批量写入"一次 setCellValues 提交所有改动行；底部手动行支持名称 + JSON 值（先 JSON.parse 失败按字符串，可写 EditRegion 等复杂对象）。
  - "离线编辑"切 softOnline=false，**关弹窗/断线自动恢复 true**（仿参考软件 FrmGrid 关窗行为）；手动触发；日志区带时间戳、info/warn/error 分级着色、原始错误体可见，便于无真机文档偏差时排障；弹窗关闭即 dispose（相机 HMI 固定 5 连接）。
  - [src/main.js](src/main.js)：仅 cogsocket 连接模式下相机格头部显示新增"单元格"按钮（[src/assets/cells.svg](src/assets/cells.svg)），切换模式只显隐按钮不重建 iframe；弹窗事件仅初始化绑定一次。
- **凭据持久化（明文注意）**：[src-tauri/src/lib.rs](src-tauri/src/lib.rs) `CamConfigItem` 新增 `#[serde(default)] cogsocket_user/cogsocket_password`（旧配置自动补空），3 处构造点同步；新命令 `update_camera_cogsocket_auth(id,user,password)` 并注册 handler。用户名/密码以**明文存入 CameraConfig.json**，与参考软件一致，仅适用于工控可信内网，请勿在公网或共享主机使用。
- 其它：[src/styles.css](src/styles.css) 追加弹窗/表格/状态灯/日志样式（CSS 变量适配双主题）；[src/i18n.js](src/i18n.js) 新增 50 个键中英两套并更新连接模式提示文案（CogSocket 已支持连接与单元格设置，取图显示后续版本）。
- 验证：`cargo check` 0 错误（1m45s）；`node --check`（cogsocket_manager.js/cogsocket_cells.js/main.js/i18n.js，以 .mjs 形式）全部通过；Node mock 相机 harness（真实 cogsocket.js + 伪 WebSocket，临时脚本验证后删除）4 组用例通过：①握手/info/openSession(A0:Z599)/login["admin","",false]/listen 顺序 + 事件后必回 ready + `setCellValue` 帧为 `["MyEditInt",10]` + `setCellValues` 帧为 `[{A:1,B:"x"}]` + 错误 resp 被 reject 且消息含 `[-1]` + dispose；②旧版固件 cam0/hmi 报错回退 `system` 根路径；③离线后断开自动 put softOnline true；④parseCameraTarget 四种输入；另验证 Function 沙箱可加载 SDK 并完成 get 往返；HTML 236 个 id 无重复、弹窗引用 20 个 id 无缺失。**无真机，未做硬件联调**。
- 版本号统一升级为 **26.9.20**：[Cargo.toml](src-tauri/Cargo.toml)、[Cargo.lock](src-tauri/Cargo.lock)、[tauri.conf.json](src-tauri/tauri.conf.json)。

---

## 2026-09-17　版本 26.9.19

### 12:16　新功能：轻度视觉检测工作台（TODO #13）

- **开关链路（持久化，全生命周期一致）**：
  - [src-tauri/src/lib.rs](src-tauri/src/lib.rs)：`AppConfig` 新增 `#[serde(default)] vision_inspection_enabled: bool`（旧 AppConfig.json 无此字段时默认 false，不报错），`impl Default` 同步；启动加载、设置页回显、勾选写入、重启恢复均走既有 `get_app_config`/`set_app_config`，无需新命令。
  - [src/index.html](src/index.html)："软件设置"标签页新增"功能开关"分区（`#ckbx-vision-enabled` + 说明文字）。
  - [src/main.js](src/main.js)：`loadConfigs()` 启动后按配置显隐入口；设置复选框 change 即持久化并 `applyVisionEnabled()`；`updateAppSettingsUI()` 打开设置时回填勾选态。关闭开关时若正停留在视觉检测页会自动退回相机网格。
- **三栏工作台**（开关启用后，主界面工具栏出现"视觉检测"按钮，点击在相机网格/工作台间切换）：
  - 左栏：Monaco 0.56 ECMAScript 编辑器（按需加载、主题联动、载入默认/保存/运行检测）。
  - 中栏：声明式数据驱动 ROI 控件——`EditRegion`（矩形，四角缩放）、`EditCircle`（圆心+半径手柄）、`EditPoint`、`EditLine`（双端点）、`EditPolygon`（顶点手柄+增删顶点）；电子表格式控件表（名称/类型，行选中）+ 属性表（数值双向绑定，画布拖拽与数字输入互相同步）。
  - 右栏：原图/效果图双 canvas（DPR 适配、等比居中）+ 脚本测试控制台（时间戳、warn/error/metric 着色、OK/NG 徽标）。
- **后端新模块** [src-tauri/src/vision.rs](src-tauri/src/vision.rs)：
  - 5 个 Tauri 命令：`get_vision_config`/`set_vision_config`（VisionConfig.json 持久化脚本/图片路径/控件）、`get_default_vision_script`、`load_vision_image`（本地图片校验解码后转 base64 data URL，canvas 不跨域污染；自带 base64 编码无新依赖）、`run_vision_inspection`。
  - 脚本沙箱沿用并升级 [clean_script.rs](src-tauri/src/clean_script.rs) 的 IIFE 桥接：用户定义 `inspect(context)` 返回 `{pass,message,overlays,metrics}`，Rust 端用 `JSON.stringify` 取回后 serde_json 解析，避免逐项遍历 boa 对象。
  - **真实轻度视觉能力**：`image` crate 读图转灰度（0.299/0.587/0.114），按各控件几何（含越界裁剪、像素去重、射线法多边形、圆方程、线段采样）计算 `count/mean/stdDev/min/max`，注入脚本 context。
  - **超时保护**：boa_engine 0.22 无指令中断钩子，脚本放独立工作线程执行，主线程 `recv_timeout(5s)`，死循环返回超时报错不卡死界面。
- **新增前端模块** [src/vision.js](src/vision.js)（约 900 行）：视图切换、Monaco 初始化、控件数据模型/表格/属性表、canvas 指针命中测试与拖拽（屏幕半径恒为 8px）、overlays 渲染（rect/circle/point/line/polygon/text）、控制台；语言切换通过 `app-language-changed` 事件刷新动态文案。
- 其它：[src/assets/vision.svg](src/assets/vision.svg) 新增眼睛图标；[src/styles.css](src/styles.css) 追加工作台全部样式（深/浅主题 CSS 变量适配）；[src/i18n.js](src/i18n.js) 新增 38 个键中英两套。
- 验证：`cargo check` 0 错误 0 警告；`cargo test --lib vision::` 6 个单测全部通过（base64 标准向量、多边形判定、五种控件 ROI 统计/越界裁剪/非均匀灰度均值、默认脚本 boa 端到端返回、坏脚本报错）；`node --check`（main.js/vision.js/i18n.js）通过；元素 ID 唯一性与引用完整性检查、HTML 标签配平检查通过；Node harness 验证默认脚本 OK/NG/空 ROI/异常 4 个用例。
- 版本号统一升级为 **26.9.19**：[Cargo.toml](src-tauri/Cargo.toml)、[Cargo.lock](src-tauri/Cargo.lock)、[tauri.conf.json](src-tauri/tauri.conf.json)。

### 13:06　文档：脚本编程指南（TODO #32）

- 新增 [PROGRAMING.md](PROGRAMING.md)：面向用户脚本编写者，与源码契约逐字段对齐，涵盖——
  - 沙箱通用规则：boa_engine 0.22 语言能力边界（无 DOM/Node/IO/定时器、每次运行无状态）、console.log/info/warn/error 行为、视觉脚本 5s 超时、错误不崩软件。
  - 视觉检测 `inspect(context)`：context/五种控件几何/灰度统计口径（0.299/0.587/0.114、圆像素中心、线 0.5px 采样、多边形射线法、越界裁剪与去重、count=0 时统计为 null）、返回值 `{pass,message,overlays,metrics}`、六种 overlay 字段表（默认色 #f97316）、默认脚本完整收录与 2 个进阶示例（按控件名阈值、stdDev 异物检测）。
  - 图片清理高级版 `evaluate(context)`：逐文件从旧到新调用、true 删/false 与异常停止的安全语义、11 个驼峰字段表（与 ImageCleanerAutoWeld 对齐）、默认 AND 脚本与 2 个示例。
  - 排错速查表（入口缺失/返回类型错误/null 统计/超时/脚本不生效等）与能力边界清单（PNG/BMP/JPEG）。
- 同步：[TODO.md](TODO.md) 第 32 项划线完成。仅文档变更，无代码改动，不升版本号（归入 26.9.19）。

---

## 2026-09-17　版本 26.9.18

### 11:16　新功能：相机连接模式（http/cogsocket/gige）持久化 + 相机设置标签页（TODO #25）

- **范围**：实现三种相机连接模式（http、cogsocket、gige）的**选择与持久化**，默认 http。CogSocket/GigE 协议的实际通信实现属 TODO #2/#12/#21，不在本次范围；用户选择 cogsocket/gige 时会持久化并回显，相机显示暂仍按 http 加载，标签页显示提示。
- [src-tauri/src/lib.rs](src-tauri/src/lib.rs)：`AppConfig` 新增 `connection_mode: String` 字段，`#[serde(default = "default_connection_mode")]` 保证旧配置文件无此字段时反序列化不报错；`default_connection_mode()` 返回 `"http"`；`impl Default` 同步追加。无需新增 Tauri 命令——`get_app_config`/`set_app_config` 已处理整个结构体。
- [src/index.html](src/index.html)：`tabs-row` 首行 `tab-camera` 后新增"相机设置"按钮（`data-tab="tab-camera-settings"`）；`tab-content` 新增 `#tab-camera-settings` 面板，含连接模式下拉框 `<select id="cmbx-connection-mode">`（http/cogsocket/gige 三个 `<option>`，均带 `data-i18n`）与提示文本。
- [src/main.js](src/main.js)：`loadConfigs()` 中 `get_app_config` 后回显下拉框（`appConfig.connection_mode || "http"`）；`setupEventListeners` 新增下拉框 `change` 事件 → 写入 `appConfig.connection_mode` + `saveAppConfig()`；`updateAppSettingsUI()` 打开设置时同步回显选中态。
- [src/i18n.js](src/i18n.js)：新增 6 键中英两套：`cameraSettings`/`connectionMode`/`modeHttp`/`modeCogsocket`/`modeGige`/`connectionModeNote`。
- 验证：`cargo check` 通过、`node --check`（main.js/i18n.js）通过。

### 10:54　修复：设置页 splashscreen 窗口白屏（TODO #31）

- **问题**：点击"设置"按钮后，`show_settings_splash` 动态创建的 420×240 加载窗口在 webview 首帧绘制前显示**纯白底**（与启动画面、主窗的 `#1b1b1f` 深色底不一致），用户感知为闪白。
- **根因**：[src-tauri/src/lib.rs](src-tauri/src/lib.rs) 的 `show_settings_splash` 命令使用 `WebviewWindowBuilder` 动态建窗时**未设置 `background_color`**，窗口原生底色为默认白色；而主窗（`lib.rs:2189`）与静态启动窗（`tauri.conf.json` 的 `"backgroundColor": "#1b1b1f"`）均已显式设置深色底。`settings-splash.html` 内联的 `background: #1b1b1f` 只在 HTML 解析后才生效，无法覆盖窗口创建到首帧之间的白底期。
- **修复**：在 `show_settings_splash` 的 `WebviewWindowBuilder` 链上追加 `.background_color(tauri::webview::Color(0x1b, 0x1b, 0x1f, 0xff))`，与主窗、启动画面同色，消除窗口原生底色的白屏期。纯 Rust 单行改动，前端无变更。
- 验证：`cargo check` 通过（6.05s，0 错误）。

### 19:20　图片清理高级版（QuickJS/boa_engine 脚本判定 + Monaco 编辑器）

- 版本号统一升级为 **26.9.18**：
  - [src-tauri/Cargo.toml](src-tauri/Cargo.toml)、[src-tauri/tauri.conf.json](src-tauri/tauri.conf.json)：`26.9.17` → `26.9.18`
- **图片清理标签页新增"高级版"开关**，复刻 [ImageCleanerAutoWeld](D:\JustStupid\CameraHelper相机显示_2606120922\ImageCleanerAutoWeld) 的 `delete_conditions.js` 脚本判定功能（TODO #30）：
  - 新增 [src-tauri/src/clean_script.rs](src-tauri/src/clean_script.rs)：`CleanScriptContext`（11 字段驼峰命名，与 ImageCleanerAutoWeld 对齐）、`evaluate_decision` 函数、`DEFAULT_SCRIPT` 常量（与 `delete_conditions.js` 默认 AND 逻辑一致）、3 个 Tauri 命令（`test_clean_script`/`get_default_clean_script`/`sample_clean_context`）
  - **脚本引擎**：原计划使用 `qjs_runtime`（Lewin671/quickjs-rust），因 GitHub 网络不可达改用 **`boa_engine` 0.22.0**（crates.io，纯 Rust，无 C 依赖）作为 fallback，`default-features = false` + `float16/xsum/temporal` features
  - **IIFE + 对象返回桥接**：boa `Context::eval` 拼装 IIFE（console 垫片 + 用户脚本 + `evaluate(ctx)` 调用），返回 `{ decision, console }` 对象，Rust 端通过 `as_object().get()` 取回判定与日志
  - [src-tauri/src/lib.rs](src-tauri/src/lib.rs)：
    - `mod clean_script` 接线
    - `CleanConfig` 新增 4 字段（全 `#[serde(default)]`）：`is_advanced`、`delete_script`、`is_enable_image_count`、`image_count_threshold`
    - `clean_once` 改造：`is_advanced=true` 且脚本非空时走脚本判定（每删一个重新评估，直到脚本返回 false），`is_advanced=false` 保留旧 OR 行为零回归
    - `generate_handler!` 注册 3 个新命令
  - 前端 [src/index.html](src/index.html)：`<head>` 加载 Monaco loader/editor.main.css；`tab-clean` 面板加高级版开关 + 折叠容器（数量条件、脚本编辑器、载入默认/测试按钮、结果输出区、提示）
  - 前端 [src/main.js](src/main.js)：`initCleanMonaco`（AMD `require` 按需加载 Monaco）、`testCleanScript`（调 `test_clean_script` 展示判定结果+console+错误）、`applyCleanConfig` 追加 4 个新字段、表单回显追加高级版状态、主题切换联动 Monaco 主题
  - [src/styles.css](src/styles.css)：`.clean-advanced-section`/`.clean-editor-wrap`(260px)/`.clean-test-result`/`.btn-secondary`/`.clean-advanced-hint`
  - [src/i18n.js](src/i18n.js)：14 个新键（cleanAdvanced/enableImageCount/deleteScript/loadDefault/test/decisionResult/decisionDelete/decisionKeep/decisionNone/cleanScriptRunning/cleanScriptError/cleanAdvancedHint）中英两套
- 验证：`cargo check`（boa_engine 编译通过）、`node --check`（main.js/i18n.js 语法通过）

---

## 2026-09-17　版本 26.9.17

### 07:19　版本号升级

- 版本号统一升级为 **26.9.17**：
  - [src-tauri/Cargo.toml](src-tauri/Cargo.toml)、[src-tauri/Cargo.lock](src-tauri/Cargo.lock)（`camera-viewer-tauri` 锁定版本）、[src-tauri/tauri.conf.json](src-tauri/tauri.conf.json)：`26.9.16` → `26.9.17`
  - [README.md](README.md)：当前版本更新；版本记录新增 26.9.17 条目，并补记 26.9.16 的 NTP/局域网扫描参数名修复
- 本版本相对 26.9.16 最后一个打包（`CameraViewerTauri_202609162012.exe`，20:21）包含的增量（详见 2026-09-16 日志 22:22 之后条目）：
  - 关于页开源链接、配置文件位置可选/便携模式/多实例（TODO #22）、monaco-editor 0.56.0 本地化、NTP/FTP 端到端测试脚本。

### 07:30　新功能：启动画面（进度条 + 毫秒计时器），消除启动白屏（TODO #26）

- **问题**：窗口创建后到主界面首帧绘制之间存在较长白屏期（8 个配置 invoke 串行加载 + 外部资源解析）。
- **三层覆盖，消除各阶段白屏**：
  1. [src-tauri/tauri.conf.json](src-tauri/tauri.conf.json)：窗口新增 `"backgroundColor": "#1b1b1f"`，webview 首帧绘制前的**窗口底色**与启动画面一致（Tauri 2 Color 配置，已核对 tauri-utils 2.9.2 支持 `#rrggbb` 字符串）。
  2. [src/index.html](src/index.html)：`<head>` 最前面内联启动画面关键 CSS（先于 styles.css 等所有外部资源）；`<body>` 首元素 `#app-splash` 同时带内联 `style="display:flex"` 双保险，保证**首帧即覆盖**。画面含内联 SVG 相机 logo、应用名、indeterminate 进度条、双语"正在启动… / Loading…"、实时跳动的**毫秒计时器**（33ms 刷新，`<head>` 脚本在解析阶段即记录 `performance.now()` 起点，不依赖任何外部文件）。
  3. [src/main.js](src/main.js)：初始化流程包 try/catch/finally，完成后 `hideSplash()`——主界面绘制一帧后 280ms 淡出移除，控制台输出最终 `[启动耗时] UI 就绪：xxx ms`；初始化抛错也会移除画面。
- **防卡死兜底**：内联脚本内置 12 秒超时，主脚本异常未移除画面时强制淡出，避免永久遮罩。
- 启动画面文案在语言配置加载前显示，采用中英双语静态文案，不引入 i18n 依赖。
- 验证：JS 语法检查、tauri.conf.json JSON 校验通过；`cargo check` 通过。

### 07:48　新功能：新增"软件设置"标签页（语言/主题/配置位置）；设置弹窗点击即显示加载动画（TODO #28、#29）

- **#28 软件设置标签页**：
  - [src/index.html](src/index.html)：设置弹窗第二行标签新增"软件设置"（`tab-app`）；原位于"安全设置"的"配置文件位置"区块**整体迁移**至此（元素 ID 不变，Rust 命令与监听逻辑零改动）。
  - 新增"界面语言"分段控件（中文 / English）与"界面主题"分段控件（浅色 / 深色），功能与主界面工具栏图标按钮一致：复用 `setLanguage`/`applyTheme`，持久化到 `AppConfig.json`，工具栏图标、状态栏文本实时联动。
  - [src/main.js](src/main.js)：抽出共用函数 `setAppLanguage()` / `setAppTheme()`，主界面 `toggleLanguage()`/`toggleTheme()` 改为调用它们；新增 `updateAppSettingsUI()` 同步分段控件选中态（任一侧切换均双向同步）；打开设置时刷新选中态。
  - [src/i18n.js](src/i18n.js)：新增 8 个键中英文（appSettings、languageSetting、languageZh/En、themeSetting、themeLight/Dark、settingsLoading）。
  - [src/styles.css](src/styles.css)：新增 `.segmented`/`.seg-btn` 分段控件样式（accent 选中态，明暗主题自适应）。
- **#29 设置页加载动画**：
  - 点击"设置"后**同一帧**移除弹窗与加载遮罩的 hidden，遮罩含旋转 spinner、"正在加载设置…"文案和 4 条 shimmer 骨架条；双 `requestAnimationFrame` 确保遮罩先绘制，再执行数据加载——即使后续状态查询（防火墙 netsh、UAC 注册表、网络接口枚举等）耗时，用户也立即看到反馈。
  - `loadSettingData()` 改为 async：表单同步填充后，9 项异步状态查询通过 `Promise.allSettled` **并行**发起（原先串行触发），全部结束才淡出遮罩；单项失败不影响其他面板与遮罩关闭；子网静默探测保持后台执行不参与等待。
  - 用加载代号 `settingLoadSeq` 防止快速重复点击时旧加载提前隐藏新遮罩；关闭弹窗同步隐藏遮罩。
- 验证：main.js/i18n.js 语法检查通过；9 个关键 ID 唯一性、8 个 i18n 键双语言覆盖脚本校验通过；index.html div 标签平衡（166/166）。纯前端改动，无需 Rust 重新编译。

### 08:33　重构：启动画面改用 Tauri 官方 splashscreen 方式（独立窗口，替代 07:30 的页内遮罩）

- 按 [Tauri 官方启动画面指南](https://tauri.org.cn/v1/guides/features/splashscreen/)（v1 文档，本项目按 **Tauri 2 API** 适配）实现：启动时显示**独立 splashscreen 窗口**，主窗口 `visible:false` 隐藏，主界面就绪后由 Rust 命令关闭启动窗并显示主窗。
- 新增 [src/splashscreen.html](src/splashscreen.html)：完全自包含（内联 CSS/JS/内联 SVG，零外部资源），460×300 无边框居中窗口，深色 `#1b1b1f` 与主窗底色一致；含相机 logo、应用名、indeterminate 进度条、双语"正在启动… / Loading…"、**实时毫秒计时器**（从启动窗文档加载起 33ms 刷新）。
- [src-tauri/tauri.conf.json](src-tauri/tauri.conf.json)：主窗显式 `"label": "main"` + `"visible": false`；新增 splashscreen 窗口（label `splashscreen`、url `splashscreen.html`、`decorations:false`、`alwaysOnTop`、`skipTaskbar`、同底色；字段名已经 tauri-utils 2.9.2 源码核对）。
- [src-tauri/src/lib.rs](src-tauri/src/lib.rs)：
  - 新增 `close_splashscreen` 异步命令：`get_webview_window("splashscreen").close()` + 主窗 `show()/set_focus()`，幂等（v1 指南的 `get_window`/`Window` 参数在 v2 中改为 `get_webview_window`/`AppHandle`），已注册。
  - setup 中新增 **15 秒兜底线程**：前端异常未调用命令时强制关启动窗、显示主窗，避免界面永久不可见。
- 前端：[src/main.js](src/main.js) 初始化 finally 中改为主界面绘制两帧后 `invoke("close_splashscreen")`，控制台仍输出 `[启动耗时] UI 就绪：xxx ms`；删除 index.html 中 07:30 版页内遮罩的全部内联 CSS/JS 与 `#app-splash` 元素（页内遮罩只覆盖 webview 内容、无法消除窗口创建初期的白底；独立窗口方案覆盖从进程启动到主界面就绪的全过程）。
- 权限：splashscreen 为纯静态页不发起 IPC，capabilities 仍仅授权 `main` 窗口即可。
- 验证：JS/JSON 校验通过；`cargo check` 通过（11.42s，0 错误）。

### 08:45　重构：打开设置页改用 splashscreen 技术（独立加载窗口，替代 #29 的弹窗内遮罩）

- 将 #29 的"设置弹窗内遮罩"升级为与启动画面相同的 **splashscreen 独立窗口技术**：点击"设置"立即由 Rust 动态创建 420×240 无边框、置顶、不进任务栏、屏幕居中的加载窗口；9 项状态查询（防火墙 netsh、UAC 注册表、网络接口枚举等）全部完成后关闭该窗口并把焦点交还主窗。
- 新增 [src/settings-splash.html](src/settings-splash.html)：完全自包含（内联 CSS/JS/内联齿轮 SVG，零外部资源、不发起任何 IPC），含旋转齿轮、双语"正在加载设置… / Loading settings…"、indeterminate 进度条、实时毫秒计时器，视觉与启动画面统一。
- [src-tauri/src/lib.rs](src-tauri/src/lib.rs)：
  - 新增 `show_settings_splash` 同步命令：`WebviewWindowBuilder`（label `settings-splash`，`WebviewUrl::App("settings-splash.html")`，decorations/alwaysOnTop/skipTaskbar/center）动态建窗——splash 窗口与启动画面的静态配置窗口不同，是**运行时按需创建/销毁**的；幂等。
  - 新增 `close_settings_splash` 异步命令：关闭加载窗口 + 主窗 `set_focus()`；幂等。两命令均已注册。
  - 导入新增 `webview::WebviewWindowBuilder`、`WebviewUrl`（路径与方法已经 tauri 2.11.2 源码核对）。
- 前端：[src/main.js](src/main.js) `openSetting` 改为先 `invoke("show_settings_splash")` 当帧获得反馈，再显示弹窗，两帧后执行并行加载，finally 中 `close_settings_splash`；`closeSetting` 在加载中途关闭时也确保关闭 splash 窗口；`settingLoadSeq` 防重入代号保留。
- 清理：删除 #29 的 `#setting-loading` 遮罩 DOM、全部遮罩/spinner/骨架条 CSS（含 `position:relative` 补丁与两个 keyframes）、i18n 键 `settingsLoading`（加载窗为静态双语页，无需 i18n）；`loadSettingData` 的 `Promise.allSettled` 9 项并行加载逻辑保留。
- 验证：main.js/i18n.js 语法检查通过、全 src 目录无旧遮罩残留引用；`cargo check` 通过（0 错误）。

### 09:20　新增：相机 HMI 网页 i18n 文本替换（/pages/hmi/，语言跟随软件界面）

- 需求：相机（Cognex In-Sight IS8905MX，Web HMI 路径 `/pages/hmi/`）网页内容随软件界面语言自动翻译，采用**直接文本替换**方式。
- 关键约束与方案：HMI 在跨域 iframe 中加载，浏览器同源策略禁止父页面改写其 DOM；经实测确认 Framework.js（9.38MB RequireJS 打包框架）几乎不含可见文案，分组标题/Settings 对话框模板（"Override Size""Layout Position"等）由设备运行时数据下发——静态改写不可行。方案为 **Tauri `initialization_script` 宿主层注入**：脚本在每个 frame 文档创建期随页面自身上下文执行，对该 frame DOM 有完整权限（含跨域 iframe）。
- 新增 [src/assets/hmi-i18n.js](src/assets/hmi-i18n.js)（`include_str!` 编译期烘焙，不新增运行时资源依赖）：
  - 仅在 iframe 内且路径匹配 `/pages/hmi/` 激活；默认英文（原文），父页面 postMessage 下发 `{__hmiI18n:"lang",lang}` 后切换；
  - **整串精确匹配替换**文本节点与 `title`/`placeholder`/`aria-label` 属性（保留首尾空白），不做子串替换；
  - MutationObserver 批处理（16ms 合并）框架后续动态渲染/重绘节点；切回英文时按记录的原文（`__hmiOrig`/`data-hmi-orig-*`）完整还原；
  - 防误伤：易混淆词 "OK"（与检测结果 OK/NG 同形）仅在位于 `cjsButton` 按钮元素内时替换为"确定"。
- [src-tauri/src/lib.rs](src-tauri/src/lib.rs)：主窗口改为 setup 中 `WebviewWindowBuilder` 创建（conf.json 不再静态声明 main），参数与原配置一致（1200×800/最大化/居中/隐藏/底色），挂载初始化脚本；导入已在 splash 改造中完成。
- [src/main.js](src/main.js)：新增 `postHmiLang`/`broadcastHmiLang`；iframe `load` 时主动下发语言、监听注入脚本 ready 握手回传；`setAppLanguage` 中广播到全部相机 iframe。
- 翻译覆盖（38 项，均经真实页面实测）：Online/Offline/Trigger/Load/Save/Settings、Validation/View Options 分组标题与 Run Job Validation/System Validation/Live Mode、EasyView/Custom View/Image/Graphics、Cancel/Reset/Close/Next/Back/Accept/Freeze、图像工具栏全部 title（Zoom In/Out、Fit、Fill、Rotate、Pan、像素数据等）、Not Supported/Disabled、Settings 对话框标签（Result Queue/Layout Position/Override Size/Width/Height/Top/Bottom/Point Cloud 等）、作业验证进度文案、Cookie 提示整句。**不译**：设备名、作业文件名、用户名、自定义视图名、作业内中文标签、NG/OK 结果、数值与日期时间。
- 验证：用临时 CORS 服务在**真实 HMI 页面**注入实测——中文模式全部词条正确替换、中文作业标签与 NG/OK 结果保持原样、切回英文完整还原无残留、控制台无报错；`cargo check` 通过（10.86s）。临时文件/服务已清理。注：跨域 iframe 内宿主脚本执行行为基于 wry/WebView2 的 init script 全 frame 注入机制，建议打包后在相机环境做一次实机确认。

---

## 2026-09-16　版本 26.9.16

### 15:00　版本号升级

- 版本号统一升级为 **26.9.16**
  - [src-tauri/Cargo.toml](src-tauri/Cargo.toml)：`26.7.31` → `26.9.16`
  - [src-tauri/tauri.conf.json](src-tauri/tauri.conf.json)：`26.7.25` → `26.9.16`
  - `Cargo.lock` 随构建同步更新

### 15:08　修复：相机网格锁定/解锁状态不持久化

- **现象**：点击锁定按钮无反应；锁定状态重启软件后丢失。
- **修复内容**：
  - [src-tauri/src/lib.rs](src-tauri/src/lib.rs)
    - `CamConfigItem` 新增 `locked: bool` 字段（`#[serde(default)]` 兼容旧配置）
    - 新增 `update_camera_lock` 命令并注册到 `generate_handler!`，锁定状态写入 `CameraConfig.json`
  - [src/main.js](src/main.js)
    - 修正锁定按钮监听的参数错位（`toggleLock` 实参顺序与函数签名不一致，导致点击无响应）
    - 渲染时根据 `item.locked` 恢复只读状态、锁定图标、按钮高亮
    - 切换时调用 `invoke("update_camera_lock", { index, locked })` 持久化
- **打包**：`CameraViewerTauri_202609161508.exe`（15:18 产出，18.9 MB）
  - 构建期间曾遇到 `missing field locked` 编译错误（结构体某处构造点漏填字段），补全后构建通过。

### 15:27　修复：打开设置页弹窗“扫描局域网设备失败”

- **现象**：中文 Windows 下打开系统设置页即弹出扫描失败提示。
- **根因**：
  1. `ipconfig` 输出的 IPv4 地址带 `(首选)` / `（首选）` / `(Preferred)` 后缀，子网解析失败；
  2. 设置页加载时调用子网探测，事件对象被当作 `silent` 参数（真值），失败仍弹窗。
- **修复内容**：
  - [src-tauri/src/debug_tools.rs](src-tauri/src/debug_tools.rs)：新增 `strip_ip_suffix()`，解析 IPv4 前剥离语言相关后缀
  - [src/main.js](src/main.js)：`detectLanSubnet(silent)` 增加显式布尔参数；设置页加载时静默探测（失败仅 console.warn，只回填输入框），手动按钮点击仍弹窗提示
- **打包**：`CameraViewerTauri_202609161529.exe`（15:41 产出，18.9 MB）

### 16:36　文档：更新 README.md

- [README.md](README.md)：版本更新为 26.9.16；补充锁定状态持久化、局域网子网静默预填说明与版本记录段落。

### 16:43　文档：TODO.md 收尾 + 新建 PLAN.md

- [TODO.md](TODO.md)：第 23、24 项（锁定持久化、局域网扫描弹窗）标记完成。
- [PLAN.md](PLAN.md)：根据 TODO 待办项生成实施计划，10 项任务按 P0–P4 优先级分组；同步 Hsl 通信、Cognex HMI、GigE（替代 GenTL）、quickjs-rust 等决策。

### 16:52　文档：扩展 CogSocket&WebApi.md

- [CogSocket&WebApi.md](CogSocket&WebApi.md)：基于 3 份 In-Sight 官方 PDF（Web SDK 26.1.0 等）扩展，408 行 → 859 行（约 29 KB）。
- 新增内容：固件 5.x/6.x/22.2+ 连接差异与 URL 查询参数、CogSocket 协议细节、HMI 资源树与会话流程、HmiResult/ViewRecord/ImageLayer 结构、22 种图形类型、15 种单元格结果类型、单元格读写、CameraInfo/HmiSettings/UserAccessInfo（17 个权限常量）、30+ HmiSession 方法、Platform REST API（审计日志/证书/固件/备份恢复）、API 1.0→26.1.0 版本演进。

### 19:23　修复：NTP 测试命令参数名不匹配

- **现象**：NTP 校时页点击测试报错
  `invalid args ^hostPort for command 'test_ntp_server': command test_ntp_server missing required key hostPort`。
- **根因**：Tauri v2 要求 JS 侧参数使用 **camelCase** 键名（自动映射到 Rust 端 snake_case 参数），前端误传 `host_port`。
- **修复内容**（均在 [src/main.js](src/main.js)）：
  - `invoke("test_ntp_server", { host_port })` → `{ hostPort: host_port }`
  - 顺带修复同类隐患：`scan_lan_devices` 的 `timeout_ms` → `timeoutMs`
    （该参数为 `Option<u32>`，键名不匹配时不报错而静默回退默认 800ms，用户设置的扫描超时此前一直未生效）
  - 返回值字段（如 `result.elapsed_ms`）保持 snake_case 不变：返回值走 serde 序列化，不经过 camelCase 转换。
- 本次仅改前端 JS，Rust 端无改动。

### 20:12　重新打包

- 执行 `build-with-timestamp.sh`：`CameraViewerTauri_202609162012.exe`（20:21 产出，18.9 MB），含 19:23 的 NTP/局域网扫描参数修复。

### 22:22　新增：关于页面与 README 添加开源链接

- 关于弹窗新增"开源链接"信息行，展示可点击的仓库地址，点击通过 `tauri-plugin-opener`（`opener:default` 权限已授权）调用系统默认浏览器打开：
  - [src/i18n.js](src/i18n.js)：新增 `aboutOpenSource` 文案（中文"开源链接" / 英文"Open Source"）
  - [src/main.js](src/main.js)：`showAbout()` 中新增链接行，拦截点击并调用 `window.__TAURI__.opener.openUrl()`，避免在 webview 内跳转
  - [src/styles.css](src/styles.css)：新增 `.about-link` 样式（使用主题 accent 色，hover 下划线，明暗主题自适应）
- [README.md](README.md)：版本号下方新增"开源仓库：<https://github.com/JustSmartAuto/CameraViewerTauri>"。
- 本次仅改前端，Rust 端无改动。

### 22:36　依赖：下载 monaco-editor 0.56.0 到本地 assets

- 从 npm 官方源下载 `monaco-editor@0.56.0` tarball（`npm pack` 单一权威来源），仅提取浏览器运行所需的 `min/vs` 产物。
- 落盘位置：[src/assets/monaco-editor/](src/assets/monaco-editor/)（151 个文件，23.3 MB）
  - `vs/loader.js`：AMD 加载器；`vs/editor/editor.main.js` / `editor.main.css`：编辑器主模块
  - 语言 Worker：`vs/ts.worker-*.js`、`css.worker-*.js`、`html.worker-*.js`、`json.worker-*.js` 及 `vs/editor/editor.worker.js`
  - 附带 `LICENSE`（MIT）
- 用途：为后续视觉脚本编辑器（TODO #13，quickjs-rust + ES 编辑）准备代码编辑器组件。
- 尚未在页面中引入，本次仅下载资源。

### 23:01　新功能：配置文件位置可选（默认目录 / 跟随 exe 便携模式，支持多实例，TODO #22）

- **需求**：配置文件可选系统默认文件夹或跟随 exe，方便启动多个互不干扰的实例。
- **方案**：启动时 `resolve_config_dir()` 按优先级解析配置根目录——
  1. 命令行 `--config-dir <路径>`（同一 exe 建不同快捷方式即独立实例）；
  2. exe 同目录存在标记文件 `portable.txt` → 便携模式，配置落在程序目录 `Configs/`（拷贝整个程序文件夹即独立实例）；
  3. 否则系统默认 AppData（原行为，完全兼容）。
  - 用标记文件而非配置项存储模式，规避"模式开关本身该存哪个目录"的鸡生蛋问题。
- [src-tauri/src/lib.rs](src-tauri/src/lib.rs)：
  - `AppState` 新增 `config_mode` 字段；新增 `resolve_config_dir()` / `exe_dir()` / `copy_dir_contents()`（递归合并复制、同名覆盖）
  - 新增命令 `get_config_storage_info`（当前模式/目录/标记状态）、`switch_config_storage(portable, migrate)`（双向切换 + 可选迁移配置，Program Files 不可写时返回明确错误）、`restart_app`（`AppHandle::restart()`），均已注册
  - 切换仅写/删标记文件，**重启后生效**
- 前端：
  - [src/index.html](src/index.html)：安全设置页新增"配置文件位置"区块（当前模式、配置目录、说明、两个切换按钮）
  - [src/main.js](src/main.js)：`updateConfigStorageInfo()` / `switchConfigStorage()`；切换前确认是否迁移配置，成功后提示立即重启；已切换未重启时禁用按钮并显示待生效提示；`custom` 模式禁用界面切换
  - [src/i18n.js](src/i18n.js)：16 个新键中英文文案；[src/styles.css](src/styles.css)：长路径换行/说明文字样式
- 验证：`cargo check` 通过（2m53s，0 错误）；JS 语法检查通过。
- 文档：[TODO.md](TODO.md) #22 标记完成；[PLAN.md](PLAN.md) #22 更新实际方案；[README.md](README.md) 功能列表新增说明。

### 23:12　测试：完善 scripts 目录端到端测试脚本

- 补全此前为空文件的两个黑盒 E2E 脚本（纯 Python 标准库，无需 pip 安装，Python 3.8+ 可用）：
  - [scripts/ntp-test-end2end.py](scripts/ntp-test-end2end.py)：UDP NTP 协议测试。校验 48 字节响应、`0x1C` 头（LI=0/VN=3/Mode=4）、stratum=1、precision=0xFA、refid="LOCL"、origin 时间戳回显、时间戳顺序，并按软件实际行为校验返回时间为 **UTC+8**（可 `--expect-offset-hours 0` 切换标准 NTP 断言）；异常用例验证 mode≠3 与短包被静默丢弃；支持重试。
  - [scripts/ftp-test-end2end.py](scripts/ftp-test-end2end.py)：FTP 全链路测试（ftplib，PASV）。覆盖 220 banner、未知用户/错误密码 530、空密码用户登录、PWD/NOOP、MKD/RMD、STOR/RETR 二进制 SHA256 一致性、**嵌套目录自动递归创建**、NLST、DELE 清理、`--root` 本地落盘校验、`--tls` FTPS（PROT P）。
  - 两个脚本均内置 `--selftest`：在回环口启动与 Rust 实现行为一致的模拟服务（NTP mock / 最小 FTP mock），**无需启动软件即可验证测试逻辑本身**；对真实软件测试时通过命令行参数指定 host/port/用户/根目录。
  - 统一输出 `[PASS]/[FAIL]/[SKIP]` 与通过/失败统计，退出码 0/1，便于接入 CI 或打包后回归。
- 验证：`py_compile` 通过；NTP selftest 15/15 通过；FTP selftest 21/21 通过（修复了一处测试断言错误：`ftplib.mkd()` 返回解析后的目录名而非 257 原始响应）；服务未启动时正确报错并返回退出码 1。

---

## 历史版本（打包记录）

| 日期 | 安装包 |
|------|--------|
| 2026-07-31 | `CameraViewerTauri_202607311714.exe`、`CameraViewerTauri_202607312231.exe` |
| 2026-07-26 | `CameraViewerTauri_26.7.25_x64-setup.exe`、`CameraViewerTauri_2607261616.exe` |
| 2026-06-25 | `CameraViewerTauri_202606251751.exe` |
| 2026-06-12 | `CameraViewerTauri_202606121037.exe` |
