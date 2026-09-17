// ==================== 轻度视觉检测工作台（TODO #13） ====================
// 三栏：左 Monaco 脚本（boa_engine 后端执行）、中声明式 ROI 控件画布、
// 右原图/效果图 + 控制台。控件数据 {id,name,type,几何字段,points} 是
// 画布/属性表/脚本 context 的唯一数据源（声明式数据驱动）。

import { t } from "./i18n.js";

const { invoke } = window.__TAURI__.core;
const { open } = window.__TAURI__.dialog;

// ==================== 模块状态 ====================
let visionConfig = { script: "", image_path: "", controls: [] };
let configLoaded = false;
let editor = null;
let imgEl = null; // HTMLImageElement
let imagePath = "";
let imageInfo = null; // {dataUrl,width,height,fileName}
let controls = [];
let selectedId = null;
let active = false;
let lastResult = null;
let dragOp = null;

const PALETTE = [
  "#3b82f6", "#f59e0b", "#22c55e", "#a855f7",
  "#ec4899", "#14b8a6", "#eab308", "#ef4444",
];

const $ = (id) => document.getElementById(id);

// ==================== 对外接口 ====================

/// 软件设置开关变化/启动加载时调用：控制主界面入口按钮可见性
export function applyVisionEnabled(enabled) {
  const btn = $("btn-vision");
  if (btn) btn.style.display = enabled ? "inline-flex" : "none";
  if (!enabled && active) exitVisionView();
}

export function isVisionActive() {
  return active;
}

/// 主界面初始化时调用一次：绑定工作台全部事件
export function initVisionModule() {
  $("btn-vision").addEventListener("click", toggleVisionView);
  $("btn-vision-load-image").addEventListener("click", loadImage);
  $("btn-vision-run").addEventListener("click", runInspection);
  $("btn-vision-save").addEventListener("click", saveVisionConfig);
  $("btn-vision-default").addEventListener("click", loadDefaultScript);
  $("btn-vision-clear-log").addEventListener("click", () => {
    $("vision-console").innerHTML = "";
  });
  $("btn-vision-ctrl-add").addEventListener("click", addControl);
  $("btn-vision-ctrl-del").addEventListener("click", deleteSelectedControl);

  // ROI 画布指针交互
  const canvas = $("vision-roi-canvas");
  canvas.addEventListener("pointerdown", onPointerDown);
  canvas.addEventListener("pointermove", onPointerMove);
  window.addEventListener("pointerup", onPointerUp);

  // 属性表事件委托（控件参数编辑）
  $("vision-prop-grid").addEventListener("input", onPropInput);
  $("vision-prop-grid").addEventListener("click", onPropClick);

  // 控件表：单击行选中（事件委托）
  $("dgv-vision-controls").querySelector("tbody").addEventListener("click", (e) => {
    const tr = e.target.closest("tr[data-id]");
    if (tr) selectControl(tr.dataset.id);
  });

  window.addEventListener("resize", () => {
    if (active) redrawAll();
  });

  // 语言切换后刷新动态生成的文案（控件表/属性表）
  window.addEventListener("app-language-changed", () => {
    if (active) {
      renderControlTable();
      renderPropGrid();
    }
  });
}

// ==================== 视图切换 ====================

async function toggleVisionView() {
  if (active) {
    exitVisionView();
  } else {
    await enterVisionView();
  }
}

async function enterVisionView() {
  active = true;
  $("camera-grid").classList.add("hidden");
  $("vision-view").classList.remove("hidden");
  $("btn-vision").classList.add("active");

  await ensureConfigLoaded();
  await ensureEditor();

  // 等布局完成一帧后再按容器尺寸绘制画布/校正 Monaco 布局
  requestAnimationFrame(() => {
    redrawAll();
    editor?.layout();
  });
}

function exitVisionView() {
  active = false;
  $("vision-view").classList.add("hidden");
  $("camera-grid").classList.remove("hidden");
  $("btn-vision").classList.remove("active");
}

// ==================== 配置加载/保存 ====================

