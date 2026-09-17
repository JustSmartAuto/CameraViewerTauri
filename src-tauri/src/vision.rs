//! 轻度视觉检测（TODO #13）：boa_engine ECMAScript 脚本沙箱 + ROI 灰度统计。
//!
//! 三栏工作台（前端）：
//! - 左栏：Monaco 编辑用户脚本，脚本需定义 `inspect(context)` 并返回
//!   `{ pass, message, overlays, metrics }`；
//! - 中栏：声明式数据驱动的 ROI 控件（region/circle/point/line/polygon），
//!   控件数据即脚本 context，画布与属性表双向绑定；
//! - 右栏：原图/效果图（overlays 由脚本声明、前端绘制）+ 控制台日志。
//!
//! 后端职责：
//! 1. 持久化 `VisionConfig`（脚本文本、图片路径、控件列表）；
//! 2. 读取本地图片为 data URL（同源 data: 协议，前端 canvas 不受跨域污染）；
//! 3. 用 `image` crate 按控件几何计算 ROI 灰度统计（mean/stdDev/min/max/count），
//!    连同图像/控件信息序列化为 context 注入 boa 沙箱；
//! 4. IIFE + JSON.stringify 桥接取回脚本结果；脚本放工作线程执行并加 5s 超时
//!    （boa_engine 0.22 无指令级中断钩子，超时后丢弃结果，工作线程自行结束）。

use std::collections::HashSet;
use std::fs;
use std::sync::{mpsc, Arc};
use std::time::Duration;

use image::RgbImage;
use serde::{Deserialize, Serialize};
use tauri::State;

use crate::AppState;

// ==================== 数据模型 ====================

/// ROI 控件（声明式数据驱动，字段即画布/属性表/脚本 context 的唯一数据源）。
/// 几何字段按控件类型取用，未使用字段保留默认值并在脚本 context 中省略。
#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct VisionControl {
    pub id: String,
    pub name: String,
    /// region（矩形）/ circle（圆）/ point（点）/ line（线段）/ polygon（多边形）
    #[serde(rename = "type", default = "default_control_type")]
    pub kind: String,
    #[serde(default)]
    pub x: f64,
    #[serde(default)]
    pub y: f64,
    #[serde(default)]
    pub w: f64,
    #[serde(default)]
    pub h: f64,
    #[serde(default)]
    pub cx: f64,
    #[serde(default)]
    pub cy: f64,
    #[serde(default)]
    pub r: f64,
    #[serde(default)]
    pub x1: f64,
    #[serde(default)]
    pub y1: f64,
    #[serde(default)]
    pub x2: f64,
    #[serde(default)]
    pub y2: f64,
    #[serde(default)]
    pub points: Vec<[f64; 2]>,
}

fn default_control_type() -> String {
    "region".to_string()
}

/// 视觉检测持久化配置（VisionConfig.json）。
#[derive(Clone, Debug, Default, Serialize, Deserialize)]
pub struct VisionConfig {
    #[serde(default)]
    pub script: String,
    #[serde(default)]
    pub image_path: String,
    #[serde(default)]
    pub controls: Vec<VisionControl>,
}

/// 图片读取结果：data URL 供前端直接绘制，宽高供脚本 context。
#[derive(Clone, Debug, Serialize)]
pub struct VisionImageInfo {
    pub data_url: String,
    pub width: u32,
    pub height: u32,
    pub file_name: String,
}

/// ROI 灰度统计。无像素时各统计字段为 None（前端显示 N/A）。
#[derive(Clone, Debug, Serialize)]
struct RoiStat {
    count: u64,
    mean: Option<f64>,
    std_dev: Option<f64>,
    min: Option<f64>,
    max: Option<f64>,
}

/// 脚本执行结果。
#[derive(Clone, Debug, Serialize)]
pub struct VisionRunResult {
    /// 脚本返回的 pass 布尔（OK/NG）。None 表示未返回。
    pub pass: Option<bool>,
    #[serde(default)]
    pub message: String,
    /// 脚本声明的效果图叠加层（rect/circle/point/line/polygon/text），原样透传。
    #[serde(default)]
    pub overlays: Vec<serde_json::Value>,
    /// 脚本声明的指标键值，原样透传。
    #[serde(default)]
    pub metrics: Vec<serde_json::Value>,
    #[serde(default)]
    pub console: Vec<String>,
    #[serde(default)]
    pub error: Option<String>,
}

