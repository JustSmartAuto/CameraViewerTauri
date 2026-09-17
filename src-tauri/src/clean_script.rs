//! 图片清理高级版：ECMAScript 脚本沙箱。
//!
//! 使用 `boa_engine`（纯 Rust ECMAScript 引擎，crates.io）做脚本执行，
//! 通过 IIFE + 对象返回桥接取回 `evaluate(context)` 的判定结果和 console 输出。
//!
//! 原计划使用 `qjs_runtime`（Lewin671/quickjs-rust），因 GitHub 网络不可达
//! 改用 boa_engine 作为 fallback（crates.io 纯 Rust，无 C 依赖，Windows MSVC 零摩擦）。
//!
//! context 字段与 ImageCleanerAutoWeld 的 `delete_conditions.js` 完全对齐
//! （驼峰命名），老用户的脚本可直接粘贴复用。

use serde::{Deserialize, Serialize};
use tauri::State;
use std::sync::Arc;

use crate::AppState;

// ==================== 数据模型 ====================

/// 传给用户脚本的 context，字段名按 ImageCleanerAutoWeld 约定使用驼峰。
/// `#[serde(rename_all = "camelCase")]` 让 JSON 输出为驼峰，匹配旧脚本。
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CleanScriptContext {
    /// 当前 UTC 毫秒时间戳
    pub now_ms: f64,
    /// 监控路径
    pub path: String,
    /// 清理模式："Image" 或 "Folder"（本项目固定 "Image"）
    pub cleanup_mode: String,
    /// 保存时间阈值（秒）
    pub storage_time_seconds: f64,
    /// 已过期目标数（创建时间早于阈值）
    pub expired_count: i64,
    /// 当前目标总数
    pub image_count: i64,
    /// 是否启用数量条件
    pub image_count_enabled: bool,
    /// 数量阈值
    pub image_count_threshold: i64,
    /// 磁盘剩余空间 GB
    pub free_space_gb: f64,
    /// 是否启用磁盘条件
    pub disk_space_enabled: bool,
    /// 磁盘余量阈值 GB
    pub disk_space_threshold_gb: f64,
}

/// 脚本执行结果，序列化回前端用于展示。
#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct ScriptResult {
    /// `evaluate()` 返回的布尔判定。None 表示脚本出错或返回非布尔值。
    pub decision: Option<bool>,
    /// 脚本中 console.log/warn/error/info 收集的日志。
    pub console: Vec<String>,
    /// 脚本执行出错时的错误信息（解析/运行时错误）。
    #[serde(default)]
    pub error: Option<String>,
}

/// 默认脚本：与 ImageCleanerAutoWeld 的 `delete_conditions.js` 内置 AND 逻辑一致。
/// 老用户粘贴自定义脚本前可用此恢复默认行为。
pub const DEFAULT_SCRIPT: &str = r#"// ImageCleaner 删除判定脚本（boa_engine / ECMAScript）
//
// 约定：定义 evaluate(context) 函数，返回 true 表示允许继续删除最旧目标，false 表示停止。
// context 字段：
//   nowMs                当前 UTC 毫秒
//   path                 监控路径
//   cleanupMode          "Image" 或 "Folder"
//   storageTimeSeconds   保存时间阈值（秒）
//   expiredCount         已过期目标数（创建时间早于阈值）
//   imageCount           当前目标总数
//   imageCountEnabled    是否启用数量条件
//   imageCountThreshold  数量阈值
//   freeSpaceGb          磁盘剩余空间 GB
//   diskSpaceEnabled     是否启用磁盘条件
//   diskSpaceThresholdGb 磁盘余量阈值 GB
//
// 默认逻辑（与未自定义时一致）：
function evaluate(c) {
    var ageMet = c.expiredCount > 0;
    var countMet = !c.imageCountEnabled || c.imageCount > c.imageCountThreshold;
    var diskMet = !c.diskSpaceEnabled || c.freeSpaceGb < c.diskSpaceThresholdGb;
    return ageMet && countMet && diskMet;
}
"#;

// ==================== 核心评估 ====================

/// 拼装 IIFE 脚本：console 垫片 + 用户脚本 + evaluate(ctx) 调用 + 对象返回。
fn build_iife(user_script: &str, ctx_json: &str) -> String {
    // console 垫片把 log/warn/error/info 全收集到 __logs 数组一并返回。
    // IIFE 末尾 return 对象 { decision, console }，Rust 端通过对象属性访问取回。
    format!(
        "(function(){{\
           var __logs = [];\
           var console = {{\
             log:   function(){{ __logs.push(Array.prototype.join.call(arguments, ' ')); }},\
             info:  function(){{ __logs.push(Array.prototype.join.call(arguments, ' ')); }},\
             warn:  function(){{ __logs.push('[warn] '  + Array.prototype.join.call(arguments, ' ')); }},\
             error: function(){{ __logs.push('[error] ' + Array.prototype.join.call(arguments, ' ')); }}\
           }};\
           var __ctx = {ctx};\
           {user}\
           var __decision = evaluate(__ctx);\
           return {{ decision: __decision, console: __logs }};\
         }})()",
        ctx = ctx_json,
        user = user_script,
    )
}

