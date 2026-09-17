const { invoke } = window.__TAURI__.core;
const { open } = window.__TAURI__.dialog;
import { t, setLanguage, getLanguage } from "./i18n.js";
import {
  initVisionModule,
  applyVisionEnabled,
  syncVisionEditorTheme,
} from "./vision.js";
import {
  initCogSocketCells,
  openCogSocketDialog,
} from "./cogsocket_cells.js";

// ==================== 全局状态 ====================
let cameraConfig = { count: 1, delay: 10, items: [] };
let transformConfigs = [];
let cleanConfig = {};
let compressConfig = {};
let ntpConfig = {};
let ftpConfig = {};
let jobxBackupConfig = { cameras: [] };
let appConfig = { language: "zh", theme: "light", vision_inspection_enabled: false };
let currentTransformIndex = -1;
let currentJobxIndex = -1;
let pendingJobxEdit = false;
// 图片清理高级版：Monaco 编辑器实例（按需初始化）
let cleanMonacoEditor = null;
let monacoReady = false;
let currentDirPartIndex = -1;
let currentFilePartIndex = -1;
let maximizedCamera = -1;
let compressPaused = false;

// ==================== 初始化 ====================
window.addEventListener("DOMContentLoaded", async () => {
  try {
    await loadConfigs();
    applyTheme(appConfig.theme || "light");
    initUI();
    startTimer();
    setupEventListeners();
    updateAppSettingsUI();

    // 监听托盘“关于”事件
    if (window.__TAURI__?.event) {
      window.__TAURI__.event.listen("show_about", () => showAbout());
    }
  } catch (e) {
    console.error("初始化失败：", e);
  } finally {
    // 无论初始化成功与否都关闭启动画面（Rust setup 另有 15s 兜底）
    await closeSplashscreen();
  }
});

// ==================== 启动画面（Tauri 官方 splashscreen 方式） ====================
async function closeSplashscreen() {
  // 等主界面先绘制两帧，避免关闭启动画面后看到未完成布局
  await new Promise((resolve) =>
    requestAnimationFrame(() => requestAnimationFrame(resolve))
  );
  const elapsed = Math.round(performance.now());
  console.log(`[启动耗时] UI 就绪：${elapsed.toLocaleString("en-US")} ms`);
  try {
    // 关闭独立 splashscreen 窗口并显示主窗口（Rust 端实现，幂等）
    await invoke("close_splashscreen");
  } catch (e) {
    console.error("关闭启动画面失败：", e);
  }
}

async function loadConfigs() {
  cameraConfig = await invoke("get_camera_config");
  transformConfigs = await invoke("get_transform_configs");
  cleanConfig = await invoke("get_clean_config");
  compressConfig = await invoke("get_compress_config");
  ntpConfig = await invoke("get_ntp_config");
  ftpConfig = await invoke("get_ftp_config");
  jobxBackupConfig = await invoke("get_jobx_backup_config");
  appConfig = await invoke("get_app_config");
  if (appConfig.language) {
    setLanguage(appConfig.language);
  }
  if (appConfig.theme) {
    applyTheme(appConfig.theme);
  }
  // 相机连接模式回显（默认 http）
  const connModeSel = document.getElementById("cmbx-connection-mode");
  if (connModeSel) {
    connModeSel.value = appConfig.connection_mode || "http";
  }
  // 轻度视觉检测开关：控制主界面入口按钮显隐（TODO #13）
  applyVisionEnabled(appConfig.vision_inspection_enabled === true);
}

function initUI() {
  renderCameraGrid();
  updateStatusBar();
  updateThemeIcon();
  updateLangIcon();
}

function startTimer() {
  setInterval(async () => {
    const time = await invoke("get_current_time");
    document.getElementById("lbl-time").textContent = `${t("systemTime")}${time}`;
  }, 1000);
}

async function updateStatusBar() {
  const version = await invoke("get_app_version");
  document.getElementById("lbl-version").textContent = `${t("version")}${version}`;
}

// ==================== 主题切换 ====================
function applyTheme(theme) {
  document.documentElement.setAttribute("data-theme", theme);
  appConfig.theme = theme;
  // 同步 Monaco 编辑器主题（高级版脚本编辑器）
  if (monacoReady && window.monaco) {
    try {
      window.monaco.editor.setTheme(theme === "dark" ? "vs-dark" : "vs");
    } catch (_) {}
  }
  // 视觉检测工作台的 Monaco 编辑器
  syncVisionEditorTheme(theme);
}

function toggleTheme() {
  const current = document.documentElement.getAttribute("data-theme") || "light";
  const next = current === "light" ? "dark" : "light";
  applyTheme(next);
  updateThemeIcon();
  updateAppSettingsUI();
  saveAppConfig();
}

function updateThemeIcon() {
  const icon = document.getElementById("icon-theme");
  const current = document.documentElement.getAttribute("data-theme") || "light";
  if (icon) {
    icon.src = current === "light" ? "assets/moon.svg" : "assets/sun.svg";
  }
}

// ==================== 语言切换 ====================
function toggleLanguage() {
  const current = getLanguage();
  const next = current === "zh" ? "en" : "zh";
  setAppLanguage(next);
}

// 软件设置页/主界面共用：显式设置语言并持久化
function setAppLanguage(lang) {
  if (getLanguage() === lang) return;
  setLanguage(lang);
  appConfig.language = lang;
  updateLangIcon();
  updateAppSettingsUI();
  updateStatusBar();
  saveAppConfig();
  // 同步切换所有相机 HMI 网页（iframe 内由注入脚本做文本替换）
  broadcastHmiLang();
  // 通知动态生成 UI 的模块（视觉检测控件表/属性表）刷新文案
  window.dispatchEvent(new CustomEvent("app-language-changed"));
}

// 软件设置页/主界面共用：显式设置主题并持久化
function setAppTheme(theme) {
  const current = document.documentElement.getAttribute("data-theme") || "light";
  if (current === theme) return;
  applyTheme(theme);
  updateThemeIcon();
  updateAppSettingsUI();
  saveAppConfig();
}

// 同步“软件设置”页分段按钮选中态（主界面切换时也会调用）
function updateAppSettingsUI() {
  document.querySelectorAll("#seg-language .seg-btn").forEach((b) =>
    b.classList.toggle("active", b.dataset.lang === getLanguage())
  );
  const theme = document.documentElement.getAttribute("data-theme") || "light";
  document.querySelectorAll("#seg-theme .seg-btn").forEach((b) =>
    b.classList.toggle("active", b.dataset.theme === theme)
  );
  // 相机连接模式回显
  const connModeSel = document.getElementById("cmbx-connection-mode");
  if (connModeSel) {
    connModeSel.value = (appConfig && appConfig.connection_mode) || "http";
  }
  // 轻度视觉检测开关回显
  const visionChk = document.getElementById("ckbx-vision-enabled");
  if (visionChk) {
    visionChk.checked = (appConfig && appConfig.vision_inspection_enabled) === true;
  }
}

function updateLangIcon() {
  const icon = document.getElementById("icon-lang");
  const current = getLanguage();
  if (icon) {
    icon.src = current === "zh" ? "assets/lang-zh.svg" : "assets/lang-en.svg";
  }
}

async function saveAppConfig() {
  await invoke("set_app_config", { config: appConfig });
}

// ==================== 相机网格 ====================
function renderCameraGrid() {
  const grid = document.getElementById("camera-grid");
  grid.innerHTML = "";

  const count = cameraConfig.items.length || cameraConfig.count;
  if (count === 0) return;

  let cols, rows;
  if (count === 1) { cols = 1; rows = 1; }
  else if (count === 2) { cols = 2; rows = 1; }
  else if (count <= 4) { cols = 2; rows = 2; }
  else if (count <= 6) { cols = 3; rows = 2; }
  else if (count <= 9) { cols = 3; rows = 3; }
  else { cols = 4; rows = 3; }

  grid.style.gridTemplateColumns = `repeat(${cols}, 1fr)`;
  grid.style.gridTemplateRows = `repeat(${rows}, 1fr)`;

  for (let i = 0; i < count; i++) {
    const item = cameraConfig.items[i] || { id: i, ip: "" };
    const cell = createCameraCell(i, item);
    grid.appendChild(cell);
  }
}

