# 图片清理高级版实现计划

## Context

项目 TODO #30 要求在"图片清理"标签页添加"高级版"开关，复刻 ImageCleanerAutoWeld 项目的核心功能——用户可编写 JS 脚本（`evaluate(context)` 函数）自定义删除判定逻辑。使用 `quickjs-rust`（用户指定的 GitHub 仓库，纯 Rust ECMAScript 引擎）做后端脚本沙箱，`monaco-editor`（已下载到 `src/assets/monaco-editor/`）做前端代码编辑器。

## 依赖选择

**采用** `qjs_runtime` git 依赖（`qjs_runtime = { git = "https://github.com/Lewin671/quickjs-rust" }`）：
- 纯 Rust，无 C 依赖，Windows MSVC 零摩擦
- 公开 API 仅 `eval()`，采用 IIFE + `JSON.stringify` 桥接模式绕开无持久 Context 的限制
- 若 git 依赖构建受阻，备选 `boa_engine`（crates.io，纯 Rust）

## 实施步骤

### 1. Cargo.toml + 版本号
- `src-tauri/Cargo.toml`：加 `qjs_runtime` git 依赖，版本升 `26.9.18`
- `src-tauri/tauri.conf.json`：版本同步

### 2. 新模块 `src-tauri/src/clean_script.rs`
- `CleanScriptContext` 结构体（11 字段，与 ImageCleanerAutoWeld 的 context 完全对齐：nowMs/path/cleanupMode/storageTimeSeconds/expiredCount/imageCount/imageCountEnabled/imageCountThreshold/freeSpaceGb/diskSpaceEnabled/diskSpaceThresholdGb）
- `evaluate_decision(script, ctx)` 函数：拼装 IIFE（console 垫片 + 用户脚本 + `evaluate(ctx)` 调用 + `JSON.stringify({decision, console})`），单次 `qjs_runtime::eval()`，解析返回的 JSON 字符串
- `DEFAULT_SCRIPT` 常量：与 `delete_conditions.js` 默认 AND 逻辑一致
- 3 个 Tauri 命令：`test_clean_script`、`get_default_clean_script`、`sample_clean_context`

### 3. lib.rs 接线
- `mod clean_script;`
- `CleanConfig` 加字段：`is_advanced: bool`、`delete_script: String`、`is_enable_image_count: bool`、`image_count_threshold: i32`（全加 `#[serde(default)]`）
- `clean_once` 改造：`is_advanced=true` 走脚本/AND 判定；`is_advanced=false` 保留旧 OR 行为（零回归）
- `generate_handler!` 注册 3 个新命令

### 4. 前端 index.html
- `tab-clean` 面板内（`btn-clean-apply` 之前）加：高级版复选框 + 折叠容器（Monaco 容器 div + 载入默认/测试按钮 + 结果输出区）
- `<head>` 加 `<script src="assets/monaco-editor/vs/loader.js"></script>`

### 5. 前端 main.js
- `initMonaco()`：AMD `require` 加载 `vs/editor/editor.main`，注册暗黑主题
- 高级版开关 change 事件：按需初始化 Monaco 编辑器
- 测试按钮：调 `test_clean_script` 展示判定结果 + console 输出 + 错误
- 载入默认按钮：调 `get_default_clean_script`
- `applyCleanConfig` 追加 `is_advanced` + `delete_script` 字段读写
- 表单回显追加高级版开关状态 + 编辑器内容填充
- 主题切换联动 Monaco 主题

### 6. styles.css
- `.clean-advanced-section`、`.clean-editor-wrap`（260px 高）、`.clean-test-result`、`.btn-secondary`

### 7. i18n.js
- ~12 个新键（cleanAdvanced/deleteScript/loadDefault/test/decisionDelete/decisionKeep/decisionNone/imageCount/enableImageCount/cleanAdvancedHint/cleanScriptDocs/cleanScriptError）

### 8. 文档
- CHANGELOG.md 追加条目
- TODO.md 第 30 条划掉

## 关键设计决策

1. **IIFE + JSON 桥接**：`qjs_runtime` 无持久 Context API，每次 eval 拼装完整脚本（console 垫片 + 用户脚本 + evaluate 调用），返回 JSON 字符串在 Rust 端解析
2. **context 字段驼峰**：与 ImageCleanerAutoWeld 完全对齐，老用户的 `delete_conditions.js` 可直接粘贴
3. **不读外部文件**：脚本存配置 `delete_script` 字段，随 CleanConfig.json 持久化（省略外部文件优先级，避免双源歧义）
4. **向后兼容**：`is_advanced=false` 保留旧 OR 行为，零回归
5. **Monaco 按需加载**：仅在用户勾选高级版时 `require` 加载

## 验证
- `cargo check`（src-tauri，验证 qjs_runtime git 依赖编译通过）
- `node --check src/main.js`
- Monaco 编辑器渲染测试
- `test_clean_script` 命令功能测试（默认脚本 + 自定义脚本 + 错误脚本）
- 旧 CleanConfig.json 向后兼容（新字段缺省值）

## 修改文件清单
- `src-tauri/Cargo.toml`
- `src-tauri/tauri.conf.json`
- `src-tauri/src/clean_script.rs`（新增）
- `src-tauri/src/lib.rs`
- `src/index.html`
- `src/main.js`
- `src/styles.css`
- `src/i18n.js`
- `CHANGELOG.md`
- `TODO.md`
