// CogSocket 单元格设置弹窗（TODO #7）
//
// 单例弹窗，按相机打开：连接（会话/ready/keepAlive 在 cogsocket_manager.js）
// → 拉取最新 HmiResult → 过滤可编辑单元格（EditInt/Float/String/CheckBox/Button）
// → 单行/批量 setCellValue(setCellValues)，写后回读校验。
// 弹窗关闭即 dispose 会话；若曾置离线则自动恢复在线。

import { t } from "./i18n.js";
import { CogConnection, parseCameraTarget } from "./cogsocket_manager.js";

const { invoke } = window.__TAURI__.core;

// 每相机一个连接实例（弹窗关闭时统一断开）
const connections = new Map();
let currentIndex = -1;
let currentItem = null;
let currentConn = null;
let cellsView = []; // 当前表格对应的可编辑单元格（HmiResult.cells 子集）

const READBACK_DELAY_MS = 600;

function el(id) {
  return document.getElementById(id);
}

export function initCogSocketCells() {
  el("cs-btn-close").addEventListener("click", closeDialog);
  el("cogsocket-modal").addEventListener("click", (e) => {
    if (e.target === el("cogsocket-modal")) closeDialog();
  });
  el("cs-btn-save-auth").addEventListener("click", saveAuth);
  el("cs-btn-connect").addEventListener("click", toggleConnect);
  el("cs-btn-refresh").addEventListener("click", () => loadEditableCells(true));
  el("cs-btn-offline").addEventListener("click", toggleOffline);
  el("cs-btn-trigger").addEventListener("click", manualTrigger);
  el("cs-btn-batch").addEventListener("click", batchWrite);
  el("cs-btn-manual-write").addEventListener("click", manualWrite);
  el("cs-btn-clear-log").addEventListener("click", () => (el("cs-log").innerHTML = ""));

  // 语言切换：静态文案由 data-i18n 机制刷新，这里重渲染动态内容
  window.addEventListener("app-language-changed", () => {
    renderConnBar();
    renderRows();
    updateOfflineButton();
  });
}

/** 由相机网格"单元格"按钮调用 */
export async function openCogSocketDialog(index, item) {
  currentIndex = index;
  // 从后端取最新配置（凭据可能在本软件其它入口/重启后更新过），与传入项合并
  let merged = item || { id: index, ip: "" };
  try {
    const cfg = await invoke("get_camera_config");
    const fresh = (cfg.items || [])[index];
    if (fresh) merged = { ...fresh, ip: item?.ip || fresh.ip || "" };
  } catch {
    /* 拉取失败则使用传入项 */
  }
  currentItem = merged;
  currentConn = connections.get(index) || null;
  cellsView = [];

  el("cs-camera-title").textContent =
    `#${index + 1}${currentItem.remark ? " " + currentItem.remark : ""}`;

  let target;
  try {
    target = parseCameraTarget(currentItem.ip);
    el("cs-target").value = `${target.host}:${target.port}`;
  } catch (e) {
    el("cs-target").value = "";
    log("error", e.message);
  }
  el("cs-user").value = currentItem.cogsocket_user || "admin";
  el("cs-password").value = currentItem.cogsocket_password || "";
  el("cs-tbody").innerHTML = "";
  el("cs-conn-info").textContent = "";
  el("cs-manual-name").value = "";
  el("cs-manual-value").value = "";

  renderConnBar();
  updateOfflineButton();

  el("cogsocket-modal").classList.remove("hidden");

  if (currentConn && currentConn.state === "open") {
    renderConnBar();
    await loadEditableCells(false);
  }
}

async function closeDialog() {
  el("cogsocket-modal").classList.add("hidden");
  // 关闭即释放 HMI 会话（相机固定连接数有限）；离线状态由 disconnect 自动恢复
  for (const conn of connections.values()) {
    try {
      await conn.disconnect();
    } catch {
      /* ignore */
    }
  }
  connections.clear();
  currentConn = null;
}

async function saveAuth() {
  const user = el("cs-user").value.trim();
  const password = el("cs-password").value;
  try {
    await invoke("update_camera_cogsocket_auth", {
      id: currentIndex,
      user,
      password,
    });
    if (currentItem) {
      currentItem.cogsocket_user = user;
      currentItem.cogsocket_password = password;
    }
    log("info", t("cogsocketAuthSaved"));
  } catch (e) {
    log("error", `${t("cogsocketAuthSaved")} ×: ${e.message || e}`);
  }
}