function createCameraCell(index, item) {
  const cell = document.createElement("div");
  cell.className = "camera-cell";
  cell.dataset.index = index;

  const header = document.createElement("div");
  header.className = "camera-header";

  const label = document.createElement("label");
  label.textContent = "URL:";

  const isLocked = item.locked === true;

  const input = document.createElement("input");
  input.type = "text";
  input.value = item.ip || "";
  input.placeholder = "https://www.example.com";
  input.dataset.locked = isLocked ? "true" : "false";
  input.readOnly = isLocked;

  const btnLock = document.createElement("button");
  btnLock.className = "btn-lock";
  btnLock.innerHTML = isLocked
    ? `<img src="assets/lock.svg" alt="lock" />`
    : `<img src="assets/unlock.svg" alt="unlock" />`;
  btnLock.title = "锁定/解锁";
  btnLock.classList.toggle("active", isLocked);
  btnLock.addEventListener("click", () => toggleLock(index, input, btnLock, remarkInput));

  const btnRefresh = document.createElement("button");
  btnRefresh.className = "btn-refresh";
  btnRefresh.innerHTML = `<img src="assets/refresh.svg" alt="refresh" />`;
  btnRefresh.title = "刷新";
  btnRefresh.addEventListener("click", () => refreshCamera(cell, input.value.trim()));

  input.addEventListener("change", async () => {
    if (input.dataset.locked === "true") return;
    const url = input.value.trim();
    const remarkVal = remarkInput.value.trim();
    if (url) {
      await invoke("update_camera_ip", { id: index, ip: url, remark: remarkVal });
      refreshCamera(cell, url);
    }
  });

  // 备注输入框
  const remarkLabel = document.createElement("label");
  remarkLabel.textContent = "备注:";
  remarkLabel.className = "remark-label";

  const remarkInput = document.createElement("input");
  remarkInput.type = "text";
  remarkInput.className = "remark-input";
  remarkInput.value = item.remark || "";
  remarkInput.placeholder = "备注...";
  remarkInput.dataset.locked = isLocked ? "true" : "false";
  remarkInput.readOnly = isLocked;
  remarkInput.addEventListener("change", async () => {
    if (remarkInput.dataset.locked === "true") return;
    const url = input.value.trim();
    const remarkVal = remarkInput.value.trim();
    await invoke("update_camera_ip", { id: index, ip: url, remark: remarkVal });
  });

  const btnMaxi = document.createElement("button");
  btnMaxi.className = "btn-maxi";
  btnMaxi.innerHTML = `<img src="assets/maximize.svg" alt="max" />`;
  btnMaxi.addEventListener("click", () => toggleMaximize(index, btnMaxi));

  // CogSocket 模式：每格提供"单元格值设置"入口（TODO #7）；
  // 按钮始终创建，按当前连接模式显隐，避免切模式时重建相机 iframe
  const btnCells = document.createElement("button");
  btnCells.className = "btn-cells";
  btnCells.innerHTML = `<img src="assets/cells.svg" alt="cells" />`;
  btnCells.title = t("cogsocketCellsBtn");
  btnCells.style.display =
    (appConfig && appConfig.connection_mode) === "cogsocket" ? "" : "none";
  btnCells.addEventListener("click", () => {
    openCogSocketDialog(index, {
      ...item,
      ip: input.value.trim() || item.ip || "",
    });
  });

  header.appendChild(label);
  header.appendChild(input);
  header.appendChild(btnLock);
  header.appendChild(btnRefresh);
  header.appendChild(remarkLabel);
  header.appendChild(remarkInput);
  header.appendChild(btnCells);
  header.appendChild(btnMaxi);

  const iframe = document.createElement("iframe");
  iframe.className = "camera-webview";
  iframe.sandbox = "allow-scripts allow-same-origin allow-forms";
  // HMI 网页（/pages/hmi/）内的 i18n 注入脚本加载完成后会上报 ready，
  // 这里在每次 iframe load 时再主动下发一次当前界面语言（非 HMI 页面无监听者，安全）
  iframe.addEventListener("load", () => postHmiLang(iframe.contentWindow));

  if (item.ip) {
    setTimeout(() => {
      refreshCamera(cell, item.ip);
    }, (cameraConfig.delay || 10) * 1000);
  }

  cell.appendChild(header);
  cell.appendChild(iframe);
  return cell;
}

async function toggleLock(index, input, btn, remarkInput) {
  // 当前为未锁定时，点击后进入锁定状态
  const locked = input.dataset.locked !== "true";
  setLockUI(input, btn, remarkInput, locked);

  // 同步内存配置，保证不重新拉取配置的本地重绘也能保持锁定状态
  if (!cameraConfig.items[index]) {
    cameraConfig.items[index] = { id: index, ip: input.value.trim(), remark: "" };
  }
  cameraConfig.items[index].locked = locked;

  try {
    await invoke("update_camera_lock", { id: index, locked });
  } catch (e) {
    console.error("persist camera lock state failed:", e);
  }
}

function setLockUI(input, btn, remarkInput, locked) {
  input.dataset.locked = locked ? "true" : "false";
  input.readOnly = locked;
  if (remarkInput) {
    remarkInput.dataset.locked = locked ? "true" : "false";
    remarkInput.readOnly = locked;
  }
  btn.innerHTML = locked
    ? `<img src="assets/lock.svg" alt="lock" />`
    : `<img src="assets/unlock.svg" alt="unlock" />`;
  btn.classList.toggle("active", locked);
}

function refreshCamera(cell, url) {
  if (!url) return;
  const iframe = cell.querySelector("iframe");
  if (!iframe) return;
  // 自动补全协议头
  let finalUrl = url;
  if (!/^https?:\/\//i.test(url)) {
    finalUrl = `http://${url}`;
  }
  iframe.src = finalUrl;
}

// ==================== HMI 相机网页语言同步 ====================
// 注入脚本（src/assets/hmi-i18n.js，由 Rust initialization_script 挂载到每个
// frame）在 /pages/hmi/ 页面内做整串文本替换；语言通过 postMessage 跨域下发。
function postHmiLang(targetWindow) {
  if (!targetWindow) return;
  try {
    targetWindow.postMessage(
      { __hmiI18n: "lang", lang: getLanguage() },
      "*"
    );
  } catch (e) {
    // frame 尚未就绪或已卸载，忽略（load 时会再下发）
  }
}

// 向所有相机 iframe 广播当前语言（切换语言时调用）
function broadcastHmiLang() {
  document.querySelectorAll("iframe.camera-webview").forEach((f) =>
    postHmiLang(f.contentWindow)
  );
}

// 接收 HMI 注入脚本的 ready 握手并立即回传当前语言
window.addEventListener("message", (e) => {
  const d = e.data;
  if (d && typeof d === "object" && d.__hmiI18n === "ready") {
    postHmiLang(e.source);
  }
});

function toggleMaximize(index, btn) {
  const grid = document.getElementById("camera-grid");
  const cells = grid.querySelectorAll(".camera-cell");

  if (maximizedCamera === index) {
    maximizedCamera = -1;
    cells.forEach(c => c.classList.remove("maximized"));
    btn.innerHTML = `<img src="assets/maximize.svg" alt="max" />`;
  } else {
    maximizedCamera = index;
    cells.forEach(c => c.classList.remove("maximized"));
    const cell = cells[index];
    if (cell) {
      cell.classList.add("maximized");
      // 更新所有按钮图标
      cells.forEach((c, i) => {
        const b = c.querySelector(".btn-maxi");
        if (b) {
          b.innerHTML = i === index
            ? `<img src="assets/restore.svg" alt="restore" />`
            : `<img src="assets/maximize.svg" alt="max" />`;
        }
      });
    }
  }
}