// ==================== 默认脚本 ====================

/// 默认检测脚本：对所有 ROI 做灰度均值阈值判定（50~200），并回显 ROI 轮廓。
/// 用户可在此基础上编写自定义判定；context 结构见脚本注释。
pub const DEFAULT_SCRIPT: &str = r#"// 轻度视觉检测脚本（boa_engine / ECMAScript）
//
// 约定：定义 inspect(context)，返回 { pass, message, overlays, metrics }。
// context 结构：
//   context.nowMs                 当前 UTC 毫秒
//   context.image.fileName        图片文件名
//   context.image.width/height    图片宽高（像素）
//   context.controls[]            ROI 控件数组：
//     .id / .name / .type         region | circle | point | line | polygon
//     矩形:   .x .y .w .h
//     圆:     .cx .cy .r
//     点:     .x .y
//     线段:   .x1 .y1 .x2 .y2
//     多边形: .points = [[x,y], ...]
//     .stats  { count, mean, stdDev, min, max }  // 区域内灰度统计（0-255）
//
// overlays 支持：
//   {type:'rect', x,y,w,h, color?, label?}
//   {type:'circle', cx,cy,r, color?, label?}
//   {type:'point', x,y, color?, label?}
//   {type:'line', x1,y1,x2,y2, color?, label?}
//   {type:'polygon', points:[[x,y],...], color?, label?}
//   {type:'text', x,y,text, color?}
// 颜色省略时由前端按 OK/NG 自动着色。

var MEAN_MIN = 50;
var MEAN_MAX = 200;

function outlineOf(c) {
  if (c.type === 'region')  return { type:'rect', x:c.x, y:c.y, w:c.w, h:c.h, label:c.name };
  if (c.type === 'circle')  return { type:'circle', cx:c.cx, cy:c.cy, r:c.r, label:c.name };
  if (c.type === 'point')   return { type:'point', x:c.x, y:c.y, label:c.name };
  if (c.type === 'line')    return { type:'line', x1:c.x1, y1:c.y1, x2:c.x2, y2:c.y2, label:c.name };
  if (c.type === 'polygon') return { type:'polygon', points:c.points, label:c.name };
  return null;
}

function inspect(ctx) {
  var overlays = [];
  var metrics = [];
  var allOk = true;
  var checked = 0;

  ctx.controls.forEach(function (c) {
    var o = outlineOf(c);
    if (o) overlays.push(o);
    if (!c.stats || c.stats.count === 0) {
      console.log('[warn] 控件 ' + c.name + ' 区域内无像素，已跳过');
      return;
    }
    checked++;
    metrics.push({ name: c.name + '.mean', value: c.stats.mean });
    metrics.push({ name: c.name + '.stdDev', value: c.stats.stdDev });
    var ok = c.stats.mean >= MEAN_MIN && c.stats.mean <= MEAN_MAX;
    if (o) o.color = ok ? '#22c55e' : '#ef4444';
    if (!ok) {
      allOk = false;
      console.log('[NG] ' + c.name + ' 灰度均值 ' + c.stats.mean.toFixed(2) + ' 超出阈值');
    }
  });

  if (checked === 0) {
    return { pass: null, message: '无有效 ROI', overlays: overlays, metrics: metrics };
  }
  return {
    pass: allOk,
    message: allOk ? 'OK' : 'NG',
    overlays: overlays,
    metrics: metrics
  };
}
"#;

// ==================== 脚本桥接 ====================