async function toggleConnect() {
  if (currentConn && (currentConn.state === "open" || currentConn.state === "connecting")) {
    try {
      await currentConn.disconnect();
    } finally {
      connections.delete(currentIndex);
      currentConn = null;
      renderConnBar();
      updateOfflineButton();
    }
    return;
  }

  let target;
  try {
    target = parseCameraTarget(el("cs-target").value);
  } catch (e) {
    log("error", e.message);
    return;
  }

  const conn = new CogConnection({
    host: target.host,
    port: target.port,
    user: el("cs-user").value.trim() || "admin",
    password: el("cs-password").value,
    onLog: (level, msg) => log(level, msg),
    onState: () => renderConnBar(),
    onResult: (result) => handleResultFrame(result),
  });
  connections.set(currentIndex, conn);
  currentConn = conn;
  renderConnBar();

  try {
    await conn.connect();
    renderConnBar();
    await loadEditableCells(false);
  } catch (e) {
    log("error", e.message || String(e));
    renderConnBar();
  }
}

function renderConnBar() {
  const conn = currentConn;
  const state = conn ? conn.state : "closed";
  const dot = el("cs-status-dot");
  dot.className = `cs-status-dot cs-${state}`;
  dot.title = t(`cogsocketState_${state}`);

  const btn = el("cs-btn-connect");
  btn.textContent =
    state === "open" || state === "connecting"
      ? t("cogsocketDisconnect")
      : t("cogsocketConnect");
  btn.disabled = state === "connecting";

  const connected = state === "open";
  for (const id of [
    "cs-btn-refresh",
    "cs-btn-offline",
    "cs-btn-trigger",
    "cs-btn-batch",
    "cs-btn-manual-write",
  ]) {
    el(id).disabled = !connected;
  }

  if (connected) {
    conn
      .getJobName()
      .then((name) => {
        el("cs-conn-info").textContent = `${t("cogsocketJob")}: ${name || "-"}`;
      })
      .catch(() => {});
  } else {
    el("cs-conn-info").textContent = t(`cogsocketState_${state}`);
  }
}

async function loadEditableCells(manual) {
  if (!isOpen()) return;
  try {
    const result = await currentConn.getLatestResult();
    applyResult(result);
    if (manual && cellsView.length === 0) log("info", t("cogsocketNoEditable"));
  } catch (e) {
    log("error", `${t("cogsocketRefresh")} ×: ${e.message || e}`);
  }
}

function handleResultFrame(result) {
  // 结果帧已由 manager 自动回 ready；弹窗打开时刷新表格
  if (!el("cogsocket-modal").classList.contains("hidden")) applyResult(result);
}

function applyResult(result) {
  if (!result || !Array.isArray(result.cells)) {
    cellsView = [];
  } else {
    cellsView = result.cells.filter((c) => c && c.editable === true);
  }
  renderRows();
}

// ---------------- 单元格表 ----------------

function cellKind(cell) {
  const ty = cell.$type || "";
  if (ty.includes("EditInt")) return "int";
  if (ty.includes("EditFloat") || ty.includes("EditDouble") || ty.includes("EditReal"))
    return "float";
  if (ty.includes("EditString")) return "string";
  if (ty.includes("CheckBox") || ty.includes("CheckButton")) return "checkbox";
  if (ty.includes("Button")) return "button";
  return "other";
}

function kindLabel(kind) {
  return t(`cogsocketKind_${kind}`);
}

function cellDisplayName(cell) {
  return cell.name || cell.location || "";
}