// ==================== 设置弹窗 ====================
function setupEventListeners() {
  // 打开设置：先立即显示弹窗与加载遮罩，等遮罩绘制后再加载数据
  document.getElementById("btn-setting").addEventListener("click", openSetting);

  // 关闭设置
  document.getElementById("btn-close-modal").addEventListener("click", closeSetting);

  // 关于页面
  document.getElementById("btn-about").addEventListener("click", showAbout);
  document.getElementById("btn-close-about").addEventListener("click", closeAbout);

  // 主题切换
  document.getElementById("btn-theme").addEventListener("click", toggleTheme);

  // 语言切换
  document.getElementById("btn-lang").addEventListener("click", toggleLanguage);

  // “软件设置”页分段控件
  document.querySelectorAll("#seg-language .seg-btn").forEach((btn) =>
    btn.addEventListener("click", () => setAppLanguage(btn.dataset.lang))
  );
  document.querySelectorAll("#seg-theme .seg-btn").forEach((btn) =>
    btn.addEventListener("click", () => setAppTheme(btn.dataset.theme))
  );

  // 相机连接模式下拉框：切换即持久化
  document.getElementById("cmbx-connection-mode").addEventListener("change", (e) => {
    appConfig.connection_mode = e.target.value;
    saveAppConfig();
    // CogSocket 模式才显示每格的"单元格"按钮（不重建 iframe）
    const showCells = e.target.value === "cogsocket";
    document.querySelectorAll(".btn-cells").forEach((b) => {
      b.style.display = showCells ? "" : "none";
    });
  });

  // 轻度视觉检测开关：持久化 + 立即显隐主界面入口（TODO #13）
  document.getElementById("ckbx-vision-enabled").addEventListener("change", (e) => {
    appConfig.vision_inspection_enabled = e.target.checked;
    saveAppConfig();
    applyVisionEnabled(e.target.checked);
  });
  // 视觉检测工作台事件（按钮仅在功能启用后可见，监听只需绑定一次）
  initVisionModule();

  // CogSocket 单元格设置弹窗事件（仅绑定一次）
  initCogSocketCells();

  // 标签切换
  document.querySelectorAll(".tab-btn").forEach(btn => {
    btn.addEventListener("click", () => {
      document.querySelectorAll(".tab-btn").forEach(b => b.classList.remove("active"));
      document.querySelectorAll(".tab-panel").forEach(p => p.classList.remove("active"));
      btn.classList.add("active");
      document.getElementById(btn.dataset.tab).classList.add("active");
    });
  });

  // 相机显示设置
  document.getElementById("btn-display-create").addEventListener("click", async () => {
    const count = parseInt(document.getElementById("num-count").value);
    const delay = parseInt(document.getElementById("num-delay").value);
    await invoke("update_camera_config", { count, delay });
    cameraConfig = await invoke("get_camera_config");
    renderCameraGrid();
  });

  document.getElementById("btn-display-refresh").addEventListener("click", () => {
    renderCameraGrid();
  });

  // 文件转存设置
  document.getElementById("cmbx-transform-name").addEventListener("change", onTransformSelect);
  document.getElementById("btn-add-transform").addEventListener("click", showAddNameModal);
  document.getElementById("btn-delete-transform").addEventListener("click", deleteTransform);
  document.getElementById("btn-transform-save").addEventListener("click", saveTransform);

// 浏览按钮 - 转存监控路径
  document.getElementById("btn-watch-path-browse")?.addEventListener("click", async () => {
    try {
      const selected = await open({ directory: true, multiple: false });
      if (selected) {
        document.getElementById("txt-watch-path").value = selected;
        updateCurrentTransform();
      }
    } catch (e) {
      console.error("Open dialog failed:", e);
    }
  });

  // 浏览按钮 - 清理路径
  document.getElementById("btn-clean-path-browse")?.addEventListener("click", async () => {
    try {
      const selected = await open({ directory: true, multiple: false });
      if (selected) {
        document.getElementById("txt-clean-path").value = selected;
      }
    } catch (e) {
      console.error("Open dialog failed:", e);
    }
  });

  // 转存字段变更
  ["txt-watch-path", "txt-source-spliter", "num-folder-index", "num-file-name-index",
   "txt-folder-spliter", "txt-file-spliter"].forEach(id => {
    document.getElementById(id)?.addEventListener("change", updateCurrentTransform);
  });

  // 文件夹部分
  document.getElementById("btn-dir-part-add").addEventListener("click", () => showAddPartModal("dir"));
  document.getElementById("btn-dir-part-del").addEventListener("click", () => deletePart("dir"));
  document.getElementById("btn-dir-part-clear").addEventListener("click", () => clearParts("dir"));
  document.getElementById("num-dir-part-src").addEventListener("change", updateDirPart);
  document.getElementById("txt-dir-part-format").addEventListener("change", updateDirPart);

  // 文件名部分
  document.getElementById("btn-file-part-add").addEventListener("click", () => showAddPartModal("file"));
  document.getElementById("btn-file-part-del").addEventListener("click", () => deletePart("file"));
  document.getElementById("btn-file-part-clear").addEventListener("click", () => clearParts("file"));
  document.getElementById("num-file-part-src").addEventListener("change", updateFilePart);
  document.getElementById("txt-file-part-format").addEventListener("change", updateFilePart);

  // 清理设置
  document.getElementById("btn-clean-apply").addEventListener("click", applyCleanConfig);
  // 高级版：开关展开/折叠 Monaco 编辑器，载入默认脚本，测试脚本
  document.getElementById("ckbx-clean-advanced")?.addEventListener("change", (e) => {
    const body = document.getElementById("clean-advanced-body");
    if (body) body.style.display = e.target.checked ? "block" : "none";
    if (e.target.checked) initCleanMonaco();
  });
  document.getElementById("btn-clean-load-default")?.addEventListener("click", async () => {
    if (!cleanMonacoEditor) { initCleanMonaco(); }
    try {
      const script = await invoke("get_default_clean_script");
      cleanMonacoEditor?.setValue(script);
    } catch (err) {
      console.error("load default script failed:", err);
    }
  });
  document.getElementById("btn-clean-test-script")?.addEventListener("click", testCleanScript);

  // 压缩设置 - 浏览按钮
  document.getElementById("btn-compress-watch-browse")?.addEventListener("click", async () => {
    try {
      const selected = await open({ directory: true, multiple: false });
      if (selected) {
        document.getElementById("txt-compress-watch-path").value = selected;
      }
    } catch (e) {
      console.error("Open dialog failed:", e);
    }
  });
  document.getElementById("btn-compress-output-browse")?.addEventListener("click", async () => {
    try {
      const selected = await open({ directory: true, multiple: false });
      if (selected) {
        document.getElementById("txt-compress-output-path").value = selected;
      }
    } catch (e) {
      console.error("Open dialog failed:", e);
    }
  });
  document.getElementById("rng-compress-quality")?.addEventListener("input", (e) => {
    document.getElementById("lbl-compress-quality").textContent = e.target.value;
  });
  document.getElementById("rng-compress-speed")?.addEventListener("input", (e) => {
    document.getElementById("lbl-compress-speed").textContent = e.target.value;
  });
  document.getElementById("btn-compress-save").addEventListener("click", applyCompressConfig);
  document.getElementById("btn-compress-pause").addEventListener("click", toggleCompressPause);

  // FTP 服务器设置
  document.getElementById("btn-ftp-user-add").addEventListener("click", showAddFtpUserModal);
  document.getElementById("btn-ftp-user-del").addEventListener("click", deleteFtpUser);
  document.getElementById("btn-ftp-save").addEventListener("click", applyFtpConfig);
  document.getElementById("btn-ftp-user-ok").addEventListener("click", confirmAddFtpUser);
  document.getElementById("btn-ftp-user-cancel").addEventListener("click", () => hideModal("ftp-user-modal"));
  document.getElementById("btn-ftp-root-dir-browse")?.addEventListener("click", async () => {
    try {
      const selected = await open({ directory: true, multiple: false });
      if (selected) {
        document.getElementById("txt-ftp-root-dir").value = selected;
      }
    } catch (e) {
      console.error("Open dialog failed:", e);
    }
  });

  // NTP 时间服务器设置
  document.getElementById("btn-ntp-save").addEventListener("click", applyNtpConfig);
  document.getElementById("btn-ntp-test").addEventListener("click", testNtpServer);

  // JOBX 作业备份
  document.getElementById("btn-jobx-add").addEventListener("click", () => showJobxCameraModal(false));
  document.getElementById("btn-jobx-edit").addEventListener("click", () => showJobxCameraModal(true));
  document.getElementById("btn-jobx-delete").addEventListener("click", deleteJobxCamera);
  document.getElementById("btn-jobx-backup").addEventListener("click", backupJobxCamera);
  document.getElementById("btn-jobx-backup-all").addEventListener("click", backupAllJobxCameras);
  document.getElementById("btn-jobx-open-dir").addEventListener("click", openJobxBackupDir);
  document.getElementById("btn-jobx-ok").addEventListener("click", confirmJobxCamera);
  document.getElementById("btn-jobx-cancel").addEventListener("click", () => hideModal("jobx-camera-modal"));
  document.getElementById("btn-jobx-backup-dir-browse")?.addEventListener("click", async () => {
    try {
      const selected = await open({ directory: true, multiple: false });
      if (selected) {
        document.getElementById("txt-jobx-backup-dir").value = selected;
      }
    } catch (e) {
      console.error("Open dialog failed:", e);
    }
  });

  // 安全设置
  document.getElementById("btn-firewall-open").addEventListener("click", () => setFirewallStatus(true));
  document.getElementById("btn-firewall-close").addEventListener("click", () => setFirewallStatus(false));
  document.getElementById("btn-uac-open").addEventListener("click", () => setUacStatus(true));
  document.getElementById("btn-uac-close").addEventListener("click", () => setUacStatus(false));
  document.getElementById("btn-config-portable").addEventListener("click", () => switchConfigStorage(true));
  document.getElementById("btn-config-default").addEventListener("click", () => switchConfigStorage(false));

  // 显示设置
  document.getElementById("btn-mirror-refresh").addEventListener("click", refreshMirrorWindows);
  document.getElementById("btn-mirror-start").addEventListener("click", startMirror);
  document.getElementById("btn-mirror-stop").addEventListener("click", stopMirror);
  document.getElementById("rng-mirror-opacity").addEventListener("input", (e) => setMirrorOpacity(e.target.value));
  document.getElementById("ckbx-mirror-click-through").addEventListener("change", (e) => setMirrorClickThrough(e.target.checked));
  document.getElementById("btn-mirror-fit-original").addEventListener("click", () => setMirrorFit("original"));
  document.getElementById("btn-mirror-fit-half").addEventListener("click", () => setMirrorFit("half"));
  document.getElementById("btn-mirror-fit-quarter").addEventListener("click", () => setMirrorFit("quarter"));
  document.getElementById("btn-mirror-fit-stretch").addEventListener("click", () => setMirrorFit("stretch"));
  document.getElementById("btn-monitor-refresh").addEventListener("click", refreshMonitors);
  document.getElementById("btn-monitor-apply").addEventListener("click", applyMonitor);

  // 虚拟显示器
  document.getElementById("btn-vdd-install").addEventListener("click", installVirtualDisplayDriver);
  document.getElementById("btn-vdd-add").addEventListener("click", addVirtualDisplay);
  document.getElementById("btn-vdd-remove").addEventListener("click", removeVirtualDisplay);
  document.getElementById("btn-vdd-refresh").addEventListener("click", updateVirtualDisplayStatus);

  // 调试工具
  document.getElementById("btn-remote-cmd-start").addEventListener("click", startRemoteCommandServer);
  document.getElementById("btn-remote-cmd-stop").addEventListener("click", stopRemoteCommandServer);
  document.getElementById("btn-remote-cmd-refresh").addEventListener("click", updateRemoteCommandStatus);
  document.getElementById("btn-lan-subnet-detect").addEventListener("click", () => detectLanSubnet());
  document.getElementById("btn-lan-scan").addEventListener("click", scanLanDevices);

  // 添加名称弹窗
  document.getElementById("btn-add-name-ok").addEventListener("click", confirmAddName);
  document.getElementById("btn-add-name-cancel").addEventListener("click", () => hideModal("add-name-modal"));

  // 添加部分弹窗
  document.getElementById("btn-add-part-ok").addEventListener("click", confirmAddPart);
  document.getElementById("btn-add-part-cancel").addEventListener("click", () => hideModal("add-part-modal"));
}