async function ensureConfigLoaded() {
  if (configLoaded) return;
  try {
    const cfg = await invoke("get_vision_config");
    visionConfig = cfg || visionConfig;
    controls = Array.isArray(visionConfig.controls) ? visionConfig.controls : [];
    imagePath = visionConfig.image_path || "";
    if (imagePath) {
      try {
        await reloadImage(imagePath);
      } catch (e) {
        appendLog(`[warn] ${t("visionImageLoadFail")}: ${e}`, "warn");
      }
    }
    renderControlTable();
    renderPropGrid();
    updateEmptyHint();
    configLoaded = true;
  } catch (e) {
    appendLog(`[error] ${t("visionConfigLoadFail")}: ${e}`, "error");
  }
}

async function saveVisionConfig() {
  visionConfig.script = editor ? editor.getValue() : visionConfig.script;
  visionConfig.image_path = imagePath;
  visionConfig.controls = controls;
  try {
    await invoke("set_vision_config", { config: visionConfig });
    appendLog(`[info] ${t("visionSaved")}`, "info");
  } catch (e) {
    appendLog(`[error] ${t("visionSaveFail")}: ${e}`, "error");
  }
}

async function loadDefaultScript() {
  await ensureEditor();
  const script = await invoke("get_default_vision_script");
  editor?.setValue(script);
}

// ==================== Monaco 编辑器 ====================

let monacoPromise = null;
function ensureMonaco() {
  if (window.monaco) return Promise.resolve(window.monaco);
  if (!monacoPromise) {
    monacoPromise = new Promise((resolve) => {
      const req = window.require;
      req.config({ paths: { vs: "assets/monaco-editor/vs" } });
      req(["vs/editor/editor.main"], () => resolve(window.monaco));
    });
  }
  return monacoPromise;
}

async function ensureEditor() {
  const monaco = await ensureMonaco();
  if (editor) return editor;
  const wrap = $("vision-editor-wrap");
  const theme =
    document.documentElement.getAttribute("data-theme") === "dark" ? "vs-dark" : "vs";
  editor = monaco.editor.create(wrap, {
    value: visionConfig.script || "",
    language: "javascript",
    theme,
    automaticLayout: true,
    minimap: { enabled: false },
    scrollBeyondLastLine: false,
    fontSize: 13,
    lineNumbers: "on",
    wordWrap: "on",
    tabSize: 2,
  });
  if (!visionConfig.script) {
    try {
      editor.setValue(await invoke("get_default_vision_script"));
    } catch (_) {}
  }
  return editor;
}

/// 主题切换时由 main.js 调用（与图片清理编辑器共用同一全局 monaco）
export function syncVisionEditorTheme(theme) {
  if (window.monaco && editor) {
    try {
      window.monaco.editor.setTheme(theme === "dark" ? "vs-dark" : "vs");
    } catch (_) {}
  }
}

// ==================== 图片载入 ====================

async function loadImage() {
  const selected = await open({
    multiple: false,
    filters: [
      { name: "Images", extensions: ["png", "jpg", "jpeg", "bmp"] },
    ],
  });
  if (!selected) return;
  try {
    await reloadImage(selected);
    imagePath = selected;
    redrawAll();
    updateEmptyHint();
    appendLog(
      `[info] ${t("visionImageLoaded")}: ${imageInfo.fileName} ${imageInfo.width}×${imageInfo.height}`,
      "info"
    );
  } catch (e) {
    appendLog(`[error] ${t("visionImageLoadFail")}: ${e}`, "error");
  }
}

async function reloadImage(path) {
  const info = await invoke("load_vision_image", { path });
  imageInfo = info;
  imgEl = await new Promise((resolve, reject) => {
    const im = new Image();
    im.onload = () => resolve(im);
    im.onerror = () => reject(new Error("Image decode error"));
    im.src = info.data_url;
  });
}

function updateEmptyHint() {
  $("vision-roi-empty").style.display = imgEl ? "none" : "flex";
}

// ==================== 控件数据模型（声明式） ====================

const TYPE_LABEL_KEY = {
  region: "ctrlRegion",
  circle: "ctrlCircle",
  point: "ctrlPoint",
  line: "ctrlLine",
  polygon: "ctrlPolygon",
};

function genId() {
  return (
    "c" +
    Date.now().toString(36) +
    Math.random().toString(36).slice(2, 7)
  );
}