function renderRows() {
  const tbody = el("cs-tbody");
  tbody.innerHTML = "";
  for (const cell of cellsView) {
    const kind = cellKind(cell);
    const tr = document.createElement("tr");
    tr.dataset.location = cell.location || "";
    tr.dataset.name = cellDisplayName(cell);

    const tdLoc = document.createElement("td");
    tdLoc.className = "cs-col-loc";
    tdLoc.textContent = cell.location || "";
    const tdName = document.createElement("td");
    tdName.textContent = cellDisplayName(cell);
    const tdType = document.createElement("td");
    tdType.textContent = kindLabel(kind);

    const tdValue = document.createElement("td");
    tdValue.className = "cs-col-value";
    let input = null;
    if (kind === "button") {
      tdValue.textContent = cell.caption || "—";
    } else if (kind === "checkbox") {
      input = document.createElement("input");
      input.type = "checkbox";
      input.checked = cell.data === true;
    } else if (kind === "int" || kind === "float") {
      input = document.createElement("input");
      input.type = "number";
      input.step = kind === "int" ? "1" : "any";
      if (cell.min != null) input.min = String(cell.min);
      if (cell.max != null) input.max = String(cell.max);
      input.value = cell.data == null ? "" : String(cell.data);
      input.title =
        cell.min != null || cell.max != null
          ? `${t("cogsocketRange")}: [${cell.min ?? "-∞"}, ${cell.max ?? "+∞"}]`
          : "";
    } else if (kind === "string") {
      input = document.createElement("input");
      input.type = "text";
      if (cell.maxLength != null) input.maxLength = Number(cell.maxLength);
      input.value = cell.data == null ? "" : String(cell.data);
    } else {
      input = document.createElement("input");
      input.type = "text";
      input.value = cell.data == null ? "" : String(cell.data);
    }
    if (input) {
      input.classList.add("cs-cell-input");
      input.addEventListener("input", () => tr.classList.add("cs-dirty"));
      tdValue.appendChild(input);
    }

    // 当前值（回读列）
    const tdRead = document.createElement("td");
    tdRead.className = "cs-col-readback";
    tdRead.textContent = formatReadback(cell.data);

    const tdAct = document.createElement("td");
    tdAct.className = "cs-col-action";
    if (kind === "button") {
      const btn = document.createElement("button");
      btn.className = "cs-mini-btn";
      btn.textContent = t("cogsocketExecute");
      btn.addEventListener("click", () => executeButton(cell));
      tdAct.appendChild(btn);
    } else {
      const btn = document.createElement("button");
      btn.className = "cs-mini-btn";
      btn.textContent = t("cogsocketWrite");
      btn.addEventListener("click", () =>
        writeRow(cell, input, tdRead, tr, btn)
      );
      tdAct.appendChild(btn);
    }

    tr.appendChild(tdLoc);
    tr.appendChild(tdName);
    tr.appendChild(tdType);
    tr.appendChild(tdValue);
    tr.appendChild(tdRead);
    tr.appendChild(tdAct);
    tbody.appendChild(tr);
  }
}

function readInputValue(kind, input) {
  if (kind === "checkbox") return input.checked;
  const raw = input.value;
  if (kind === "int") {
    const v = Number(raw);
    if (!Number.isInteger(v)) throw new Error(t("cogsocketErrNotInt"));
    return v;
  }
  if (kind === "float") {
    const v = Number(raw);
    if (Number.isNaN(v)) throw new Error(t("cogsocketErrNotNumber"));
    return v;
  }
  return raw;
}

async function writeRow(cell, input, tdRead, tr, btn) {
  if (!isOpen()) return;
  const kind = cellKind(cell);
  let value;
  try {
    value = readInputValue(kind, input);
    validateRange(cell, value);
  } catch (e) {
    log("error", e.message);
    return;
  }
  btn.disabled = true;
  try {
    await currentConn.setCell(cellDisplayName(cell), value);
    log("info", `setCellValue("${cellDisplayName(cell)}", ${safeJson(value)}) ${t("cogsocketWriteOk")}`);
    await refreshReadback(tdRead);
    tr.classList.remove("cs-dirty");
  } catch (e) {
    log("error", `${t("cogsocketWriteFail")}: ${e.message || e}`);
    tdRead.textContent = "✗";
    tdRead.classList.add("cs-read-fail");
  } finally {
    btn.disabled = false;
  }
}

async function executeButton(cell) {
  if (!isOpen()) return;
  const name = cellDisplayName(cell);
  if (!window.confirm(t("cogsocketExecConfirm").replace("{name}", name))) return;
  try {
    await currentConn.setCell(name, true);
    log("info", `按钮 ${name} ${t("cogsocketWriteOk")}`);
  } catch (e) {
    log("error", `${t("cogsocketWriteFail")}: ${e.message || e}`);
  }
}