// 设置加载代号：防止快速重复点击时旧加载提前关闭新加载的 splash 窗口
let settingLoadSeq = 0;

async function openSetting() {
  const seq = ++settingLoadSeq;
  const modal = document.getElementById("setting-modal");
  try {
    // 立即弹出独立加载窗口（splashscreen 技术：无边框置顶独立窗口），
    // 点击当帧即获得反馈，不依赖设置弹窗是否完成布局
    await invoke("show_settings_splash");
  } catch (e) {
    console.error("显示设置加载窗口失败：", e);
  }
  modal.classList.remove("hidden");
  updateAppSettingsUI();

  // 等加载窗口先绘制两帧，再执行数据加载（其中的耗时 invoke 不会推迟首帧反馈）
  await new Promise((resolve) =>
    requestAnimationFrame(() => requestAnimationFrame(resolve))
  );
  if (seq !== settingLoadSeq) return;

  try {
    await loadSettingData();
  } catch (e) {
    console.error("加载设置数据失败：", e);
  } finally {
    if (seq === settingLoadSeq) {
      try {
        await invoke("close_settings_splash");
      } catch (e) {
        console.error("关闭设置加载窗口失败：", e);
      }
    }
  }
}

function closeSetting() {
  document.getElementById("setting-modal").classList.add("hidden");
  settingLoadSeq++;
  // 幂等：加载中途关闭弹窗时，splash 窗口也必须关闭
  invoke("close_settings_splash").catch(() => {});
}

function hideModal(id) {
  document.getElementById(id).classList.add("hidden");
}

// ==================== 设置数据加载 ====================
async function loadSettingData() {
  // ---- 同步表单填充（来自启动时已加载到内存的配置，瞬时完成） ----
  // 相机显示
  document.getElementById("num-count").value = cameraConfig.count;
  document.getElementById("num-delay").value = cameraConfig.delay;

  // 转存配置列表
  const cmbx = document.getElementById("cmbx-transform-name");
  cmbx.innerHTML = "";
  transformConfigs.forEach((cfg, idx) => {
    const opt = document.createElement("option");
    opt.value = idx;
    opt.textContent = cfg.name;
    cmbx.appendChild(opt);
  });
  if (transformConfigs.length > 0) {
    cmbx.selectedIndex = 0;
    currentTransformIndex = 0;
    loadTransformDetail(transformConfigs[0]);
  }

  // 压缩配置
  document.getElementById("ckbx-compress-enable").checked = compressConfig.is_enable || false;
  document.getElementById("txt-compress-watch-path").value = compressConfig.watch_path || "";
  document.getElementById("txt-compress-output-path").value = compressConfig.output_path || "";
  const quality = compressConfig.quality || 80;
  document.getElementById("rng-compress-quality").value = quality;
  document.getElementById("lbl-compress-quality").textContent = quality;
  const speed = compressConfig.speed || 4;
  document.getElementById("rng-compress-speed").value = speed;
  document.getElementById("lbl-compress-speed").textContent = speed;
  document.getElementById("ckbx-compress-delete-source").checked = compressConfig.delete_source || false;

  // NTP / FTP / JOBX 表单填充
  loadNtpSettingData();
  loadFtpSettingData();
  loadJobxSettingData();

  // 清理配置
  document.getElementById("ckbx-clean-enable").checked = cleanConfig.is_enable || false;
  document.getElementById("num-clean-scan-interval").value = cleanConfig.scan_interval || 2;
  document.getElementById("ckbx-clean-empty-folder").checked = cleanConfig.is_clean_empty_folders !== false;
  document.getElementById("rbtn-hold-day").checked = cleanConfig.is_enable_hold_days || false;
  document.getElementById("num-clean-hold-day").value = cleanConfig.hold_days || 30;
  document.getElementById("rbtn-remaining").checked = cleanConfig.is_enable_remaining_space !== false;
  document.getElementById("num-clean-remaining").value = cleanConfig.remaining_space || 5;
  document.getElementById("num-clean-interval").value = cleanConfig.delete_interval || 5;
  // 高级版字段回显
  const isAdvanced = cleanConfig.is_advanced || false;
  document.getElementById("ckbx-clean-advanced").checked = isAdvanced;
  document.getElementById("ckbx-clean-image-count").checked = cleanConfig.is_enable_image_count || false;
  document.getElementById("num-clean-image-count").value = cleanConfig.image_count_threshold || 3000;
  const advBody = document.getElementById("clean-advanced-body");
  if (advBody) advBody.style.display = isAdvanced ? "block" : "none";
  // 高级版开启时按需初始化 Monaco 并回填脚本
  if (isAdvanced) {
    initCleanMonaco().then(() => {
      cleanMonacoEditor?.setValue(cleanConfig.delete_script || "");
    });
  }

  // ---- 异步状态查询：并行发起，全部完成后再隐藏加载遮罩 ----
  // （allSettled：单个查询失败不影响其他面板与遮罩关闭）
  await Promise.allSettled([
    updateCompressStatus(),
    updateNtpStatus(),
    updateFtpStatus(),
    updateJobxLogs(),
    updateFirewallStatus(),
    updateUacStatus(),
    updateConfigStorageInfo(),
    loadDisplaySettingData(),
    loadDebugToolsData(),
  ]);
}

function onTransformSelect() {
  const idx = parseInt(document.getElementById("cmbx-transform-name").value);
  currentTransformIndex = idx;
  if (transformConfigs[idx]) {
    loadTransformDetail(transformConfigs[idx]);
  }
}

function loadTransformDetail(cfg) {
  document.getElementById("txt-watch-path").value = cfg.watch_path || "";
  document.getElementById("txt-source-spliter").value = cfg.source_spliter || ",";
  document.getElementById("num-folder-index").value = cfg.folder_index || 1;
  document.getElementById("num-file-name-index").value = cfg.file_name_index || 1;
  document.getElementById("txt-folder-spliter").value = cfg.folder_spliter || "+";
  document.getElementById("txt-file-spliter").value = cfg.file_name_spliter || "_";
  renderDirParts(cfg.folder_part_list || []);
  renderFileParts(cfg.file_name_part_list || []);
}

function renderDirParts(parts) {
  const tbody = document.querySelector("#dgv-dir-parts tbody");
  tbody.innerHTML = "";
  parts.forEach((part, idx) => {
    const tr = document.createElement("tr");
    tr.dataset.index = idx;
    const typeText = part.part_type === "DateTime" ? t("pcTime") : t("name");
    tr.innerHTML = `<td>${idx}</td><td>${typeText}</td>`;
    tr.addEventListener("click", () => selectDirPart(idx));
    tbody.appendChild(tr);
  });
  currentDirPartIndex = -1;
  document.getElementById("pnl-dir-part-src").classList.add("hidden");
  document.getElementById("pnl-dir-part-format").classList.add("hidden");
}

function renderFileParts(parts) {
  const tbody = document.querySelector("#dgv-file-parts tbody");
  tbody.innerHTML = "";
  parts.forEach((part, idx) => {
    const tr = document.createElement("tr");
    tr.dataset.index = idx;
    const typeText = part.part_type === "DateTime" ? t("pcTime") : t("name");
    tr.innerHTML = `<td>${idx}</td><td>${typeText}</td>`;
    tr.addEventListener("click", () => selectFilePart(idx));
    tbody.appendChild(tr);
  });
  currentFilePartIndex = -1;
  document.getElementById("pnl-file-part-src").classList.add("hidden");
  document.getElementById("pnl-file-part-format").classList.add("hidden");
}

function selectDirPart(idx) {
  currentDirPartIndex = idx;
  document.querySelectorAll("#dgv-dir-parts tbody tr").forEach(tr => tr.classList.remove("selected"));
  const tr = document.querySelector(`#dgv-dir-parts tbody tr[data-index="${idx}"]`);
  if (tr) tr.classList.add("selected");

  const cfg = transformConfigs[currentTransformIndex];
  const part = cfg?.folder_part_list?.[idx];
  if (!part) return;

  if (part.part_type === "Name") {
    document.getElementById("pnl-dir-part-src").classList.remove("hidden");
    document.getElementById("pnl-dir-part-format").classList.add("hidden");
    document.getElementById("num-dir-part-src").value = part.source_index || 0;
  } else {
    document.getElementById("pnl-dir-part-src").classList.add("hidden");
    document.getElementById("pnl-dir-part-format").classList.remove("hidden");
    document.getElementById("txt-dir-part-format").value = part.format || "";
  }
}

function selectFilePart(idx) {
  currentFilePartIndex = idx;
  document.querySelectorAll("#dgv-file-parts tbody tr").forEach(tr => tr.classList.remove("selected"));
  const tr = document.querySelector(`#dgv-file-parts tbody tr[data-index="${idx}"]`);
  if (tr) tr.classList.add("selected");

  const cfg = transformConfigs[currentTransformIndex];
  const part = cfg?.file_name_part_list?.[idx];
  if (!part) return;

  if (part.part_type === "Name") {
    document.getElementById("pnl-file-part-src").classList.remove("hidden");
    document.getElementById("pnl-file-part-format").classList.add("hidden");
    document.getElementById("num-file-part-src").value = part.source_index || 0;
  } else {
    document.getElementById("pnl-file-part-src").classList.add("hidden");
    document.getElementById("pnl-file-part-format").classList.remove("hidden");
    document.getElementById("txt-file-part-format").value = part.format || "";
  }
}