function newControl(type) {
  const w = imageInfo?.width || 640;
  const h = imageInfo?.height || 480;
  const cx = Math.round(w / 2);
  const cy = Math.round(h / 2);
  const idx = controls.filter((c) => c.type === type).length + 1;
  const c = {
    id: genId(),
    name: `${type}_${idx}`,
    type,
    x: 0,
    y: 0,
    w: 0,
    h: 0,
    cx: 0,
    cy: 0,
    r: 0,
    x1: 0,
    y1: 0,
    x2: 0,
    y2: 0,
    points: [],
  };
  switch (type) {
    case "region":
      Object.assign(c, { x: cx - 50, y: cy - 50, w: 100, h: 100 });
      break;
    case "circle":
      Object.assign(c, { cx, cy, r: 50 });
      break;
    case "point":
      Object.assign(c, { x: cx, y: cy });
      break;
    case "line":
      Object.assign(c, { x1: cx - 60, y1: cy, x2: cx + 60, y2: cy });
      break;
    case "polygon":
      c.points = [
        [cx - 50, cy - 40],
        [cx + 50, cy - 40],
        [cx + 50, cy + 40],
        [cx - 50, cy + 40],
      ];
      break;
  }
  return c;
}

function addControl() {
  const type = $("cmbx-vision-ctrl-type").value;
  const c = newControl(type);
  controls.push(c);
  selectedId = c.id;
  renderControlTable();
  renderPropGrid();
  redrawRoiCanvas();
}

function deleteSelectedControl() {
  if (!selectedId) return;
  controls = controls.filter((c) => c.id !== selectedId);
  selectedId = null;
  renderControlTable();
  renderPropGrid();
  redrawRoiCanvas();
}

function getSelected() {
  return controls.find((c) => c.id === selectedId) || null;
}

function selectControl(id) {
  selectedId = id;
  renderControlTable();
  renderPropGrid();
  redrawRoiCanvas();
}

// ==================== 控件表（电子表格式） ====================

function renderControlTable() {
  const tbody = $("dgv-vision-controls").querySelector("tbody");
  tbody.innerHTML = "";
  controls.forEach((c, i) => {
    const tr = document.createElement("tr");
    tr.dataset.id = c.id;
    if (c.id === selectedId) tr.classList.add("selected");
    const tdIdx = document.createElement("td");
    tdIdx.textContent = i + 1;
    const tdName = document.createElement("td");
    tdName.textContent = c.name;
    const tdType = document.createElement("td");
    tdType.textContent = t(TYPE_LABEL_KEY[c.type]) || c.type;
    tr.append(tdIdx, tdName, tdType);
    tbody.appendChild(tr);
  });
}

// ==================== 属性表（数据驱动） ====================

function field(label, key, value, isInt = true) {
  const v = isInt ? Math.round(value) : value;
  return `<label>${label}<input type="number" step="1" data-key="${key}" value="${v}" /></label>`;
}

function renderPropGrid() {
  const host = $("vision-prop-grid");
  const c = getSelected();
  if (!c) {
    host.innerHTML = `<div class="vision-prop-empty">${t("visionPropEmpty")}</div>`;
    return;
  }
  let html = `<div class="vision-prop-title">${t("visionProperties")}</div>`;
  html += `<label class="vision-prop-name">${t("visionControlName")}<input type="text" data-key="name" value="${escapeHtml(c.name)}" /></label>`;
  html += `<div class="vision-prop-fields">`;
  switch (c.type) {
    case "region":
      html +=
        field("X", "x", c.x) + field("Y", "y", c.y) +
        field("W", "w", c.w) + field("H", "h", c.h);
      break;
    case "circle":
      html +=
        field("CX", "cx", c.cx) + field("CY", "cy", c.cy) +
        field("R", "r", c.r);
      break;
    case "point":
      html += field("X", "x", c.x) + field("Y", "y", c.y);
      break;
    case "line":
      html +=
        field("X1", "x1", c.x1) + field("Y1", "y1", c.y1) +
        field("X2", "x2", c.x2) + field("Y2", "y2", c.y2);
      break;
    case "polygon":
      c.points.forEach((p, i) => {
        html +=
          `<label>P${i + 1}X<input type="number" data-pi="${i}" data-pk="0" value="${Math.round(p[0])}" /></label>` +
          `<label>P${i + 1}Y<input type="number" data-pi="${i}" data-pk="1" value="${Math.round(p[1])}" /></label>`;
      });
      break;
  }
  html += `</div>`;
  if (c.type === "polygon") {
    html += `<div class="vision-prop-actions">
      <button type="button" id="btn-poly-add" class="btn-secondary">${t("visionAddVertex")}</button>
      <button type="button" id="btn-poly-del" class="btn-secondary">${t("visionDelVertex")}</button>
    </div>`;
  }
  host.innerHTML = html;
}