async function batchWrite() {
  if (!isOpen()) return;
  const map = {};
  let n = 0;
  const failures = [];
  el("cs-tbody").querySelectorAll("tr.cs-dirty").forEach((tr) => {
    const cell = cellsView.find(
      (c) => (c.location || "") === tr.dataset.location &&
        cellDisplayName(c) === tr.dataset.name
    );
    if (!cell || cellKind(cell) === "button") return;
    const input = tr.querySelector(".cs-cell-input");
    try {
      const value = readInputValue(cellKind(cell), input);
      validateRange(cell, value);
      map[cellDisplayName(cell)] = value;
      n++;
    } catch (e) {
      failures.push(`${cellDisplayName(cell)}: ${e.message}`);
    }
  });
  if (failures.length) {
    log("error", failures.join("; "));
    return;
  }
  if (n === 0) {
    log("info", t("cogsocketNoDirty"));
    return;
  }
  try {
    await currentConn.setCells(map);
    log("info", `setCellValues ×${n} ${t("cogsocketWriteOk")}: ${safeJson(map)}`);
    await new Promise((r) => setTimeout(r, READBACK_DELAY_MS));
    const result = await currentConn.getLatestResult();
    applyResult(result);
  } catch (e) {
    log("error", `${t("cogsocketWriteFail")}: ${e.message || e}`);
  }
}

async function manualWrite() {
  if (!isOpen()) return;
  const name = el("cs-manual-name").value.trim();
  if (!name) {
    log("error", t("cogsocketManualNameEmpty"));
    return;
  }
  const raw = el("cs-manual-value").value;
  let value;
  try {
    value = JSON.parse(raw);
  } catch {
    value = raw; // 解析失败按原始字符串
  }
  try {
    await currentConn.setCell(name, value);
    log("info", `setCellValue("${name}", ${safeJson(value)}) ${t("cogsocketWriteOk")}`);
  } catch (e) {
    log("error", `${t("cogsocketWriteFail")}: ${e.message || e}`);
  }
}

async function manualTrigger() {
  if (!isOpen()) return;
  try {
    await currentConn.manualTrigger();
    log("info", t("cogsocketTriggerOk"));
    await new Promise((r) => setTimeout(r, READBACK_DELAY_MS));
    await loadEditableCells(false);
  } catch (e) {
    log("error", `${t("cogsocketTrigger")} ×: ${e.message || e}`);
  }
}

async function toggleOffline() {
  if (!isOpen()) return;
  const goOffline = !currentConn.wentOffline;
  try {
    await currentConn.setSoftOnline(!goOffline);
    log("info", goOffline ? t("cogsocketOfflineDone") : t("cogsocketOnlineRestored"));
    updateOfflineButton();
  } catch (e) {
    log("error", `${t("cogsocketOfflineToggleFail")}: ${e.message || e}`);
  }
}

function updateOfflineButton() {
  const btn = el("cs-btn-offline");
  if (!btn) return;
  btn.textContent =
    currentConn && currentConn.wentOffline
      ? t("cogsocketGoOnline")
      : t("cogsocketGoOffline");
}

async function refreshReadback(tdRead) {
  await new Promise((r) => setTimeout(r, READBACK_DELAY_MS));
  const result = await currentConn.getLatestResult();
  if (result && Array.isArray(result.cells)) {
    const tr = tdRead.closest("tr");
    const found = result.cells.find(
      (c) =>
        (c.location || "") === tr.dataset.location &&
        cellDisplayName(c) === tr.dataset.name
    );
    if (found) {
      tdRead.textContent = formatReadback(found.data);
      tdRead.classList.remove("cs-read-fail");
    }
  }
}

function validateRange(cell, value) {
  if (typeof value !== "number") return;
  if (cell.min != null && value < cell.min)
    throw new Error(`${t("cogsocketRangeErr")} < ${cell.min}`);
  if (cell.max != null && value > cell.max)
    throw new Error(`${t("cogsocketRangeErr")} > ${cell.max}`);
}

function formatReadback(data) {
  if (data == null) return "";
  if (typeof data === "object") return safeJson(data);
  return String(data);
}

function isOpen() {
  if (currentConn && currentConn.state === "open") return true;
  log("warn", t("cogsocketNotConnected"));
  return false;
}

function safeJson(v) {
  try {
    return JSON.stringify(v);
  } catch {
    return String(v);
  }
}

// ---------------- 日志 ----------------

function log(level, msg) {
  const box = el("cs-log");
  if (!box) return;
  const line = document.createElement("div");
  line.className = `cs-log-line cs-log-${level}`;
  const now = new Date();
  const hh = String(now.getHours()).padStart(2, "0");
  const mm = String(now.getMinutes()).padStart(2, "0");
  const ss = String(now.getSeconds()).padStart(2, "0");
  line.textContent = `[${hh}:${mm}:${ss}] ${msg}`;
  box.appendChild(line);
  box.scrollTop = box.scrollHeight;
  while (box.childNodes.length > 300) box.removeChild(box.firstChild);
}