// ==================== 转存操作 ====================
function updateCurrentTransform() {
  if (currentTransformIndex < 0) return;
  const cfg = transformConfigs[currentTransformIndex];
  if (!cfg) return;

  cfg.watch_path = document.getElementById("txt-watch-path").value;
  cfg.source_spliter = document.getElementById("txt-source-spliter").value;
  cfg.folder_index = parseInt(document.getElementById("num-folder-index").value) || 0;
  cfg.file_name_index = parseInt(document.getElementById("num-file-name-index").value) || 0;
  cfg.folder_spliter = document.getElementById("txt-folder-spliter").value;
  cfg.file_name_spliter = document.getElementById("txt-file-spliter").value;
}

function updateDirPart() {
  if (currentTransformIndex < 0 || currentDirPartIndex < 0) return;
  const cfg = transformConfigs[currentTransformIndex];
  const part = cfg?.folder_part_list?.[currentDirPartIndex];
  if (!part) return;

  if (part.part_type === "Name") {
    part.source_index = parseInt(document.getElementById("num-dir-part-src").value) || 0;
  } else {
    part.format = document.getElementById("txt-dir-part-format").value;
  }
}

function updateFilePart() {
  if (currentTransformIndex < 0 || currentFilePartIndex < 0) return;
  const cfg = transformConfigs[currentTransformIndex];
  const part = cfg?.file_name_part_list?.[currentFilePartIndex];
  if (!part) return;

  if (part.part_type === "Name") {
    part.source_index = parseInt(document.getElementById("num-file-part-src").value) || 0;
  } else {
    part.format = document.getElementById("txt-file-part-format").value;
  }
}

let pendingPartType = "";
let pendingPartTarget = "";

function showAddPartModal(target) {
  pendingPartTarget = target;
  document.getElementById("add-part-modal").classList.remove("hidden");
}

function confirmAddPart() {
  const type = document.getElementById("cmbx-part-type").value;
  if (currentTransformIndex < 0) return;
  const cfg = transformConfigs[currentTransformIndex];

  const newPart = {
    part_type: type,
    source_index: 0,
    format: type === "DateTime" ? "yyyyMMdd" : ""
  };

  if (pendingPartTarget === "dir") {
    cfg.folder_part_list = cfg.folder_part_list || [];
    cfg.folder_part_list.push(newPart);
    renderDirParts(cfg.folder_part_list);
  } else {
    cfg.file_name_part_list = cfg.file_name_part_list || [];
    cfg.file_name_part_list.push(newPart);
    renderFileParts(cfg.file_name_part_list);
  }
  hideModal("add-part-modal");
}

function deletePart(target) {
  if (currentTransformIndex < 0) return;
  const cfg = transformConfigs[currentTransformIndex];

  if (target === "dir" && currentDirPartIndex >= 0) {
    cfg.folder_part_list.splice(currentDirPartIndex, 1);
    renderDirParts(cfg.folder_part_list);
  } else if (target === "file" && currentFilePartIndex >= 0) {
    cfg.file_name_part_list.splice(currentFilePartIndex, 1);
    renderFileParts(cfg.file_name_part_list);
  }
}

function clearParts(target) {
  if (currentTransformIndex < 0) return;
  const cfg = transformConfigs[currentTransformIndex];

  if (target === "dir") {
    cfg.folder_part_list = [];
    renderDirParts([]);
  } else {
    cfg.file_name_part_list = [];
    renderFileParts([]);
  }
}

// ==================== 添加/删除转存配置 ====================
function showAddNameModal() {
  document.getElementById("add-name-modal").classList.remove("hidden");
  document.getElementById("txt-add-name").value = "";
}

function confirmAddName() {
  const name = document.getElementById("txt-add-name").value.trim();
  if (!name) return;

  const newConfig = {
    name,
    watch_path: "",
    source_spliter: ",",
    folder_index: 1,
    file_name_index: 1,
    folder_spliter: "+",
    folder_part_list: [],
    file_name_spliter: "_",
    file_name_part_list: []
  };

  transformConfigs.push(newConfig);
  const cmbx = document.getElementById("cmbx-transform-name");
  const opt = document.createElement("option");
  opt.value = transformConfigs.length - 1;
  opt.textContent = name;
  cmbx.appendChild(opt);
  cmbx.selectedIndex = transformConfigs.length - 1;
  currentTransformIndex = transformConfigs.length - 1;
  loadTransformDetail(newConfig);
  hideModal("add-name-modal");
}

function deleteTransform() {
  if (currentTransformIndex < 0) return;
  transformConfigs.splice(currentTransformIndex, 1);

  const cmbx = document.getElementById("cmbx-transform-name");
  cmbx.innerHTML = "";
  transformConfigs.forEach((cfg, idx) => {
    const opt = document.createElement("option");
    opt.value = idx;
    opt.textContent = cfg.name;
    cmbx.appendChild(opt);
  });

  if (transformConfigs.length > 0) {
    cmbx.selectedIndex = 0;
    currentTransformIndex = 0;
    loadTransformDetail(transformConfigs[0]);
  } else {
    currentTransformIndex = -1;
  }
}

async function saveTransform() {
  updateCurrentTransform();
  await invoke("set_transform_configs", { configs: transformConfigs });
  await invoke("stop_file_watchers");
  await invoke("start_file_watchers");
}

// ==================== 清理配置 ====================
// 按需初始化 Monaco 编辑器（AMD loader，仅勾选高级版时加载）
// 返回 Promise，解析为已就绪的 monaco 全局
let monacoInitPromise = null;
function initCleanMonaco() {
  if (monacoInitPromise) return monacoInitPromise;
  monacoInitPromise = new Promise((resolve) => {
    const amdRequire = window.require;
    if (!amdRequire || monacoReady) { resolve(window.monaco); return; }
    amdRequire.config({ paths: { vs: "assets/monaco-editor/vs" } });
    amdRequire(["vs/editor/editor.main"], () => {
      monacoReady = true;
      const theme = document.documentElement.getAttribute("data-theme") || "light";
      try {
        window.monaco.editor.defineTheme("clean-dark", {
          base: "vs-dark",
          inherit: true,
          rules: [],
          colors: {},
        });
      } catch (_) {}
      const wrap = document.getElementById("clean-editor-wrap");
      if (wrap && !cleanMonacoEditor) {
        cleanMonacoEditor = window.monaco.editor.create(wrap, {
          value: "",
          language: "javascript",
          theme: theme === "dark" ? "vs-dark" : "vs",
          automaticLayout: true,
          minimap: { enabled: false },
          scrollBeyondLastLine: false,
          fontSize: 13,
          lineNumbers: "on",
          wordWrap: "on",
          tabSize: 2,
        });
      }
      resolve(window.monaco);
    });
  });
  return monacoInitPromise;
}

// 测试脚本：调后端 test_clean_script，展示判定结果 + console 输出 + 错误
async function testCleanScript() {
  const resultEl = document.getElementById("clean-test-result");
  if (resultEl) resultEl.textContent = t("cleanScriptRunning") || "执行中…";
  try {
    const script = cleanMonacoEditor ? cleanMonacoEditor.getValue() : "";
    const ctx = await invoke("sample_clean_context");
    const result = await invoke("test_clean_script", { script, ctx });
    let out = "";
    if (result.error) {
      out += `[ERROR] ${result.error}\n`;
    }
    if (result.decision === true) {
      out += `${t("decisionResult") || "判定结果"}: ${t("decisionDelete") || "删除"}\n`;
    } else if (result.decision === false) {
      out += `${t("decisionResult") || "判定结果"}: ${t("decisionKeep") || "保留"}\n`;
    } else {
      out += `${t("decisionResult") || "判定结果"}: ${t("decisionNone") || "未返回布尔值"}\n`;
    }
    if (result.console && result.console.length) {
      out += `\n--- console ---\n${result.console.join("\n")}\n`;
    }
    if (resultEl) resultEl.textContent = out;
  } catch (err) {
    if (resultEl) resultEl.textContent = `${t("cleanScriptError") || "脚本执行出错"}: ${err}`;
  }
}

async function applyCleanConfig() {
  cleanConfig = {
    is_enable: document.getElementById("ckbx-clean-enable").checked,
    folder_path: document.getElementById("txt-clean-path").value,
    scan_interval: parseFloat(document.getElementById("num-clean-scan-interval").value) || 2,
    is_clean_empty_folders: document.getElementById("ckbx-clean-empty-folder").checked,
    is_enable_hold_days: document.getElementById("rbtn-hold-day").checked,
    hold_days: parseInt(document.getElementById("num-clean-hold-day").value) || 30,
    is_enable_remaining_space: document.getElementById("rbtn-remaining").checked,
    remaining_space: parseFloat(document.getElementById("num-clean-remaining").value) || 5,
    delete_interval: parseInt(document.getElementById("num-clean-interval").value) || 5,
    // 高级版字段：脚本判定与数量条件
    is_advanced: document.getElementById("ckbx-clean-advanced").checked,
    delete_script: cleanMonacoEditor ? cleanMonacoEditor.getValue() : "",
    is_enable_image_count: document.getElementById("ckbx-clean-image-count").checked,
    image_count_threshold: parseInt(document.getElementById("num-clean-image-count").value) || 3000,
  };

  await invoke("stop_cleaner");
  await invoke("set_clean_config", { config: cleanConfig });
  await invoke("start_cleaner");
}