function onPropInput(e) {
  const c = getSelected();
  if (!c) return;
  const el = e.target;
  if (el.dataset.key) {
    const key = el.dataset.key;
    if (key === "name") {
      c.name = el.value;
      renderControlTable();
    } else {
      c[key] = parseFloat(el.value) || 0;
      redrawRoiCanvas();
    }
  } else if (el.dataset.pi !== undefined) {
    const i = parseInt(el.dataset.pi, 10);
    const k = parseInt(el.dataset.pk, 10);
    if (c.points[i]) c.points[i][k] = parseFloat(el.value) || 0;
    redrawRoiCanvas();
  }
}

function onPropClick(e) {
  const c = getSelected();
  if (!c || c.type !== "polygon") return;
  if (e.target.id === "btn-poly-add") {
    const last = c.points[c.points.length - 1] || [0, 0];
    c.points.push([last[0] + 12, last[1] + 12]);
    renderPropGrid();
    redrawRoiCanvas();
  } else if (e.target.id === "btn-poly-del") {
    if (c.points.length > 1) c.points.pop();
    renderPropGrid();
    redrawRoiCanvas();
  }
}

// ==================== ROI 画布：坐标变换 ====================

function getCanvasView(canvas) {
  if (!imageInfo) return null;
  const cw = canvas.clientWidth;
  const ch = canvas.clientHeight;
  if (cw === 0 || ch === 0) return null;
  const scale = Math.min(cw / imageInfo.width, ch / imageInfo.height);
  return {
    scale,
    ox: (cw - imageInfo.width * scale) / 2,
    oy: (ch - imageInfo.height * scale) / 2,
  };
}

function toImagePoint(canvas, clientX, clientY) {
  const rect = canvas.getBoundingClientRect();
  const v = getCanvasView(canvas);
  return {
    x: (clientX - rect.left - v.ox) / v.scale,
    y: (clientY - rect.top - v.oy) / v.scale,
  };
}

const HANDLE_HIT = 8; // 手柄命中半径（CSS 像素）

// ==================== ROI 画布：命中测试 ====================

function dist2(ax, ay, bx, by) {
  const dx = ax - bx;
  const dy = ay - by;
  return dx * dx + dy * dy;
}

function hitControl(c, p) {
  // 返回命中类型：'handle:<名>' 或 'body' 或 null。p 为图像坐标。
  // 手柄命中半径按当前缩放换算到图像坐标，保证屏幕上手感一致。
  const canvas = $("vision-roi-canvas");
  const v = getCanvasView(canvas);
  const hitR = HANDLE_HIT / (v ? v.scale : 1);
  const hitR2 = hitR * hitR;
  switch (c.type) {
    case "region": {
      const corners = [
        ["nw", c.x, c.y],
        ["ne", c.x + c.w, c.y],
        ["se", c.x + c.w, c.y + c.h],
        ["sw", c.x, c.y + c.h],
      ];
      for (const [name, hx, hy] of corners) {
        if (dist2(p.x, p.y, hx, hy) <= hitR2) {
          return `handle:${name}`;
        }
      }
      if (p.x >= c.x && p.x <= c.x + c.w && p.y >= c.y && p.y <= c.y + c.h) {
        return "body";
      }
      return null;
    }
    case "circle": {
      if (dist2(p.x, p.y, c.cx + c.r, c.cy) <= hitR2) {
        return "handle:r";
      }
      if (dist2(p.x, p.y, c.cx, c.cy) <= c.r * c.r) return "body";
      return null;
    }
    case "point":
      if (dist2(p.x, p.y, c.x, c.y) <= hitR2) {
        return "body";
      }
      return null;
    case "line": {
      if (dist2(p.x, p.y, c.x1, c.y1) <= hitR2) return "handle:p1";
      if (dist2(p.x, p.y, c.x2, c.y2) <= hitR2) return "handle:p2";
      if (ptSegDist(p.x, p.y, c) <= hitR) return "body";
      return null;
    }
    case "polygon": {
      for (let i = 0; i < c.points.length; i++) {
        const [px, py] = c.points[i];
        if (dist2(p.x, p.y, px, py) <= hitR2) {
          return `handle:v${i}`;
        }
      }
      if (pointInPolygon(p.x, p.y, c.points)) return "body";
      return null;
    }
  }
  return null;
}

