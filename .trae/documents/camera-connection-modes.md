# 计划：相机连接模式（http/cogsocket/gige）持久化 + 相机设置标签页（TODO #25）

## Context

TODO #25 要求实现三种相机连接模式（http、cogsocket、gige）的选择与持久化，默认 http，新建"相机设置"标签页放置连接模式下拉框。

**范围说明**：本任务仅实现**配置持久化 + UI**。CogSocket / GigE 协议的实际通信实现属于 TODO #2（CogSocket 取图）、#12（GigE 相机支持）、#21（CogSocket 数据传输），不在本次范围。用户选择 cogsocket/gige 时，选择会被持久化并在下拉框回显；相机显示区域暂仍按 http 加载（协议未实现），同时在"相机设置"标签页显示提示说明。

## 现有可复用的模式

- **配置结构**：[lib.rs:248](src-tauri/src/lib.rs) `AppConfig` 当前含 `language`/`theme`，通过 `get_app_config`/`set_app_config` 命令读写（[lib.rs:788-793](src-tauri/src/lib.rs)），前端 `loadConfigs()` 调 `invoke("get_app_config")`、`saveAppConfig()` 调 `invoke("set_app_config", { config: appConfig })`（[main.js:63-78](src/main.js), [main.js:177-178](src/main.js)）
- **serde 序列化约定**：config 结构体字段默认 snake_case 序列化（参考 `CleanConfig` 的 `is_advanced`/`delete_script` 等字段在 JS 中以 `cleanConfig.is_advanced` 访问），新字段 `connection_mode` 在 JS 中以 `appConfig.connection_mode` 读写
- **下拉框 UI**：显示设置标签页已有 `<select>` 模式（[index.html:95](src/index.html) `cmbx-mirror-window`、[index.html:127](src/index.html) `cmbx-monitor`），含 `<label>` + `<select>` + 可选刷新按钮
- **标签页结构**：`<button class="tab-btn" data-tab="tab-xxx">` + `<div id="tab-xxx" class="tab-panel">`（[index.html:58-70](src/index.html), [index.html:90-155](src/index.html)），面板内用 `<div class="part-section"><h4>` + `<div class="form-row">` 组织
- **设置回显模式**：`updateAppSettingsUI()`（[main.js:159-167](src/main.js)）打开设置时同步选中态；新增类似的 `connection_mode` 回显

## 改动清单

### 1. Rust：AppConfig 新增字段（[src-tauri/src/lib.rs](src-tauri/src/lib.rs) ~L248）

`AppConfig` 结构体新增字段，`#[serde(default)]` 保证旧配置文件无此字段时反序列化不报错：

```rust
pub struct AppConfig {
    pub language: String,
    pub theme: String,
    #[serde(default = "default_connection_mode")]
    pub connection_mode: String,
}

fn default_connection_mode() -> String { "http".to_string() }
```

`impl Default` 同步追加 `connection_mode: "http".to_string()`。无需新增 Tauri 命令——`get_app_config`/`set_app_config` 已处理整个结构体。

### 2. 前端 HTML：新增"相机设置"标签页（[src/index.html](src/index.html)）

- 在 `tabs-row` 中 `tab-camera`（相机显示）按钮后追加：
  `<button class="tab-btn" data-tab="tab-camera-settings" data-i18n="cameraSettings">相机设置</button>`
- 在 `tab-content` 中 `#tab-camera` 面板后追加新面板 `#tab-camera-settings`，含一个 `part-section`：
  - `<h4 data-i18n="connectionMode">连接模式</h4>`
  - `<select id="cmbx-connection-mode">` 含三个 `<option>`（http/cogsocket/gige，均带 `data-i18n` 文案）
  - 提示文本 `<span data-i18n="connectionModeNote">` 说明 cogsocket/gige 待后续版本支持

### 3. 前端 JS：配置读写与事件绑定（[src/main.js](src/main.js)）

- `loadConfigs()` 中 `get_app_config` 后，回显下拉框：`document.getElementById("cmbx-connection-mode").value = appConfig.connection_mode || "http"`
- 事件绑定函数（与现有 `addEventListener` 块同处）追加：下拉框 `change` → `appConfig.connection_mode = value` + `saveAppConfig()`
- 打开设置弹窗时（`openSetting` 流程末尾或 `updateAppSettingsUI` 旁）同步回显选中态

### 4. i18n（[src/i18n.js](src/i18n.js)）

新增 6 个键，中英两套：`cameraSettings`、`connectionMode`、`modeHttp`、`modeCogsocket`、`modeGige`、`connectionModeNote`

### 5. 文档：CHANGELOG.md + TODO.md

- TODO.md #25 标记完成（删除线）
- CHANGELOG.md 在 26.9.18 版本段追加条目（时间取文件修改时间）

## 验证

1. `cargo check` 通过（0 错误）
2. `node --check src/main.js` + `node --check src/i18n.js` 语法通过
3. 手动验证（需打包后）：
   - 打开"相机设置"标签页，下拉框默认显示"http"
   - 切换到"gige"，重启软件后仍为"gige"（持久化生效）
   - 切换语言后下拉框选项文案正确切换
   - 切到 cogsocket/gige 后相机显示区仍按 http 加载（符合"协议未实现"预期）