// ==================== 压缩配置 ====================
async function applyCompressConfig() {
  compressConfig = {
    is_enable: document.getElementById("ckbx-compress-enable").checked,
    watch_path: document.getElementById("txt-compress-watch-path").value,
    output_path: document.getElementById("txt-compress-output-path").value,
    quality: parseInt(document.getElementById("rng-compress-quality").value) || 80,
    speed: parseInt(document.getElementById("rng-compress-speed").value) || 4,
    delete_source: document.getElementById("ckbx-compress-delete-source").checked,
  };

  await invoke("stop_compress_watcher");
  await invoke("set_compress_config", { config: compressConfig });
  await invoke("start_compress_watcher");
  updateCompressStatus();
}

async function toggleCompressPause() {
  compressPaused = !compressPaused;
  const btn = document.getElementById("btn-compress-pause");
  if (compressPaused) {
    await invoke("pause_compress");
    btn.textContent = t("resumeCompress");
    btn.dataset.i18n = "resumeCompress";
  } else {
    await invoke("resume_compress");
    btn.textContent = t("pauseCompress");
    btn.dataset.i18n = "pauseCompress";
  }
  updateCompressStatus();
}

async function updateCompressStatus() {
  try {
    const status = await invoke("get_compress_status");
    const statusPanel = document.getElementById("compress-status");
    if (status && status.is_running) {
      statusPanel.classList.remove("hidden");
      document.getElementById("lbl-compress-state").textContent = status.is_paused ? t("paused") : t("running");
      document.getElementById("lbl-compress-total").textContent = status.total_files || 0;
      document.getElementById("lbl-compress-done").textContent = status.completed_files || 0;
      const total = status.total_files || 1;
      const done = status.completed_files || 0;
      const pct = Math.min(100, Math.round((done / total) * 100));
      document.getElementById("compress-progress").style.width = pct + "%";
    } else {
      statusPanel.classList.add("hidden");
    }
  } catch (e) {
    // compress status not available yet
  }
}

// ==================== NTP 时间服务器配置 ====================
function loadNtpSettingData() {
  document.getElementById("ckbx-ntp-enable").checked = ntpConfig.is_enable || false;
  document.getElementById("num-ntp-port").value = ntpConfig.port || 123;
}

async function applyNtpConfig() {
  ntpConfig = {
    is_enable: document.getElementById("ckbx-ntp-enable").checked,
    port: parseInt(document.getElementById("num-ntp-port").value) || 123,
  };

  await invoke("stop_ntp_server");
  await invoke("set_ntp_config", { config: ntpConfig });
  await invoke("start_ntp_server");
  updateNtpStatus();
}

async function testNtpServer() {
  const port = parseInt(document.getElementById("num-ntp-port").value) || 123;
  const host_port = port === 123 ? "127.0.0.1" : `127.0.0.1,${port}`;
  const resultEl = document.getElementById("lbl-ntp-test-result");
  resultEl.textContent = t("running");
  try {
    const result = await invoke("test_ntp_server", { hostPort: host_port });
    resultEl.textContent = result || t("noResult");
  } catch (e) {
    resultEl.textContent = `${t("testFailed")}: ${e}`;
  }
}

async function updateNtpStatus() {
  try {
    const status = await invoke("get_ntp_status");
    const statusEl = document.getElementById("lbl-ntp-status");
    if (status && status.is_running) {
      statusEl.textContent = t("ntpStarted");
      statusEl.dataset.i18n = "ntpStarted";
    } else {
      statusEl.textContent = t("ntpStopped");
      statusEl.dataset.i18n = "ntpStopped";
    }
  } catch (e) {
    // ntp status not available yet
  }
}

async function updateNtpLogs() {
  try {
    const logs = await invoke("get_ntp_logs");
    const terminal = document.getElementById("ntp-terminal");
    if (!terminal) return;
    terminal.innerHTML = "";
    logs.forEach(entry => {
      const line = document.createElement("div");
      line.className = "log-line";
      line.textContent = `[${entry.timestamp}] [${entry.level}] ${entry.message}`;
      terminal.appendChild(line);
    });
    terminal.scrollTop = terminal.scrollHeight;
  } catch (e) {
    // ntp logs not available yet
  }
}

// 启动 NTP 状态和日志刷新
setInterval(() => {
  updateNtpStatus();
  updateNtpLogs();
  updateFtpStatus();
  updateFtpLogs();
  updateJobxLogs();
  updateRemoteCommandStatus();
}, 2000);

// ==================== FTP 服务器配置 ====================
function loadFtpSettingData() {
  document.getElementById("ckbx-ftp-enable").checked = ftpConfig.is_enable || false;
  document.getElementById("num-ftp-port").value = ftpConfig.port || 21;
  document.getElementById("ckbx-ftp-tls").checked = ftpConfig.use_tls || false;
  renderFtpUsers();
}

function renderFtpUsers() {
  const tbody = document.querySelector("#dgv-ftp-users tbody");
  if (!tbody) return;
  tbody.innerHTML = "";
  const users = ftpConfig.users || [];
  users.forEach((user, index) => {
    const tr = document.createElement("tr");
    tr.dataset.index = index;

    const tdUsername = document.createElement("td");
    tdUsername.textContent = user.username || "";
    const tdPassword = document.createElement("td");
    tdPassword.textContent = user.password || "";
    const tdRootDir = document.createElement("td");
    tdRootDir.textContent = user.root_dir || "";

    tr.appendChild(tdUsername);
    tr.appendChild(tdPassword);
    tr.appendChild(tdRootDir);

    tr.addEventListener("click", () => selectFtpUserRow(tr));
    tbody.appendChild(tr);
  });
}

function selectFtpUserRow(row) {
  document.querySelectorAll("#dgv-ftp-users tbody tr").forEach(r => r.classList.remove("selected"));
  row.classList.add("selected");
}

function showAddFtpUserModal() {
  document.getElementById("txt-ftp-username").value = "";
  document.getElementById("txt-ftp-password").value = "";
  document.getElementById("txt-ftp-root-dir").value = "";
  document.getElementById("ftp-user-modal").classList.remove("hidden");
}

function confirmAddFtpUser() {
  const username = document.getElementById("txt-ftp-username").value.trim();
  const password = document.getElementById("txt-ftp-password").value.trim();
  const rootDir = document.getElementById("txt-ftp-root-dir").value.trim();

  if (!username) {
    alert(t("username") + " 不能为空");
    return;
  }
  if (!rootDir) {
    alert(t("rootDir") + " 不能为空");
    return;
  }

  if (!ftpConfig.users) {
    ftpConfig.users = [];
  }
  ftpConfig.users.push({ username, password, root_dir: rootDir });
  renderFtpUsers();
  hideModal("ftp-user-modal");
}

function deleteFtpUser() {
  const selected = document.querySelector("#dgv-ftp-users tbody tr.selected");
  if (!selected) return;
  const index = parseInt(selected.dataset.index);
  if (!Array.isArray(ftpConfig.users)) return;
  ftpConfig.users.splice(index, 1);
  renderFtpUsers();
}

async function applyFtpConfig() {
  ftpConfig = {
    is_enable: document.getElementById("ckbx-ftp-enable").checked,
    port: parseInt(document.getElementById("num-ftp-port").value) || 21,
    use_tls: document.getElementById("ckbx-ftp-tls").checked,
    users: ftpConfig.users || [],
  };

  await invoke("stop_ftp_server");
  await invoke("set_ftp_config", { config: ftpConfig });
  await invoke("start_ftp_server");
  updateFtpStatus();
}

async function updateFtpStatus() {
  try {
    const status = await invoke("get_ftp_status");
    const statusEl = document.getElementById("lbl-ftp-status");
    if (!statusEl) return;
    if (status && status.is_running) {
      statusEl.textContent = t("ftpStarted");
      statusEl.dataset.i18n = "ftpStarted";
    } else {
      statusEl.textContent = t("ftpStopped");
      statusEl.dataset.i18n = "ftpStopped";
    }
  } catch (e) {
    // ftp status not available yet
  }
}

async function updateFtpLogs() {
  try {
    const logs = await invoke("get_ftp_logs");
    const terminal = document.getElementById("ftp-terminal");
    if (!terminal) return;
    terminal.innerHTML = "";
    logs.forEach(entry => {
      const line = document.createElement("div");
      line.className = "log-line";
      line.textContent = `[${entry.timestamp}] [${entry.level}] ${entry.message}`;
      terminal.appendChild(line);
    });
    terminal.scrollTop = terminal.scrollHeight;
  } catch (e) {
    // ftp logs not available yet
  }
}

// ==================== JOBX 作业备份 ====================
function loadJobxSettingData() {
  renderJobxCameras();
}

function renderJobxCameras() {
  const tbody = document.querySelector("#dgv-jobx-cameras tbody");
  if (!tbody) return;
  tbody.innerHTML = "";
  const cameras = jobxBackupConfig.cameras || [];
  cameras.forEach((cam, index) => {
    const tr = document.createElement("tr");
    tr.dataset.index = index;
    tr.innerHTML = `
      <td>${cam.name || ""}</td>
      <td>${cam.ip || ""}</td>
      <td>${cam.ftp_port || 21}</td>
      <td>${cam.ftp_username || ""}</td>
      <td>${cam.ftps_enabled ? t("yes") : t("no")}</td>
      <td>${cam.backup_directory || ""}</td>
    `;
    tr.addEventListener("click", () => selectJobxRow(tr));
    tbody.appendChild(tr);
  });
  currentJobxIndex = -1;
}