/// 拼装 IIFE：console 垫片 + 用户脚本 + inspect(ctx) + JSON.stringify 返回。
/// 用 JSON 字符串桥接，避免在 Rust 侧逐项遍历 boa 对象。
fn build_iife(user_script: &str, ctx_json: &str) -> String {
    format!(
        "(function() {{\n\
           var __logs = [];\n\
           var console = {{\n\
             log:   function() {{ __logs.push(Array.prototype.join.call(arguments, ' ')); }},\n\
             info:  function() {{ __logs.push(Array.prototype.join.call(arguments, ' ')); }},\n\
             warn:  function() {{ __logs.push('[warn] '  + Array.prototype.join.call(arguments, ' ')); }},\n\
             error: function() {{ __logs.push('[error] ' + Array.prototype.join.call(arguments, ' ')); }}\n\
           }};\n\
           var __ctx = {ctx};\n\
           {user}\n\
           if (typeof inspect !== 'function') {{\n\
             throw new Error('脚本必须定义 inspect(context) 函数');\n\
           }}\n\
           var __r = inspect(__ctx);\n\
           if (__r === null || typeof __r !== 'object') {{\n\
             throw new Error('inspect(context) 必须返回对象 {{ pass, message, overlays, metrics }}');\n\
           }}\n\
           __r.console = __logs;\n\
           return JSON.stringify(__r);\n\
         }})()",
        ctx = ctx_json,
        user = user_script
    )
}

/// 在独立工作线程执行脚本，5 秒超时保护（boa 0.22 无指令中断钩子）。
fn eval_script_timeout(user_script: &str, ctx_json: &str) -> Result<String, String> {
    let iife = build_iife(user_script, ctx_json);
    let (tx, rx) = mpsc::channel();
    // boa Context 非 Send，但在线程内创建并随线程销毁，无需跨线程传递。
    std::thread::spawn(move || {
        let mut context = boa_engine::Context::default();
        let out = context
            .eval(boa_engine::Source::from_bytes(iife.as_str()))
            .map_err(|e| format!("{e}"))
            .and_then(|v| {
                v.to_string(&mut context)
                    .map(|s| s.to_std_string_escaped())
                    .map_err(|e| format!("{e}"))
            });
        let _ = tx.send(out);
    });
    match rx.recv_timeout(Duration::from_secs(5)) {
        Ok(res) => res,
        Err(mpsc::RecvTimeoutError::Timeout) => {
            Err("脚本执行超时（>5s），请检查是否存在死循环".to_string())
        }
        Err(mpsc::RecvTimeoutError::Disconnected) => {
            Err("脚本执行线程异常退出".to_string())
        }
    }
}

// ==================== ROI 灰度统计 ====================

#[inline]
fn gray(px: &image::Rgb<u8>) -> f64 {
    0.299 * px.0[0] as f64 + 0.587 * px.0[1] as f64 + 0.114 * px.0[2] as f64
}

fn round2(v: f64) -> f64 {
    (v * 100.0).round() / 100.0
}

fn stat_of(img: &RgbImage, pixels: impl Iterator<Item = (i32, i32)>) -> RoiStat {
    let (w, h) = (img.width() as i32, img.height() as i32);
    let mut seen: HashSet<(i32, i32)> = HashSet::new();
    let mut values: Vec<f64> = Vec::new();
    for (x, y) in pixels {
        if x < 0 || y < 0 || x >= w || y >= h {
            continue;
        }
        if !seen.insert((x, y)) {
            continue;
        }
        values.push(gray(img.get_pixel(x as u32, y as u32)));
    }
    let count = values.len() as u64;
    if values.is_empty() {
        return RoiStat {
            count: 0,
            mean: None,
            std_dev: None,
            min: None,
            max: None,
        };
    }
    let min = values.iter().copied().fold(f64::INFINITY, f64::min);
    let max = values.iter().copied().fold(f64::NEG_INFINITY, f64::max);
    let mean = values.iter().sum::<f64>() / values.len() as f64;
    let var = values
        .iter()
        .map(|v| {
            let d = v - mean;
            d * d
        })
        .sum::<f64>()
        / values.len() as f64;
    RoiStat {
        count,
        mean: Some(round2(mean)),
        std_dev: Some(round2(var.sqrt())),
        min: Some(round2(min)),
        max: Some(round2(max)),
    }
}

/// 点是否在多边形内（射线法）。
fn point_in_polygon(px: f64, py: f64, pts: &[[f64; 2]]) -> bool {
    let n = pts.len();
    if n < 3 {
        return false;
    }
    let mut inside = false;
    let mut j = n - 1;
    for i in 0..n {
        let (xi, yi) = (pts[i][0], pts[i][1]);
        let (xj, yj) = (pts[j][0], pts[j][1]);
        let intersect = (yi > py) != (yj > py)
            && px < (xj - xi) * (py - yi) / (yj - yi + f64::EPSILON) + xi;
        if intersect {
            inside = !inside;
        }
        j = i;
    }
    inside
}