/// 在 boa_engine 沙箱中执行用户脚本并取回判定结果。
///
/// 流程：拼装 IIFE → `Context::eval` → 通过对象属性访问取回 decision 和 console。
/// 任何环节失败都返回带 `error` 字段的结果，不 panic。
pub fn evaluate_decision(user_script: &str, ctx: &CleanScriptContext) -> ScriptResult {
    let ctx_json = match serde_json::to_string(ctx) {
        Ok(s) => s,
        Err(e) => {
            return ScriptResult {
                decision: None,
                console: vec![],
                error: Some(format!("context 序列化失败: {e}")),
            };
        }
    };

    let iife = build_iife(user_script, &ctx_json);

    let mut context = boa_engine::Context::default();
    match context.eval(boa_engine::Source::from_bytes(&iife)) {
        Ok(value) => {
            // IIFE 末尾 return { decision, console }，期望拿到对象
            let Some(obj) = value.as_object() else {
                return ScriptResult {
                    decision: None,
                    console: vec![],
                    error: Some(format!(
                        "脚本未返回对象（得到 {value:?}）。请确保脚本定义了 evaluate(context)。"
                    )),
                };
            };

            // 取 decision 属性，仅接受布尔值
            let decision = obj
                .get(boa_engine::property::PropertyKey::from(boa_engine::js_string!("decision")), &mut context)
                .ok()
                .and_then(|v| v.as_boolean());

            // 取 console 属性，遍历数组元素转为字符串
            let mut console = Vec::new();
            if let Ok(console_val) = obj.get(boa_engine::property::PropertyKey::from(boa_engine::js_string!("console")), &mut context)
            {
                if let Some(arr) = console_val.as_object() {
                    if let Ok(length_val) =
                        arr.get(boa_engine::property::PropertyKey::from(boa_engine::js_string!("length")), &mut context)
                    {
                        let length = length_val
                            .as_number()
                            .map(|n| n as usize)
                            .unwrap_or(0);
                        for i in 0..length {
                            if let Ok(elem) =
                                arr.get(boa_engine::property::PropertyKey::from(boa_engine::js_string!(i.to_string())), &mut context)
                            {
                                if let Ok(s) = elem.to_string(&mut context) {
                                    console.push(s.to_std_string_escaped());
                                }
                            }
                        }
                    }
                }
            }

            ScriptResult {
                decision,
                console,
                error: None,
            }
        }
        Err(e) => ScriptResult {
            decision: None,
            console: vec![],
            error: Some(format!("{e}")),
        },
    }
}

// ==================== Tauri 命令 ====================

/// 测试用户脚本：用提供的 context 执行脚本，返回判定结果 + console 输出 + 错误。
/// 前端 Monaco 编辑器旁的"测试"按钮调用。
#[tauri::command]
pub fn test_clean_script(script: String, ctx: CleanScriptContext) -> ScriptResult {
    evaluate_decision(&script, &ctx)
}

/// 返回默认脚本内容，供前端"载入默认"按钮使用。
#[tauri::command]
pub fn get_default_clean_script() -> String {
    DEFAULT_SCRIPT.to_string()
}

/// 返回一个示例 context，供前端测试脚本时填入。
#[tauri::command]
pub fn sample_clean_context(state: State<Arc<AppState>>) -> CleanScriptContext {
    let cfg = state.clean_config.lock().unwrap().clone();
    // 用当前清理配置构造一个合理的示例 context
    let now_ms = chrono::Local::now().timestamp_millis() as f64;
    let storage_time_seconds = (cfg.hold_days as f64) * 86400.0;
    let path_root = std::path::Path::new(&cfg.folder_path)
        .components()
        .next()
        .map(|c| c.as_os_str().to_string_lossy().to_string())
        .unwrap_or_default();
    let free_space_gb = crate::get_disk_space(&path_root)
        .map(|(_, free)| free)
        .unwrap_or(0.0);

    CleanScriptContext {
        now_ms,
        path: cfg.folder_path.clone(),
        cleanup_mode: "Image".to_string(),
        storage_time_seconds,
        expired_count: 1,
        image_count: 100,
        image_count_enabled: cfg.is_enable_image_count,
        image_count_threshold: cfg.image_count_threshold as i64,
        free_space_gb,
        disk_space_enabled: cfg.is_enable_remaining_space,
        disk_space_threshold_gb: cfg.remaining_space,
    }
}