function selectJobxRow(row) {
  document.querySelectorAll("#dgv-jobx-cameras tbody tr").forEach(r => r.classList.remove("selected"));
  row.classList.add("selected");
  currentJobxIndex = parseInt(row.dataset.index);
}

function showJobxCameraModal(isEdit) {
  pendingJobxEdit = isEdit;
  const title = document.getElementById("jobx-modal-title");
  if (isEdit) {
    if (currentJobxIndex < 0) {
      alert(t("noCameraSelected"));
      return;
    }
    const cam = jobxBackupConfig.cameras[currentJobxIndex];
    title.textContent = t("jobxCameraEditTitle");
    title.dataset.i18n = "jobxCameraEditTitle";
    document.getElementById("txt-jobx-name").value = cam.name || "";
    document.getElementById("txt-jobx-ip").value = cam.ip || "";
    document.getElementById("num-jobx-port").value = cam.ftp_port || 21;
    document.getElementById("txt-jobx-username").value = cam.ftp_username || "admin";
    document.getElementById("txt-jobx-password").value = cam.ftp_password || "";
    document.getElementById("txt-jobx-backup-dir").value = cam.backup_directory || "";
    document.getElementById("ckbx-jobx-ftps").checked = cam.ftps_enabled || false;
    document.getElementById("ckbx-jobx-trust").checked = cam.trust_all_certs !== false;
  } else {
    title.textContent = t("jobxCameraAddTitle");
    title.dataset.i18n = "jobxCameraAddTitle";
    document.getElementById("txt-jobx-name").value = "";
    document.getElementById("txt-jobx-ip").value = "";
    document.getElementById("num-jobx-port").value = 21;
    document.getElementById("txt-jobx-username").value = "admin";
    document.getElementById("txt-jobx-password").value = "";
    document.getElementById("txt-jobx-backup-dir").value = "";
    document.getElementById("ckbx-jobx-ftps").checked = true;
    document.getElementById("ckbx-jobx-trust").checked = true;
  }
  document.getElementById("jobx-camera-modal").classList.remove("hidden");
}

async function confirmJobxCamera() {
  const camera = {
    name: document.getElementById("txt-jobx-name").value.trim(),
    ip: document.getElementById("txt-jobx-ip").value.trim(),
    ftp_port: parseInt(document.getElementById("num-jobx-port").value) || 21,
    ftp_username: document.getElementById("txt-jobx-username").value.trim(),
    ftp_password: document.getElementById("txt-jobx-password").value,
    backup_directory: document.getElementById("txt-jobx-backup-dir").value.trim(),
    ftps_enabled: document.getElementById("ckbx-jobx-ftps").checked,
    trust_all_certs: document.getElementById("ckbx-jobx-trust").checked,
  };

  if (!camera.name || !camera.ip) {
    alert(t("cameraName") + " / IP " + t("requiredHint"));
    return;
  }

  if (pendingJobxEdit && currentJobxIndex >= 0) {
    await invoke("update_jobx_camera", { index: currentJobxIndex, camera });
  } else {
    await invoke("add_jobx_camera", { camera });
  }

  jobxBackupConfig = await invoke("get_jobx_backup_config");
  renderJobxCameras();
  hideModal("jobx-camera-modal");
}

async function deleteJobxCamera() {
  if (currentJobxIndex < 0) {
    alert(t("noCameraSelected"));
    return;
  }
  if (!confirm(t("confirmDeleteCamera"))) return;
  await invoke("delete_jobx_camera", { index: currentJobxIndex });
  jobxBackupConfig = await invoke("get_jobx_backup_config");
  renderJobxCameras();
}

async function backupJobxCamera() {
  if (currentJobxIndex < 0) {
    alert(t("noCameraSelected"));
    return;
  }
  try {
    const result = await invoke("backup_jobx_camera", { index: currentJobxIndex });
    if (result.success) {
      alert(`${t("backupSuccess")}!\n${result.message}\n${result.backup_path || ""}`);
    } else {
      alert(`${t("backupFailed")}: ${result.message}`);
    }
  } catch (e) {
    alert(`${t("backupFailed")}: ${e}`);
  }
}

async function backupAllJobxCameras() {
  try {
    await invoke("backup_all_jobx_cameras");
    alert(t("backupAll") + " " + t("running"));
  } catch (e) {
    alert(`${t("backupFailed")}: ${e}`);
  }
}

async function openJobxBackupDir() {
  let path = "";
  if (currentJobxIndex >= 0) {
    const cam = jobxBackupConfig.cameras[currentJobxIndex];
    path = cam?.backup_directory || "";
  }
  try {
    await invoke("open_jobx_backup_dir", { path });
  } catch (e) {
    alert(`${t("openBackupDir")} ${t("testFailed")}: ${e}`);
  }
}

async function updateJobxLogs() {
  try {
    const logs = await invoke("get_jobx_backup_logs");
    const terminal = document.getElementById("jobx-terminal");
    if (!terminal) return;
    terminal.innerHTML = "";
    logs.forEach(entry => {
      const line = document.createElement("div");
      line.className = "log-line";
      line.textContent = `[${entry.timestamp}] [${entry.level}] ${entry.message}`;
      terminal.appendChild(line);
    });
    terminal.scrollTop = terminal.scrollHeight;
  } catch (e) {
    // jobx logs not available yet
  }
}

// ==================== 安全设置 ====================
async function updateFirewallStatus() {
  try {
    const enabled = await invoke("get_firewall_status");
    const statusEl = document.getElementById("lbl-firewall-status");
    if (enabled) {
      statusEl.textContent = t("firewallStatusOn");
      statusEl.dataset.i18n = "firewallStatusOn";
    } else {
      statusEl.textContent = t("firewallStatusOff");
      statusEl.dataset.i18n = "firewallStatusOff";
    }
  } catch (e) {
    console.error("get firewall status failed:", e);
    document.getElementById("lbl-firewall-status").textContent = "--";
  }
}

async function setFirewallStatus(enabled) {
  try {
    const result = await invoke("set_firewall_status", { enabled });
    alert(result);
    await updateFirewallStatus();
  } catch (e) {
    alert(`${t("firewallOperationFailed")}: ${e}`);
  }
}

async function updateUacStatus() {
  try {
    const enabled = await invoke("get_uac_status");
    const statusEl = document.getElementById("lbl-uac-status");
    if (enabled) {
      statusEl.textContent = t("uacStatusOn");
      statusEl.dataset.i18n = "uacStatusOn";
    } else {
      statusEl.textContent = t("uacStatusOff");
      statusEl.dataset.i18n = "uacStatusOff";
    }
  } catch (e) {
    console.error("get uac status failed:", e);
    document.getElementById("lbl-uac-status").textContent = "--";
  }
}

async function setUacStatus(enabled) {
  try {
    const result = await invoke("set_uac_status", { enabled });
    alert(result);
    await updateUacStatus();
  } catch (e) {
    alert(`${t("uacOperationFailed")}: ${e}`);
  }
}

// ==================== 配置文件位置 ====================
async function updateConfigStorageInfo() {
  const modeEl = document.getElementById("lbl-config-storage-mode");
  const pathEl = document.getElementById("lbl-config-storage-path");
  const hintEl = document.getElementById("lbl-config-storage-hint");
  const btnPortable = document.getElementById("btn-config-portable");
  const btnDefault = document.getElementById("btn-config-default");
  try {
    const info = await invoke("get_config_storage_info");
    pathEl.textContent = info.configDir || "--";

    const modeKey = {
      default: "configModeDefault",
      portable: "configModePortable",
      custom: "configModeCustom",
    }[info.mode] || info.mode;
    modeEl.textContent = t(modeKey);

    // 标记文件状态与当前模式不一致 = 已切换但尚未重启
    const markerPortable = info.markerExists;
    const pending =
      (info.mode === "default" && markerPortable) ||
      (info.mode === "portable" && !markerPortable);

    if (pending) {
      hintEl.textContent = t("configRestartPending");
    } else if (info.mode === "portable") {
      hintEl.textContent = t("configHintPortable");
    } else if (info.mode === "custom") {
      hintEl.textContent = t("configHintCustom");
    } else {
      hintEl.textContent = t("configHintDefault");
    }

    btnPortable.disabled = info.mode === "custom" || info.mode === "portable" || pending;
    btnDefault.disabled = info.mode === "custom" || info.mode === "default" || pending;
  } catch (e) {
    console.error("get config storage info failed:", e);
    modeEl.textContent = "--";
    pathEl.textContent = "--";
    hintEl.textContent = "";
  }
}

async function switchConfigStorage(portable) {
  const migrate = confirm(t("configMigrateConfirm"));
  try {
    const result = await invoke("switch_config_storage", { portable, migrate });
    await updateConfigStorageInfo();
    if (confirm(`${result}\n\n${t("configRestartNow")}`)) {
      await invoke("restart_app");
    }
  } catch (e) {
    alert(`${t("configSwitchFailed")}: ${e}`);
  }
}

// ==================== 显示设置 ====================
async function loadDisplaySettingData() {
  await refreshMirrorWindows();
  await refreshMonitors();
  await updateVirtualDisplayStatus();
}

async function refreshMirrorWindows() {
  try {
    const windows = await invoke("list_windows");
    const cmbx = document.getElementById("cmbx-mirror-window");
    cmbx.innerHTML = '<option value="">' + t("selectWindow") + "</option>";
    windows.forEach((w) => {
      const opt = document.createElement("option");
      opt.value = String(w.handle);
      const title = w.title || w.class_name || "(无标题)";
      opt.textContent = `${title} [0x${w.handle.toString(16)}]`;
      cmbx.appendChild(opt);
    });
  } catch (e) {
    console.error("list windows failed:", e);
  }
}