/// 点到线段的距离（保留：后续 ROI 命中测试/粗线段统计可复用）。
#[allow(dead_code)]
fn dist_to_segment(px: f64, py: f64, x1: f64, y1: f64, x2: f64, y2: f64) -> f64 {
    let dx = x2 - x1;
    let dy = y2 - y1;
    let len2 = dx * dx + dy * dy;
    let t = if len2 == 0.0 {
        0.0
    } else {
        (((px - x1) * dx + (py - y1) * dy) / len2).clamp(0.0, 1.0)
    };
    let cx = x1 + t * dx;
    let cy = y1 + t * dy;
    ((px - cx).powi(2) + (py - cy).powi(2)).sqrt()
}

fn compute_stat(img: &RgbImage, c: &VisionControl) -> RoiStat {
    let (iw, ih) = (img.width() as f64, img.height() as f64);
    match c.kind.as_str() {
        "region" => {
            let x0 = c.x.max(0.0).floor() as i32;
            let y0 = c.y.max(0.0).floor() as i32;
            let x1 = ((c.x + c.w).min(iw)).ceil() as i32;
            let y1 = ((c.y + c.h).min(ih)).ceil() as i32;
            stat_of(img, (y0..y1).flat_map(move |y| (x0..x1).map(move |x| (x, y))))
        }
        "circle" => {
            let x0 = (c.cx - c.r).max(0.0).floor() as i32;
            let y0 = (c.cy - c.r).max(0.0).floor() as i32;
            let x1 = (c.cx + c.r + 1.0).min(iw).ceil() as i32;
            let y1 = (c.cy + c.r + 1.0).min(ih).ceil() as i32;
            let (cx, cy, r) = (c.cx, c.cy, c.r);
            stat_of(img, (y0..y1).flat_map(move |y| {
                (x0..x1).filter_map(move |x| {
                    let dx = x as f64 + 0.5 - cx;
                    let dy = y as f64 + 0.5 - cy;
                    (dx * dx + dy * dy <= r * r).then_some((x, y))
                })
            }))
        }
        "point" => {
            let (x, y) = (c.x.round() as i32, c.y.round() as i32);
            stat_of(img, std::iter::once((x, y)))
        }
        "line" => {
            // 沿线段按 0.5 像素步长采样，去重后统计；宽度约 1 像素。
            let dx = c.x2 - c.x1;
            let dy = c.y2 - c.y1;
            let steps = (dx.abs().max(dy.abs()) * 2.0).ceil() as u32;
            let steps = steps.max(1);
            let (x1, y1) = (c.x1, c.y1);
            stat_of(
                img,
                (0..=steps).map(move |i| {
                    let t = i as f64 / steps as f64;
                    (
                        (x1 + dx * t).round() as i32,
                        (y1 + dy * t).round() as i32,
                    )
                }),
            )
        }
        "polygon" => {
            if c.points.len() < 3 {
                return stat_of(img, std::iter::empty());
            }
            let (mut minx, mut miny, mut maxx, mut maxy) =
                (f64::INFINITY, f64::INFINITY, f64::NEG_INFINITY, f64::NEG_INFINITY);
            for p in &c.points {
                minx = minx.min(p[0]);
                maxx = maxx.max(p[0]);
                miny = miny.min(p[1]);
                maxy = maxy.max(p[1]);
            }
            let x0 = minx.max(0.0).floor() as i32;
            let y0 = miny.max(0.0).floor() as i32;
            let x1 = (maxx + 1.0).min(iw).ceil() as i32;
            let y1 = (maxy + 1.0).min(ih).ceil() as i32;
            let pts = c.points.clone();
            stat_of(img, (y0..y1).flat_map(move |y| {
                let pts = pts.clone();
                (x0..x1).filter_map(move |x| {
                    point_in_polygon(x as f64 + 0.5, y as f64 + 0.5, &pts).then_some((x, y))
                })
            }))
        }
        _ => stat_of(img, std::iter::empty()),
    }
}