function ptSegDist(x, y, c) {
  const dx = c.x2 - c.x1;
  const dy = c.y2 - c.y1;
  const l2 = dx * dx + dy * dy;
  let t = l2 === 0 ? 0 : ((x - c.x1) * dx + (y - c.y1) * dy) / l2;
  t = Math.max(0, Math.min(1, t));
  const qx = c.x1 + t * dx;
  const qy = c.y1 + t * dy;
  return Math.hypot(x - qx, y - qy);
}

function pointInPolygon(x, y, pts) {
  let inside = false;
  for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) {
    const xi = pts[i][0], yi = pts[i][1];
    const xj = pts[j][0], yj = pts[j][1];
    const intersect =
      yi > y !== yj > y &&
      x < ((xj - xi) * (y - yi)) / (yj - yi || 1e-9) + xi;
    if (intersect) inside = !inside;
  }
  return inside;
}

// ==================== ROI 画布：拖拽交互 ====================

function clampX(v) {
  const max = imageInfo?.width ?? 1e9;
  return Math.round(Math.max(0, Math.min(max, v)));
}
function clampY(v) {
  const max = imageInfo?.height ?? 1e9;
  return Math.round(Math.max(0, Math.min(max, v)));
}

function moveControl(c, dx, dy) {
  switch (c.type) {
    case "region":
      c.x = clampX(c.x + dx);
      c.y = clampY(c.y + dy);
      break;
    case "circle":
      c.cx = clampX(c.cx + dx);
      c.cy = clampY(c.cy + dy);
      break;
    case "point":
      c.x = clampX(c.x + dx);
      c.y = clampY(c.y + dy);
      break;
    case "line":
      c.x1 = clampX(c.x1 + dx);
      c.y1 = clampY(c.y1 + dy);
      c.x2 = clampX(c.x2 + dx);
      c.y2 = clampY(c.y2 + dy);
      break;
    case "polygon":
      c.points = c.points.map((p) => [clampX(p[0] + dx), clampY(p[1] + dy)]);
      break;
  }
}

function onPointerDown(e) {
  if (!imgEl) return;
  const canvas = $("vision-roi-canvas");
  const p = toImagePoint(canvas, e.clientX, e.clientY);
  // 自顶向下命中（后画的在上）
  for (let i = controls.length - 1; i >= 0; i--) {
    const c = controls[i];
    const hit = hitControl(c, p);
    if (!hit) continue;
    selectedId = c.id;
    renderControlTable();
    renderPropGrid();
    canvas.setPointerCapture(e.pointerId);
    dragOp = {
      controlId: c.id,
      hit,
      start: p,
      moved: false,
    };
    redrawRoiCanvas();
    return;
  }
  // 点空白处取消选中
  selectedId = null;
  renderControlTable();
  renderPropGrid();
  redrawRoiCanvas();
}

function onPointerMove(e) {
  if (!dragOp) return;
  const canvas = $("vision-roi-canvas");
  const p = toImagePoint(canvas, e.clientX, e.clientY);
  const c = controls.find((x) => x.id === dragOp.controlId);
  if (!c) return;
  const dx = p.x - dragOp.start.x;
  const dy = p.y - dragOp.start.y;
  if (Math.abs(dx) + Math.abs(dy) > 0.5) dragOp.moved = true;

  if (dragOp.hit === "body") {
    if (!dragOp.last) {
      dragOp.last = dragOp.start;
    }
    const stepX = p.x - dragOp.last.x;
    const stepY = p.y - dragOp.last.y;
    moveControl(c, stepX, stepY);
    dragOp.last = p;
  } else {
    resizeControl(c, dragOp.hit, p);
  }
  redrawRoiCanvas();
}