async function startMirror() {
  const cmbx = document.getElementById("cmbx-mirror-window");
  const handle = cmbx.value ? parseInt(cmbx.value, 10) : null;
  if (!handle) {
    alert(t("selectWindowHint"));
    return;
  }
  try {
    await invoke("set_mirror_window", { handle });
  } catch (e) {
    alert(`${t("mirrorOperationFailed")}: ${e}`);
  }
}

async function stopMirror() {
  try {
    await invoke("stop_mirror_window");
  } catch (e) {
    alert(`${t("mirrorOperationFailed")}: ${e}`);
  }
}

async function setMirrorOpacity(value) {
  const opacity = parseInt(value, 10) / 100;
  document.getElementById("lbl-mirror-opacity").textContent = value;
  try {
    await invoke("set_mirror_opacity", { opacity });
  } catch (e) {
    console.error("set mirror opacity failed:", e);
  }
}

async function setMirrorClickThrough(enabled) {
  try {
    await invoke("set_mirror_click_through", { enabled });
  } catch (e) {
    console.error("set mirror click through failed:", e);
  }
}

async function setMirrorFit(mode) {
  try {
    await invoke("set_mirror_fit", { mode });
  } catch (e) {
    console.error("set mirror fit failed:", e);
  }
}

async function refreshMonitors() {
  try {
    const monitors = await invoke("get_monitors");
    const cmbx = document.getElementById("cmbx-monitor");
    cmbx.innerHTML = "";
    monitors.forEach((m) => {
      const opt = document.createElement("option");
      opt.value = m.index;
      opt.textContent = `${m.name} (${m.width}x${m.height}) [${m.x},${m.y}]`;
      cmbx.appendChild(opt);
    });
  } catch (e) {
    console.error("get monitors failed:", e);
  }
}

async function applyMonitor() {
  const cmbx = document.getElementById("cmbx-monitor");
  const index = parseInt(cmbx.value, 10);
  if (isNaN(index)) {
    alert(t("selectMonitorHint"));
    return;
  }
  try {
    await invoke("set_window_monitor", { index });
  } catch (e) {
    alert(`${t("monitorOperationFailed")}: ${e}`);
  }
}

async function updateVirtualDisplayStatus() {
  try {
    const status = await invoke("get_virtual_display_status");
    const statusEl = document.getElementById("lbl-vdd-status");
    statusEl.textContent = status.message;
    statusEl.dataset.i18n = "";

    const displays = await invoke("list_virtual_displays");
    const listEl = document.getElementById("lbl-vdd-displays");
    listEl.textContent = displays.length > 0 ? displays.join(", ") : t("virtualDisplayNone");

    const cmbx = document.getElementById("cmbx-vdd-display");
    cmbx.innerHTML = "";
    displays.forEach((idx) => {
      const opt = document.createElement("option");
      opt.value = idx;
      opt.textContent = `Display ${idx}`;
      cmbx.appendChild(opt);
    });
  } catch (e) {
    console.error("virtual display status failed:", e);
    document.getElementById("lbl-vdd-status").textContent = "--";
  }
}

async function installVirtualDisplayDriver() {
  try {
    const result = await invoke("install_virtual_display_driver");
    alert(result);
    await updateVirtualDisplayStatus();
  } catch (e) {
    alert(`${t("virtualDisplayOperationFailed")}: ${e}`);
  }
}

async function addVirtualDisplay() {
  try {
    const idx = await invoke("add_virtual_display");
    alert(`${t("virtualDisplayAdded")}: ${idx}`);
    await updateVirtualDisplayStatus();
  } catch (e) {
    alert(`${t("virtualDisplayOperationFailed")}: ${e}`);
  }
}

async function removeVirtualDisplay() {
  const cmbx = document.getElementById("cmbx-vdd-display");
  const index = parseInt(cmbx.value, 10);
  if (isNaN(index)) {
    alert(t("virtualDisplaySelectHint"));
    return;
  }
  try {
    await invoke("remove_virtual_display", { index });
    await updateVirtualDisplayStatus();
  } catch (e) {
    alert(`${t("virtualDisplayOperationFailed")}: ${e}`);
  }
}

// ==================== 关于页面 ====================
async function showAbout() {
  const version = await invoke("get_app_version");
  const aboutBody = document.getElementById("about-body");
  aboutBody.innerHTML = `
    <div class="about-info-row">
      <span class="label">${t("aboutVersion")}</span>
      <span class="value">${version}</span>
    </div>
    <p>${t("aboutDesc")}</p>
    <h3>${t("aboutUsageTitle")}</h3>
    <ul>
      <li>${t("aboutUsage1")}</li>
      <li>${t("aboutUsage2")}</li>
      <li>${t("aboutUsage3")}</li>
      <li>${t("aboutUsage4")}</li>
      <li>${t("aboutUsage5")}</li>
      <li>${t("aboutUsage6")}</li>
    </ul>
    <h3>${t("aboutShortcutsTitle")}</h3>
    <ul>
      <li>${t("aboutShortcuts1")}</li>
      <li>${t("aboutShortcuts2")}</li>
    </ul>
    <div class="about-info-row">
      <span class="label">${t("aboutLicense")}</span>
      <span class="value">${t("aboutLicenseText")}</span>
    </div>
    <div class="about-info-row">
      <span class="label">${t("aboutOpenSource")}</span>
      <a class="value about-link" id="about-github-link" href="https://github.com/JustSmartAuto/CameraViewerTauri" target="_blank" rel="noopener noreferrer">https://github.com/JustSmartAuto/CameraViewerTauri</a>
    </div>
  `;
  document.getElementById("about-github-link").addEventListener("click", (e) => {
    e.preventDefault();
    window.__TAURI__.opener.openUrl(e.currentTarget.href);
  });
  document.getElementById("about-modal").classList.remove("hidden");
}

function closeAbout() {
  document.getElementById("about-modal").classList.add("hidden");
}

// ==================== 调试工具 ====================
async function startRemoteCommandServer() {
  const port = parseInt(document.getElementById("num-remote-cmd-port").value) || 10022;
  try {
    const result = await invoke("start_remote_command_server", { port });
    alert(result);
    updateRemoteCommandStatus();
  } catch (e) {
    alert(`${t("remoteCommandOperationFailed")}: ${e}`);
  }
}

async function stopRemoteCommandServer() {
  try {
    const result = await invoke("stop_remote_command_server");
    alert(result);
    updateRemoteCommandStatus();
  } catch (e) {
    alert(`${t("remoteCommandOperationFailed")}: ${e}`);
  }
}

function loadDebugToolsData() {
  const remoteReady = updateRemoteCommandStatus();
  // 静默预填子网：探测失败时仅清空输入框并记录日志，不弹窗打扰；后台执行不阻塞加载遮罩
  detectLanSubnet(true);
  return remoteReady;
}

async function updateRemoteCommandStatus() {
  try {
    const status = await invoke("get_remote_command_status");
    const statusEl = document.getElementById("lbl-remote-cmd-status");
    if (status && status.is_running) {
      statusEl.textContent = `${t("running")} (${t("remoteCommandPort")}${status.port})`;
      statusEl.dataset.i18n = "";
    } else {
      statusEl.textContent = t("stopped");
      statusEl.dataset.i18n = "stopped";
    }

    const logs = await invoke("get_remote_command_logs");
    const terminal = document.getElementById("remote-cmd-terminal");
    if (!terminal) return;
    terminal.innerHTML = "";
    logs.forEach(entry => {
      const line = document.createElement("div");
      line.className = "log-line";
      line.textContent = `[${entry.timestamp}] [${entry.level}] ${entry.message}`;
      terminal.appendChild(line);
    });
    terminal.scrollTop = terminal.scrollHeight;
  } catch (e) {
    console.error("remote command status failed:", e);
  }
}

async function detectLanSubnet(silent = false) {
  const input = document.getElementById("txt-lan-subnet");
  input.value = t("running");
  try {
    const subnet = await invoke("get_local_subnet_info");
    input.value = subnet;
  } catch (e) {
    input.value = "";
    if (silent) {
      console.warn("auto detect local subnet failed:", e);
    } else {
      alert(`${t("lanScanFailed")}: ${e}`);
    }
  }
}

async function scanLanDevices() {
  const subnet = document.getElementById("txt-lan-subnet").value.trim();
  const timeout = parseInt(document.getElementById("num-lan-timeout").value) || 800;
  const resultEl = document.getElementById("lbl-lan-scan-result");
  const tbody = document.querySelector("#dgv-lan-devices tbody");
  resultEl.textContent = t("running");
  tbody.innerHTML = "";

  try {
    const result = await invoke("scan_lan_devices", { subnet: subnet || undefined, timeoutMs: timeout });
    resultEl.textContent = `${result.message} (${result.elapsed_ms} ms)`;

    result.devices.forEach(device => {
      const tr = document.createElement("tr");
      tr.innerHTML = `
        <td>${device.ip}</td>
        <td>${device.mac || "-"}</td>
        <td>${device.hostname || "-"}</td>
        <td>${device.status === "online" ? t("online") : t("offline")}</td>
      `;
      tbody.appendChild(tr);
    });
  } catch (e) {
    resultEl.textContent = `${t("lanScanFailed")}: ${e}`;
  }
}
