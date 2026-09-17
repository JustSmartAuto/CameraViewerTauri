# CogSocket 模式单元格值设置（TODO #7）实施计划

## Repository Research

### 需求与范围
- TODO #7：CogSocket 模式下设置相机电子表格单元格值，参考 C# 项目 `显示软件=20260321`。
- 参考软件（WinForms）用 Cognex 官方 .NET 控件 `CvsSpreadsheet` 实现整张电子表格编辑（`FrmGrid.cs`：离线编辑按钮 → `SetSoftOnlineAsync(false)` → 控件内编辑 → `SaveJob` → 关窗恢复在线）。Web 端无此控件，按 `CogSocket&WebApi.md` 第 10 节用 `setCellValue/setCellValues` 自行实现。
- **本次范围**：#7 单元格值设置，及其必需的 CogSocket 会话地基（连接/握手/openSession/login/keepAlive/ready 泵），即 #21 的传输层最小闭环。**不含** #19 胶片视图、#20 嵌套子区域；相机画面区在 cogsocket 模式下仍保留现有 HMI iframe（#2 取图另做）。

### 现状
- 协议文档：`CogSocket&WebApi.md` 完备（消息结构、会话流程、单元格 API、错误码、根路径 v3=`cam0/hmi`/旧版=`system`）。
- SDK：`src/assets/cogsocket/cogsocket.js`（CogSocket 类，AMD/Node 双 shim，构造时传入已连接 `WebSocket`；get/put/post/addListener 回调式）+ require.js + 两个测试页。
- 连接模式：`AppConfig.connection_mode`（http/cogsocket/gige）已持久化（#25），相机设置标签页有下拉框，目前 cogsocket 仅选择不生效；标签有"后续版本支持"提示。
- 相机网格：`main.js renderCameraGrid/createCameraCell` 每格头部有 URL 输入/锁定/刷新/备注/最大化，内容为 iframe；每相机配置 `CamConfigItem{id,ip,remark,locked}`（ip 存完整 URL），命令 `update_camera_ip/update_camera_lock`。
- 页面 `main.js` 为 ES module；tauri.conf `csp:null`（ws:// 与 http:// 图片无需改 CSP）。
- Monaco 自带 AMD loader 占用全局 `define/require`，**不能**再注入 RequireJS 加载 cogsocket.js。
- 参考软件会话参数（`CvsInSightExt.Connect`）：SheetName="Inspection"、CellNames=["A0:Z599"]、EnableQueuedResults=true、IncludeCustomView=true；凭据默认 admin/空密码。
- 单元格结果类型中可编辑：HmiEditFloatResult(min/max)、HmiEditIntResult(min/max)、HmiEditStringResult(maxLength)、HmiCheckBoxResult、HmiButtonResult；通用字段 `location/name/data/disabled/error/editable`。

## Files and Modules

### 新建
- `src/cogsocket_manager.js`：CogSocket 传输+会话封装（ES module，零全局污染）。
- `src/cogsocket_cells.js`：单元格设置弹窗 UI 模块（连接栏 + 可编辑单元格表 + 手动写入 + 日志）。
- `src/assets/cells.svg`：单元格按钮图标（表格样式）。

### 修改
- `src-tauri/src/lib.rs`：
  - `CamConfigItem` 增加 `#[serde(default)] cogsocket_user: String`、`#[serde(default)] cogsocket_password: String`（默认 admin / 空）；同步 3 处构造点（约 L654/L674/L686）。
  - 新命令 `update_camera_cogsocket_auth(id,user,password)`（仿 update_camera_ip，缺失项则新建）并注册 handler。
  - 版本号 → 26.9.20。
- `src/index.html`：在 body 末尾加单元格设置模态框 `#cogsocket-dialog`（连接区/工具栏/表格/手动行/日志）。
- `src/main.js`：import 新模块；cogsocket 模式下每格头部注入"单元格"按钮；`initCogSocketCells()` 绑定一次。
- `src/styles.css`：模态框、连接状态栏、单元格表格（sticky 表头）、日志区样式（适配深/浅主题变量）。
- `src/i18n.js`：约 25 个新键中英两套。
- `src-tauri/Cargo.toml`、`src-tauri/Cargo.lock`、`src-tauri/tauri.conf.json`：26.9.20。
- 文档：`TODO.md`（#7 划线完成）、`CHANGELOG.md`（26.9.20 小时条目）、`README.md`（版本/功能/版本记录）、`PLAN.md`（P1 组注明 #7 完成、传输层落地）。

## Implementation Steps

1. **Rust**：CamConfigItem 两字段 + Default/构造点 + `update_camera_cogsocket_auth` 命令 + handler 注册 + 版本号三处 26.9.20。
2. **cogsocket_manager.js**：
   - `loadCogSocketClass()`：`fetch('assets/cogsocket/cogsocket.js')` 取源码，用 `new Function('module','exports','require','define', code+';return module.exports')` 注入伪 module 执行（SDK 内 `define(...)` 抛错后走 catch 的 Node 分支），返回 CogSocket 类，规避与 Monaco AMD 冲突。
   - `parseTarget(urlInput)`：从相机格 URL（`192.168.0.1` / `192.168.0.1:8087` / `http://host:port/...`）解析 host/port，默认端口 80。
   - `class CogConnection`：
     - `connect()`：开 `ws://host:port/ws` → onopen → `post('@/hello',{name:'CameraViewerTauri',model:'Browser'})` → 试 `get('cam0/hmi/info')`，失败回退根 `system` → `post(root+'/openSession',{$type:'HmiSessionInfo',cellNames:['A0:Z599'],enableQueuedResults:true,includeCustomView:true})` 得 sessionId（`hs/~...`）→ `post(sid+'/login',[user,pwd,false])` 校验访问级别非 locked → `addListener(sid+'/resultChanged', cb)` 并在回调内**立即** `post(sid+'/ready','')` 维持相机出帧（负载只缓存为 latestResult）→ 启动 15s keepAlive 定时器。
     - Promise 化 `get/put/post`（回调 Error→reject，错误码+body 拼中文消息）；超时 10s（连接 8s）。
     - 业务方法：`getAllCellNames()`、`getLatestResult()`、`setCell(name,value)`（post sid/setCellValue [name,value]）、`setCells(map)`（post sid/setCellValues [map]，注意数组包一层）、`setSoftOnline(bool)`（put sid/softOnline）、`manualTrigger()`、`getJobName()`、`getState()`。
     - `disconnect()`：removeListener、post dispose（尽力）、clearInterval、close ws；状态机 closed/connecting/ready/error + onLog 回调。