/// 控件几何 + 统计序列化为脚本 context 中的一个元素。
fn control_to_json(c: &VisionControl, img: &RgbImage) -> serde_json::Value {
    let stat = compute_stat(img, c);
    let mut obj = serde_json::json!({
        "id": c.id,
        "name": c.name,
        "type": c.kind,
    });
    let map = obj.as_object_mut().unwrap();
    match c.kind.as_str() {
        "region" => {
            map.insert("x".into(), serde_json::json!(c.x));
            map.insert("y".into(), serde_json::json!(c.y));
            map.insert("w".into(), serde_json::json!(c.w));
            map.insert("h".into(), serde_json::json!(c.h));
        }
        "circle" => {
            map.insert("cx".into(), serde_json::json!(c.cx));
            map.insert("cy".into(), serde_json::json!(c.cy));
            map.insert("r".into(), serde_json::json!(c.r));
        }
        "point" => {
            map.insert("x".into(), serde_json::json!(c.x));
            map.insert("y".into(), serde_json::json!(c.y));
        }
        "line" => {
            map.insert("x1".into(), serde_json::json!(c.x1));
            map.insert("y1".into(), serde_json::json!(c.y1));
            map.insert("x2".into(), serde_json::json!(c.x2));
            map.insert("y2".into(), serde_json::json!(c.y2));
        }
        "polygon" => {
            map.insert("points".into(), serde_json::json!(c.points));
        }
        _ => {}
    }
    map.insert("stats".into(), serde_json::to_value(&stat).unwrap_or(serde_json::Value::Null));
    obj
}

// ==================== Base64 ====================

const B64_TABLE: &[u8; 64] =
    b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

fn base64_encode(input: &[u8]) -> String {
    let mut out = String::with_capacity((input.len() + 2) / 3 * 4);
    for chunk in input.chunks(3) {
        let b0 = chunk[0] as u32;
        let b1 = *chunk.get(1).unwrap_or(&0) as u32;
        let b2 = *chunk.get(2).unwrap_or(&0) as u32;
        let triple = (b0 << 16) | (b1 << 8) | b2;
        out.push(B64_TABLE[((triple >> 18) & 0x3f) as usize] as char);
        out.push(B64_TABLE[((triple >> 12) & 0x3f) as usize] as char);
        if chunk.len() > 1 {
            out.push(B64_TABLE[((triple >> 6) & 0x3f) as usize] as char);
        } else {
            out.push('=');
        }
        if chunk.len() > 2 {
            out.push(B64_TABLE[(triple & 0x3f) as usize] as char);
        } else {
            out.push('=');
        }
    }
    out
}

fn mime_from_path(path: &str) -> &'static str {
    let lower = path.to_ascii_lowercase();
    if lower.ends_with(".png") {
        "image/png"
    } else if lower.ends_with(".bmp") {
        "image/bmp"
    } else {
        // jpg/jpeg 及其它情况默认 jpeg（image crate 仅启用 bmp/png/jpeg）
        "image/jpeg"
    }
}

// ==================== Tauri 命令 ====================

#[tauri::command]
pub fn get_vision_config(state: State<Arc<AppState>>) -> VisionConfig {
    state.vision_config.lock().unwrap().clone()
}

#[tauri::command]
pub fn set_vision_config(config: VisionConfig, state: State<Arc<AppState>>) {
    *state.vision_config.lock().unwrap() = config;
    state.save_vision_config();
}

#[tauri::command]
pub fn get_default_vision_script() -> String {
    DEFAULT_SCRIPT.to_string()
}

/// 读取本地图片：校验可解码后原样转 data URL（不重编码，避免损失/耗时）。
#[tauri::command]
pub fn load_vision_image(path: String) -> Result<VisionImageInfo, String> {
    let bytes = fs::read(&path).map_err(|e| format!("读取图片失败: {e}"))?;
    let img = image::load_from_memory(&bytes).map_err(|e| format!("解码图片失败: {e}"))?;
    let file_name = std::path::Path::new(&path)
        .file_name()
        .map(|n| n.to_string_lossy().to_string())
        .unwrap_or_default();
    Ok(VisionImageInfo {
        data_url: format!(
            "data:{};base64,{}",
            mime_from_path(&path),
            base64_encode(&bytes)
        ),
        width: img.width(),
        height: img.height(),
        file_name,
    })
}