function onPointerUp() {
  if (dragOp) {
    dragOp = null;
    renderPropGrid();
  }
}

function resizeControl(c, hit, p) {
  const x = clampX(p.x);
  const y = clampY(p.y);
  if (c.type === "region") {
    let { x: rx, y: ry, w: rw, h: rh } = c;
    if (hit === "handle:nw") {
      rw = c.x + c.w - x;
      rh = c.y + c.h - y;
      rx = x;
      ry = y;
    } else if (hit === "handle:ne") {
      rw = x - c.x;
      rh = c.y + c.h - y;
      ry = y;
    } else if (hit === "handle:sw") {
      rw = c.x + c.w - x;
      rh = y - c.y;
      rx = x;
    } else if (hit === "handle:se") {
      rw = x - c.x;
      rh = y - c.y;
    }
    if (rw >= 2 && rh >= 2) {
      c.x = rx;
      c.y = ry;
      c.w = rw;
      c.h = rh;
    }
  } else if (c.type === "circle" && hit === "handle:r") {
    c.r = Math.max(2, Math.hypot(p.x - c.cx, p.y - c.cy));
  } else if (c.type === "line") {
    if (hit === "handle:p1") {
      c.x1 = x;
      c.y1 = y;
    } else if (hit === "handle:p2") {
      c.x2 = x;
      c.y2 = y;
    }
  } else if (c.type === "polygon" && hit.startsWith("handle:v")) {
    const i = parseInt(hit.slice("handle:v".length), 10);
    if (c.points[i]) c.points[i] = [x, y];
  }
}

// ==================== 画布绘制 ====================

function setupCanvasResolution(canvas) {
  const dpr = window.devicePixelRatio || 1;
  const w = canvas.clientWidth;
  const h = canvas.clientHeight;
  if (canvas.width !== Math.round(w * dpr) || canvas.height !== Math.round(h * dpr)) {
    canvas.width = Math.round(w * dpr);
    canvas.height = Math.round(h * dpr);
  }
  const ctx = canvas.getContext("2d");
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  return { ctx, w, h };
}

function redrawAll() {
  redrawRoiCanvas();
  redrawImageCanvas($("vision-original-canvas"), null);
  redrawResultCanvas();
}

function drawImageFit(canvas, overlayFn) {
  const { ctx, w, h } = setupCanvasResolution(canvas);
  ctx.clearRect(0, 0, w, h);
  if (!imgEl) return;
  const v = getCanvasView(canvas);
  if (!v) return;
  ctx.imageSmoothingEnabled = false;
  ctx.drawImage(imgEl, v.ox, v.oy, imageInfo.width * v.scale, imageInfo.height * v.scale);
  ctx.save();
  ctx.beginPath();
  ctx.rect(v.ox, v.oy, imageInfo.width * v.scale, imageInfo.height * v.scale);
  ctx.clip();
  overlayFn?.(ctx, v);
  ctx.restore();
  // 图片边框
  ctx.strokeStyle = "#888888";
  ctx.lineWidth = 1;
  ctx.strokeRect(v.ox + 0.5, v.oy + 0.5, imageInfo.width * v.scale - 1, imageInfo.height * v.scale - 1);
}

function redrawRoiCanvas() {
  const canvas = $("vision-roi-canvas");
  if (!active || !canvas.clientWidth) return;
  drawImageFit(canvas, (ctx, v) => {
    controls.forEach((c, i) => {
      const selected = c.id === selectedId;
      const color = selected ? "#ef4444" : PALETTE[i % PALETTE.length];
      drawControl(ctx, v, c, color, selected);
    });
  });
}

function drawHandle(ctx, x, y, selected) {
  ctx.beginPath();
  ctx.rect(x - 4, y - 4, 8, 8);
  ctx.fillStyle = "#ffffff";
  ctx.fill();
  ctx.lineWidth = selected ? 2 : 1;
  ctx.strokeStyle = selected ? "#ef4444" : "#222222";
  ctx.stroke();
}

function drawLabel(ctx, text, x, y, color) {
  ctx.font = "12px sans-serif";
  const w = ctx.measureText(text).width + 8;
  ctx.fillStyle = "rgba(0,0,0,0.65)";
  ctx.fillRect(x, y - 14, w, 16);
  ctx.fillStyle = color || "#ffffff";
  ctx.textBaseline = "middle";
  ctx.fillText(text, x + 4, y - 6);
}