3. **cogsocket_cells.js**：
   - 每相机一个连接实例缓存（Map），弹窗单例；`openForCamera(index, item)` 填充目标、凭据（item.cogsocket_user||'admin'）。
   - 连接栏：host:port 只读预览、用户名/密码（保存凭据按钮调 update_camera_cogsocket_auth）、连接/断开、状态灯、job 名、在线状态。
   - 工具栏：刷新单元格（getLatestResult→过滤 `editable===true`）、离线编辑/恢复在线（softOnline，关窗时若被本弹窗置离线则自动恢复，仿 FrmGrid）、手动触发、批量写入。
   - 单元格表：location / 名称 / 类型 / 当前值（按类型给 number/checkbox/text 输入，显示 min/max/maxLength 约束）/ 单行"写入"；写入后延迟 ~600ms 拉 getLatestResult 回读该行显示 ✓/✗。
   - 手动写入行：名称（可填地址如 A0 或命名单元格）+ 值（先 JSON.parse，失败按原始字符串）→ setCell；支持 EditRegion 等复杂对象 JSON。
   - 按钮类单元格显示"执行"（setCell(name,true)，带二次确认）。
   - 日志区：时间戳 + 请求/响应/错误分级着色；语言切换监听 app-language-changed 重渲染静态文案。
4. **main.js 接线**：connection_mode==='cogsocket' 时 createCameraCell 头部追加单元格按钮（其它模式不渲染）；弹窗事件在 setupEventListeners 内 init 一次；切换语言/主题无需特殊处理（CSS 变量）。
5. **i18n/styles/svg/index.html**：弹窗全部文案中英双语；样式复用现有按钮/表格类风格；图标 16x16。
6. **相机设置标签页**更新提示文案：CogSocket 已支持连接与会话/单元格设置，取图显示后续版本。
7. **验证**（见下）。
8. **文档**：TODO/CHANGELOG/README/PLAN。

## Dependencies and Considerations
- 纯前端 WebSocket，不引入 Rust ws 依赖（文档 18.1 既定方案）；凭据存 CameraConfig.json 为明文（与参考软件一致，在 README/CHANGELOG 注明工控可信内网用途）。
- 根路径兼容：先 cam0/hmi 后 system；hello 必须在 openSession 前；每收到 resultChanged 必须 ready，否则相机停帧。
- 相机 HMI 连接数上限（22.x 固定 5）：弹窗关闭即 dispose；断 ws 也会让服务端自动释放会话。
- setCellValue 所需权限为 IS.CFGJOB/在线可写 HMI 编辑单元格；需要改作业结构（setCellExpression/setCellName）不在本次 UI 范围。
- 无真机环境：协议时序用本地 mock 验证，真机差异在日志区暴露原始 error body 便于排查。

## Validation
- `cargo check`（lib.rs 改动后）0 错误。
- `node --check`：cogsocket_manager.js / cogsocket_cells.js / main.js / i18n.js。
- **Node mock 相机协议 harness**（临时脚本，验证后删除）：用伪 WebSocket（记录 send 的 JSON 帧、按脚本注入 resp/event）+ 直接 require 仓库内 cogsocket.js，驱动 CogConnection 全流程，断言帧序列：ws open → @/hello → get cam0/hmi/info → openSession(cellNames A0:Z599) → login[admin,,false] → listen resultChanged；模拟一帧含 2 个 editable 单元格（EditInt 带 min/max、EditString）的 resultChanged 事件 → 断言 manager 回发 ready；执行 setCell('MyEditInt',10) 断言发出 post sid/setCellValue 参数为 ["MyEditInt",10]；批量写入断言 body 为 `[{...}]`；模拟 error resp 断言 Promise reject 消息含错误码。
- HTML id 唯一性/引用检查（沿用上次的临时检查脚本方式）。
- 真机联调（用户现场执行）：连接 6.x/22.x 各一台更佳；本次交付不声称已通过硬件验证。

## Risks
- **无硬件导致协议字段假设偏差**（如 getLatestResult 路径/负载在旧固件差异）：所有调用路径集中在 manager 一层，日志区打印原始报文，真机微调只改一个文件；根路径/会话参数均按文档+参考软件双重印证。
- **Monaco AMD 冲突**：用 Function 沙箱加载 SDK，完全不碰全局 define/require；harness 覆盖加载函数逻辑。
- **写入错误值导致相机作业异常**：UI 提供 min/max 约束提示、按钮二次确认、写后回读；默认不提供 setCellExpression（不改公式）。
- **离线状态遗留**：弹窗置离线后关闭/断线自动恢复 softOnline=true；异常退出时下次连接也会显式 put true。