/// 执行视觉检测：读图 → ROI 统计 → boa 执行 inspect(context) → 解析结果。
#[tauri::command]
pub fn run_vision_inspection(
    script: String,
    image_path: String,
    controls: Vec<VisionControl>,
) -> VisionRunResult {
    let fail = |msg: String| VisionRunResult {
        pass: None,
        message: String::new(),
        overlays: vec![],
        metrics: vec![],
        console: vec![],
        error: Some(msg),
    };

    if image_path.is_empty() {
        return fail("请先载入图片".to_string());
    }
    let img = match image::open(&image_path) {
        Ok(dyn_img) => dyn_img.to_rgb8(),
        Err(e) => return fail(format!("打开图片失败: {e}")),
    };

    let now_ms = chrono::Local::now().timestamp_millis();
    let file_name = std::path::Path::new(&image_path)
        .file_name()
        .map(|n| n.to_string_lossy().to_string())
        .unwrap_or_default();
    let controls_json: Vec<serde_json::Value> =
        controls.iter().map(|c| control_to_json(c, &img)).collect();
    let ctx_json = serde_json::json!({
        "nowMs": now_ms,
        "image": {
            "path": image_path,
            "fileName": file_name,
            "width": img.width(),
            "height": img.height(),
        },
        "controls": controls_json,
    })
    .to_string();

    let json_string = match eval_script_timeout(&script, &ctx_json) {
        Ok(s) => s,
        Err(e) => return fail(e),
    };

    let parsed: serde_json::Value = match serde_json::from_str(&json_string) {
        Ok(v) => v,
        Err(e) => return fail(format!("解析脚本返回值失败: {e}（返回: {json_string}）")),
    };

    VisionRunResult {
        pass: parsed.get("pass").and_then(|v| v.as_bool()),
        message: parsed
            .get("message")
            .and_then(|v| v.as_str())
            .unwrap_or_default()
            .to_string(),
        overlays: parsed
            .get("overlays")
            .and_then(|v| v.as_array())
            .cloned()
            .unwrap_or_default(),
        metrics: parsed
            .get("metrics")
            .and_then(|v| v.as_array())
            .cloned()
            .unwrap_or_default(),
        console: parsed
            .get("console")
            .and_then(|v| v.as_array())
            .map(|arr| {
                arr.iter()
                    .map(|v| match v {
                        serde_json::Value::String(s) => s.clone(),
                        other => other.to_string(),
                    })
                    .collect()
            })
            .unwrap_or_default(),
        error: None,
    }
}

// ==================== 单元测试 ====================

#[cfg(test)]
mod tests {
    use super::*;
    use image::{Rgb, RgbImage};

    fn solid_image(w: u32, h: u32, v: u8) -> RgbImage {
        RgbImage::from_fn(w, h, |_, _| Rgb([v, v, v]))
    }

    fn control(kind: &str) -> VisionControl {
        let mut c = VisionControl {
            id: "t".into(),
            name: "t".into(),
            kind: kind.into(),
            x: 0.0,
            y: 0.0,
            w: 0.0,
            h: 0.0,
            cx: 0.0,
            cy: 0.0,
            r: 0.0,
            x1: 0.0,
            y1: 0.0,
            x2: 0.0,
            y2: 0.0,
            points: vec![],
        };
        match kind {
            "region" => {
                c.x = 0.0;
                c.y = 0.0;
                c.w = 4.0;
                c.h = 4.0;
            }
            "circle" => {
                c.cx = 5.0;
                c.cy = 5.0;
                c.r = 1.0;
            }
            "point" => {
                c.x = 2.0;
                c.y = 2.0;
            }
            "line" => {
                c.x1 = 0.0;
                c.y1 = 5.0;
                c.x2 = 9.0;
                c.y2 = 5.0;
            }
            "polygon" => {
                c.points = vec![[0.0, 0.0], [4.0, 0.0], [4.0, 4.0], [0.0, 4.0]];
            }
            _ => {}
        }
        c
    }