function drawControl(ctx, v, c, color, selected) {
  const s = (x, y) => [x * v.scale + v.ox, y * v.scale + v.oy];
  ctx.strokeStyle = color;
  ctx.fillStyle = color;
  ctx.lineWidth = selected ? 2.5 : 1.6;

  if (c.type === "region") {
    const [x, y] = s(c.x, c.y);
    ctx.strokeRect(x, y, c.w * v.scale, c.h * v.scale);
    drawLabel(ctx, c.name, x, y, color);
    if (selected) {
      [[c.x, c.y], [c.x + c.w, c.y], [c.x + c.w, c.y + c.h], [c.x, c.y + c.h]]
        .forEach(([hx, hy]) => drawHandle(ctx, ...s(hx, hy), true));
    }
  } else if (c.type === "circle") {
    const [cx, cy] = s(c.cx, c.cy);
    ctx.beginPath();
    ctx.arc(cx, cy, c.r * v.scale, 0, Math.PI * 2);
    ctx.stroke();
    drawLabel(ctx, c.name, cx - c.r * v.scale, cy - c.r * v.scale, color);
    if (selected) {
      drawHandle(ctx, ...s(c.cx + c.r, c.cy), true);
      ctx.beginPath();
      ctx.arc(cx, cy, 2.5, 0, Math.PI * 2);
      ctx.fill();
    }
  } else if (c.type === "point") {
    const [x, y] = s(c.x, c.y);
    ctx.beginPath();
    ctx.arc(x, y, selected ? 6 : 5, 0, Math.PI * 2);
    ctx.fill();
    ctx.lineWidth = 1.5;
    ctx.strokeStyle = "#ffffff";
    ctx.stroke();
    drawLabel(ctx, c.name, x + 8, y + 4, color);
  } else if (c.type === "line") {
    const [x1, y1] = s(c.x1, c.y1);
    const [x2, y2] = s(c.x2, c.y2);
    ctx.beginPath();
    ctx.moveTo(x1, y1);
    ctx.lineTo(x2, y2);
    ctx.stroke();
    drawLabel(ctx, c.name, x1, y1 - 6, color);
    if (selected) {
      drawHandle(ctx, x1, y1, true);
      drawHandle(ctx, x2, y2, true);
    }
  } else if (c.type === "polygon") {
    if (c.points.length) {
      ctx.beginPath();
      c.points.forEach(([px, py], i) => {
        const [sx, sy] = s(px, py);
        if (i === 0) ctx.moveTo(sx, sy);
        else ctx.lineTo(sx, sy);
      });
      ctx.closePath();
      ctx.stroke();
      const [lx, ly] = s(c.points[0][0], c.points[0][1]);
      drawLabel(ctx, c.name, lx, ly, color);
      if (selected) {
        c.points.forEach(([px, py]) => drawHandle(ctx, ...s(px, py), true));
      }
    }
  }
}

function redrawImageCanvas(canvas, overlays) {
  if (!active || !canvas.clientWidth) return;
  drawImageFit(canvas, (ctx, v) => drawOverlays(ctx, v, overlays || []));
}

function redrawResultCanvas() {
  const canvas = $("vision-result-canvas");
  if (!active || !canvas.clientWidth) return;
  drawImageFit(canvas, (ctx, v) => {
    if (lastResult && !lastResult.error) {
      drawOverlays(ctx, v, lastResult.overlays || []);
    }
  });
}