    #[test]
    fn base64_known_vectors() {
        assert_eq!(base64_encode(b""), "");
        assert_eq!(base64_encode(b"f"), "Zg==");
        assert_eq!(base64_encode(b"fo"), "Zm8=");
        assert_eq!(base64_encode(b"foo"), "Zm9v");
        assert_eq!(base64_encode(b"Man"), "TWFu");
    }

    #[test]
    fn polygon_hit_test() {
        let sq = vec![[0.0, 0.0], [10.0, 0.0], [10.0, 10.0], [0.0, 10.0]];
        assert!(point_in_polygon(5.0, 5.0, &sq));
        assert!(!point_in_polygon(15.0, 15.0, &sq));
        // 退化多边形
        assert!(!point_in_polygon(0.0, 0.0, &[[0.0, 0.0], [1.0, 1.0]]));
    }

    #[test]
    fn stats_for_all_shape_kinds() {
        let img = solid_image(10, 10, 100);
        // 矩形 4x4
        let s = compute_stat(&img, &control("region"));
        assert_eq!(s.count, 16);
        assert_eq!(s.mean, Some(100.0));
        assert_eq!(s.std_dev, Some(0.0));
        assert_eq!(s.min, Some(100.0));
        assert_eq!(s.max, Some(100.0));
        // 圆 r=1 覆盖中心 4 个像素（采样点在像素中心）
        let s = compute_stat(&img, &control("circle"));
        assert_eq!(s.count, 4);
        assert_eq!(s.mean, Some(100.0));
        // 点
        let s = compute_stat(&img, &control("point"));
        assert_eq!(s.count, 1);
        // 横线 0..9 共 10 个像素（去重）
        let s = compute_stat(&img, &control("line"));
        assert_eq!(s.count, 10);
        // 四边形覆盖 16 个像素
        let s = compute_stat(&img, &control("polygon"));
        assert_eq!(s.count, 16);
    }

    #[test]
    fn stats_clipped_and_variable() {
        let img = solid_image(10, 10, 100);
        // 矩形左上越界：x[-2,2) 在图内 2 列 × y 4 行 = 8
        let mut c = control("region");
        c.x = -2.0;
        let s = compute_stat(&img, &c);
        assert_eq!(s.count, 8);
        // 完全在图外 → 空统计
        c.x = 100.0;
        c.y = 100.0;
        let s = compute_stat(&img, &c);
        assert_eq!(s.count, 0);
        assert_eq!(s.mean, None);

        // 非均匀图 2x2：灰度 0/50/100/200 → 均值 87.5
        let mut img2 = RgbImage::new(2, 2);
        for (i, v) in [0u8, 50, 100, 200].iter().enumerate() {
            img2.put_pixel((i % 2) as u32, (i / 2) as u32, Rgb([*v, *v, *v]));
        }
        let mut full = control("region");
        full.w = 2.0;
        full.h = 2.0;
        let s = compute_stat(&img2, &full);
        assert_eq!(s.count, 4);
        assert_eq!(s.mean, Some(87.5));
    }

    #[test]
    fn default_script_runs_through_boa() {
        // 端到端：默认脚本 + 合成 context，验证 boa 桥接与 JSON 取回
        let img = solid_image(10, 10, 100);
        let ctrl = control("region");
        let ctx = serde_json::json!({
            "nowMs": 0,
            "image": { "fileName": "t.png", "width": 10, "height": 10 },
            "controls": [ control_to_json(&ctrl, &img) ],
        })
        .to_string();
        let out = eval_script_timeout(DEFAULT_SCRIPT, &ctx).expect("eval ok");
        let v: serde_json::Value = serde_json::from_str(&out).unwrap();
        assert_eq!(v.get("pass").and_then(|x| x.as_bool()), Some(true));
        assert_eq!(v.get("message").and_then(|x| x.as_str()), Some("OK"));
        assert_eq!(v.get("overlays").unwrap().as_array().unwrap().len(), 1);
    }

    #[test]
    fn invalid_script_returns_err() {
        let r = eval_script_timeout("function inspect(){ notDefinedVar; }", "{}");
        assert!(r.is_err());
    }
}