function drawOverlays(ctx, v, overlays) {
  for (const o of overlays) {
    const color = o.color || "#f97316";
    ctx.strokeStyle = color;
    ctx.fillStyle = color;
    ctx.lineWidth = 2;
    const s = (x, y) => [x * v.scale + v.ox, y * v.scale + v.oy];
    if (o.type === "rect") {
      const [x, y] = s(o.x || 0, o.y || 0);
      ctx.strokeRect(x, y, (o.w || 0) * v.scale, (o.h || 0) * v.scale);
      if (o.label) drawLabel(ctx, String(o.label), x, y, color);
    } else if (o.type === "circle") {
      const [cx, cy] = s(o.cx || 0, o.cy || 0);
      ctx.beginPath();
      ctx.arc(cx, cy, (o.r || 0) * v.scale, 0, Math.PI * 2);
      ctx.stroke();
      if (o.label) drawLabel(ctx, String(o.label), cx - (o.r || 0) * v.scale, cy - (o.r || 0) * v.scale, color);
    } else if (o.type === "point") {
      const [x, y] = s(o.x || 0, o.y || 0);
      ctx.beginPath();
      ctx.arc(x, y, 4, 0, Math.PI * 2);
      ctx.fill();
      if (o.label) drawLabel(ctx, String(o.label), x + 6, y + 4, color);
    } else if (o.type === "line") {
      const [x1, y1] = s(o.x1 || 0, o.y1 || 0);
      const [x2, y2] = s(o.x2 || 0, o.y2 || 0);
      ctx.beginPath();
      ctx.moveTo(x1, y1);
      ctx.lineTo(x2, y2);
      ctx.stroke();
      if (o.label) drawLabel(ctx, String(o.label), x1, y1 - 6, color);
    } else if (o.type === "polygon" && Array.isArray(o.points)) {
      ctx.beginPath();
      o.points.forEach((p, i) => {
        const [sx, sy] = s(p[0] || 0, p[1] || 0);
        if (i === 0) ctx.moveTo(sx, sy);
        else ctx.lineTo(sx, sy);
      });
      ctx.closePath();
      ctx.stroke();
      if (o.label && o.points[0]) {
        const [lx, ly] = s(o.points[0][0], o.points[0][1]);
        drawLabel(ctx, String(o.label), lx, ly, color);
      }
    } else if (o.type === "text") {
      const [x, y] = s(o.x || 0, o.y || 0);
      drawLabel(ctx, String(o.text ?? ""), x, y + 12, color);
    }
  }
}

// ==================== 运行检测 ====================

async function runInspection() {
  if (!imgEl || !imagePath) {
    appendLog(`[error] ${t("visionNoImage")}`, "error");
    return;
  }
  await ensureEditor();
  const script = editor ? editor.getValue() : "";
  appendLog(`[info] ${t("visionRunning")} …`, "info");
  const verdictEl = $("vision-verdict");
  verdictEl.textContent = "";
  verdictEl.className = "vision-verdict";
  try {
    const res = await invoke("run_vision_inspection", {
      script,
      imagePath,
      controls,
    });
    lastResult = res;
    (res.console || []).forEach((line) => {
      const level = line.startsWith("[error]")
        ? "error"
        : line.startsWith("[warn]")
          ? "warn"
          : "info";
      appendLog(line, level);
    });
    if (res.error) {
      appendLog(`[error] ${res.error}`, "error");
    } else {
      if (res.pass === true) {
        verdictEl.textContent = t("visionPass");
        verdictEl.classList.add("pass");
      } else if (res.pass === false) {
        verdictEl.textContent = t("visionFail");
        verdictEl.classList.add("fail");
      } else {
        verdictEl.textContent = res.message || "--";
        verdictEl.classList.add("none");
      }
      if (res.message) appendLog(`[info] ${t("visionRunResult")}: ${res.message}`, "info");
      (res.metrics || []).forEach((m) => {
        const val =
          typeof m.value === "number"
            ? (Math.round(m.value * 100) / 100).toString()
            : String(m.value ?? "");
        appendLog(`  ${m.name} = ${val}`, "metric");
      });
    }
    redrawResultCanvas();
  } catch (e) {
    appendLog(`[error] ${t("visionRunFail")}: ${e}`, "error");
  }
}

// ==================== 控制台 ====================

function escapeHtml(s) {
  return String(s).replace(
    /[&<>"']/g,
    (ch) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[ch]
  );
}

function appendLog(text, level = "info") {
  const box = $("vision-console");
  const now = new Date();
  const ts = `${String(now.getHours()).padStart(2, "0")}:${String(
    now.getMinutes()
  ).padStart(2, "0")}:${String(now.getSeconds()).padStart(2, "0")}`;
  const line = document.createElement("div");
  line.className = `vision-log-line vision-log-${level}`;
  line.innerHTML = `<span class="vision-log-ts">${ts}</span> ${escapeHtml(text)}`;
  box.appendChild(line);
  box.scrollTop = box.scrollHeight;
}
