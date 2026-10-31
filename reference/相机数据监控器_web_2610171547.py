# -*- coding: utf-8 -*-
"""
相机数据监控器（西门子 S7）
读取 4 个相机 DB 块的 X0/Y0/X1/Y1 坐标，与参考坐标对比，超差报警。
每个变量(X0/Y0/X1/Y1)支持独立设置报警阈值。
勾选"发送OK/NG信号"后, 每个相机按拍照触发DB偏移量4(INT)的上升沿
仅发送一次信号: 上升沿后间隔1秒, 向该相机自身DB块偏移量2(INT)写入:
    2 = NG (任一变量超差), 3 = OK (全部在阈值内)
每个相机面板右侧有触发信号灯: 灰=无触发, 黄=已接收触发待发送(1秒窗口),
绿=本次触发已发送OK/NG, 可通过它确认极短触发脉冲是否被轮询采集到

内嵌 Web 页面(仅标准库, 无需 Flask 等):
    程序启动时在 0.0.0.0:10209 开启 HTTP 服务, 浏览器访问
    http://<本机IP>:10209/ 即可获得与 tkinter 界面相同功能的页面:
    连接/断开/开始/停止监控、发送OK/NG信号开关、左右工位独立配方应用/保存、
    每变量参考坐标与阈值编辑、触发信号灯、4x4 趋势图(虚线基线=参考坐标,
    红色段=超差)、清除历史、小数位数切换。
    Web 线程不直接操作 Tkinter 控件: 页面轮询 /api/state 读取线程安全快照,
    页面操作通过 /api/* 指令队列转交 Tkinter 主线程执行。

依赖: 标准库 + python-snap7
    pip install python-snap7
打包:
    pyinstaller -F -w --name 相机数据监控器 相机数据监控器.py
    (snap7.dll 需与 exe 同目录, 或 --add-binary 打包)
"""

import os
import subprocess
import sys
import time
import json
import queue
import threading
import tkinter as tk
from tkinter import ttk, messagebox, simpledialog
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import tomllib
import snap7
from snap7.util import get_real, set_int, get_int

# ------------------------------------------------------------------ 配置
PLC_IP = "192.168.1.70"
PLC_RACK = 0
PLC_SLOT = 0          # 本机 S7 通信 Rack/Slot 均为 0

# 内嵌 Web 服务监听地址(0.0.0.0 = 本机所有网卡, 局域网内任何设备均可访问)
WEB_HOST = "0.0.0.0"
WEB_PORT = 10209

# 相机定义: (显示名, DB号)
CAMERAS = [
    ("左工位左相机", 92),
    ("左工位右相机", 97),
    ("右工位左相机", 98),
    ("右工位右相机", 93),
]

# 每个相机内监听的变量: (变量名, 偏移量, 字节长度)
VARIABLES = [
    ("X0", 10, 4),
    ("Y0", 50, 4),
    ("X1", 14, 4),
    ("Y1", 54, 4),
]

RECIPE_NO_OFFSET = 6       # 每个相机DB偏移量6(INT) = PLC当前配方号
# 自动切换配方的配方号来源: 左工位看DB97, 右工位看DB98 (各工位取一台相机即可)
RECIPE_NO_DB = {"left": 97, "right": 98}
FALLBACK_RECIPE = "149#K"  # 配方号无匹配配方时的回退默认配方名

POLL_INTERVAL_MS = 10      # 轮询周期(读取PLC+报警判断+OK/NG信号发送)
TREND_REDRAW_MS = 200      # 趋势图重绘限频, 避免10ms高频重绘拖垮GUI
HISTORY_LEN = 300          # 趋势图保留的最大采样点数
BG_NORMAL = "#dcffdc"      # 正常绿
BG_ALARM  = "#ff7878"      # 报警红
BG_UNKNOWN = "#f0f0f0"     # 未读到灰
BG_BADREF = "#ffe08a"      # 参考非法黄

# ------------------------------------------------------------------ 配方配置
CONFIG_FILE = os.path.join(os.path.dirname(os.path.abspath(sys.argv[0])),
                           "camera_data_listener_recipes.toml")
# 工位划分: 机台左右两个工位同时生产但配方不同, 各自独立加载/保存配方
STATION_DBS = {
    "left":  [str(db) for _n, db in CAMERAS[:2]],   # 左工位: DB92/DB97
    "right": [str(db) for _n, db in CAMERAS[2:]],   # 右工位: DB98/DB93
}
STATION_NAMES = {"left": "左工位", "right": "右工位"}
DEFAULT_STATION_RECIPES = {"left": "149#K-L", "right": "149#K-R"}

DEFAULT_CONFIG_DOC = '''auto_start = false
auto_switch_recipe = false
default_left_recipe = "149#K-L"
default_right_recipe = "149#K-R"

[recipes.left."149#K-L"]
recipe_no = 1

[recipes.left."149#K-L".cameras."92"]
X0 = 98.1
X0_threshold = 0.1
Y0 = 60.3
Y0_threshold = 0.1
X1 = 112.4
X1_threshold = 0.1
Y1 = 60.3
Y1_threshold = 0.1

[recipes.left."149#K-L".cameras."97"]
X0 = 98.1
X0_threshold = 0.1
Y0 = 60.3
Y0_threshold = 0.1
X1 = 112.4
X1_threshold = 0.1
Y1 = 60.3
Y1_threshold = 0.1

[recipes.right."149#K-R"]
recipe_no = 2

[recipes.right."149#K-R".cameras."98"]
X0 = 98.1
X0_threshold = 0.1
Y0 = 60.3
Y0_threshold = 0.1
X1 = 112.4
X1_threshold = 0.1
Y1 = 60.3
Y1_threshold = 0.1

[recipes.right."149#K-R".cameras."93"]
X0 = 98.1
X0_threshold = 0.1
Y0 = 60.3
Y0_threshold = 0.1
X1 = 112.3
X1_threshold = 0.1
Y1 = 60.3
Y1_threshold = 0.1
'''


def ensure_config_file():
    """配置文件不存在时创建并写入默认配方"""
    if not os.path.exists(CONFIG_FILE):
        with open(CONFIG_FILE, "w", encoding="utf-8") as f:
            f.write(DEFAULT_CONFIG_DOC)


def load_recipes():
    """读取全部配方, 返回 ({left: {配方名: {cameras: {DB号: {参数}}}, right: ...},
    {left: 默认配方名, right: 默认配方名})
    兼容旧格式 [recipes."名称".cameras."DB"]: 按DB归属工位自动拆分迁移"""
    with open(CONFIG_FILE, "rb") as f:
        doc = tomllib.load(f)
    recipes = {"left": {}, "right": {}}
    defaults = {"left": doc.get("default_left_recipe", ""),
                "right": doc.get("default_right_recipe", "")}
    # 新格式: [recipes.<工位>."名称".cameras."DB"]
    for st in ("left", "right"):
        for name, r in (doc.get("recipes", {}).get(st) or {}).items():
            if not isinstance(r, dict):
                continue
            cams = {}
            for db, p in (r.get("cameras") or {}).items():
                if str(db) in STATION_DBS[st] and isinstance(p, dict):
                    cams[str(db)] = dict(p)
            if cams:
                entry = {"cameras": cams}
                # 配方号(外键为配方名): PLC DB偏移量6的期望值
                if "recipe_no" in r:
                    entry["recipe_no"] = r["recipe_no"]
                recipes[st][name] = entry
    # 旧格式迁移: [recipes."名称".cameras."DB"] -> 按DB归属工位拆分
    for name, r in (doc.get("recipes") or {}).items():
        if name in ("left", "right") or not isinstance(r, dict):
            continue
        cams = r.get("cameras")
        if not isinstance(cams, dict):
            continue
        for st in ("left", "right"):
            mine = {str(db): dict(p) for db, p in cams.items()
                    if str(db) in STATION_DBS[st] and isinstance(p, dict)}
            if mine:
                recipes[st].setdefault(name, {"cameras": {}})["cameras"].update(mine)
    # 界面选项(持久化): 自动开始监控 / 自动切换配方
    options = {"auto_start": bool(doc.get("auto_start", False)),
               "auto_switch_recipe": bool(doc.get("auto_switch_recipe", False))}
    return recipes, defaults, options


def save_recipes(recipes, default_recipes, options=None):
    """将全部配方(按工位)与界面选项写回 toml 文件"""
    lines = []
    if options is not None:
        lines.append("auto_start = %s"
                     % ("true" if options.get("auto_start") else "false"))
        lines.append("auto_switch_recipe = %s"
                     % ("true" if options.get("auto_switch_recipe") else "false"))
        lines.append("")
    for st in ("left", "right"):
        d = (default_recipes or {}).get(st, "")
        if d:
            esc = d.replace("\\", "\\\\").replace('"', '\\"')
            lines.append('default_%s_recipe = "%s"' % (st, esc))
    lines.append("")
    for st in ("left", "right"):
        for name in sorted(recipes.get(st, {})):
            entry = recipes[st][name]
            esc = name.replace("\\", "\\\\").replace('"', '\\"')
            # 配方号写在配方头节点下 (与配方名绑定)
            lines.append('[recipes.%s."%s"]' % (st, esc))
            if "recipe_no" in entry:
                lines.append("recipe_no = %s" % entry["recipe_no"])
            lines.append("")
            cameras = entry.get("cameras", {})
            for db in sorted(cameras, key=str):
                params = cameras[db]
                lines.append('[recipes.%s."%s".cameras."%s"]' % (st, esc, db))
                for var, _, _ in VARIABLES:
                    if var in params:
                        lines.append("%s = %s" % (var, params[var]))
                    th_key = var + "_threshold"
                    if th_key in params:
                        lines.append("%s = %s" % (th_key, params[th_key]))
                # 兼容旧格式的全局阈值
                if "threshold" in params:
                    lines.append("threshold = %s" % params["threshold"])
                lines.append("")
    with open(CONFIG_FILE, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))


def ping_host(ip, timeout_ms=1000):
    """ping 目标IP, 可达返回 True (Windows 用 -n/-w, Linux/Mac 用 -c/-W)"""
    try:
        if sys.platform.startswith("win"):
            cmd = ["ping", "-n", "1", "-w", str(timeout_ms), ip]
        else:
            cmd = ["ping", "-c", "1", "-W", str(max(1, timeout_ms // 1000)), ip]
        return subprocess.run(cmd, stdout=subprocess.DEVNULL,
                              stderr=subprocess.DEVNULL).returncode == 0
    except Exception:
        return False


# ================================================================== Web 页面
INDEX_HTML = """<!DOCTYPE html>
<html lang="zh">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>相机数据监控器 - Web</title>
<style>
  * { box-sizing: border-box; }
  body { font-family: "Microsoft YaHei", sans-serif; margin: 8px; background: #f4f4f4; font-size: 13px; }
  .bar { display: flex; align-items: center; gap: 6px; background: #fff; border: 1px solid #bbb;
         padding: 6px 8px; margin-bottom: 6px; flex-wrap: wrap; border-radius: 3px; }
  .bar b { color: #333; }
  button { padding: 3px 12px; cursor: pointer; }
  input, select { padding: 2px 4px; border: 1px solid #aaa; }
  #panels { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
  .cam { background: #fff; border: 1px solid #999; border-radius: 4px; padding: 6px 8px; }
  .cam h3 { margin: 0 0 4px; font-size: 13px; color: #333; }
  table { border-collapse: collapse; width: 100%; }
  th, td { border: 1px solid #ddd; padding: 2px 4px; text-align: center; }
  th { background: #f0f0f0; font-size: 12px; }
  td.cell { font-family: Consolas, monospace; min-width: 64px; }
  td input { width: 72px; text-align: center; font-family: Consolas, monospace; }
  .bg-normal { background: #dcffdc; }
  .bg-alarm  { background: #ff7878; }
  .bg-unknown { background: #f0f0f0; }
  .bg-badref { background: #ffe08a; }
  .lamp { width: 20px; height: 34px; border: 1px solid #666; display: inline-block; border-radius: 3px; }
  .lamp.idle { background: #f0f0f0; }
  .lamp.pending { background: #ffcc00; }
  .lamp.sent { background: #66dd66; }
  #trendbox { background: #fff; border: 1px solid #bbb; border-radius: 3px; padding: 6px; margin-top: 8px; }
  #trends { display: grid; grid-template-columns: repeat(4, 1fr); gap: 6px; margin-top: 4px; }
  .trend { background: #fff; border: 1px solid #ccc; padding: 2px; }
  .trend .tt { font-size: 11px; color: #333; white-space: nowrap; overflow: hidden; }
  canvas { width: 100%; height: 95px; display: block; background: #fff; }
  #status { position: fixed; bottom: 0; left: 0; right: 0; background: #e4e4e4;
            border-top: 1px solid #999; padding: 3px 8px; font-size: 12px; }
  .spacer { height: 28px; }
</style>
</head>
<body>

<div class="bar">
  <b>相机数据监控器 - Web</b>
  <label>PLC IP:</label><input id="ip" size="12">
  <label>Rack:</label><input id="rack" size="3">
  <label>Slot:</label><input id="slot" size="3">
  <button id="btnConn">连 接</button>
  <button id="btnDisconn">断 开</button>
  <span style="width:14px"></span>
  <button id="btnStart">开始监控</button>
  <button id="btnStop">停止监控</button>
  <label style="margin-left:auto"><input type="checkbox" id="chkSignal"> 发送OK/NG信号</label>
  <label><input type="checkbox" id="chkAutoStart"> 自动开始监控</label>
  <label><input type="checkbox" id="chkAutoSwitch"> 自动切换配方</label>
</div>

<div class="bar">
  <b>左工位配方:</b>
  <input id="recipe-left" list="recipelist-left" size="12">
  <datalist id="recipelist-left"></datalist>
  <label>配方号:</label><input id="recipeno-left" size="4" title="与配方名绑定保存">
  <button id="btnApplyLeft">应用配方</button>
  <button id="btnSaveLeft" disabled>保存配方</button>
  <label><input type="checkbox" id="chkUnlockLeft"> 解锁</label>
  <span style="width:18px"></span>
  <b>右工位配方:</b>
  <input id="recipe-right" list="recipelist-right" size="12">
  <datalist id="recipelist-right"></datalist>
  <label>配方号:</label><input id="recipeno-right" size="4" title="与配方名绑定保存">
  <button id="btnApplyRight">应用配方</button>
  <button id="btnSaveRight" disabled>保存配方</button>
  <label><input type="checkbox" id="chkUnlockRight"> 解锁</label>
  <button id="btnClear" style="margin-left:auto">清除历史</button>
  <label>小数位数:</label>
  <select id="decimals"></select>
</div>

<div id="panels"></div>

<div id="trendbox">
  <div style="font-size:12px;color:#555">趋势图 (4相机 x 4变量, 虚线基线 = 参考坐标, 红色段 = 超差)</div>
  <div id="trends"></div>
</div>

<div class="spacer"></div>
<div id="status">未连接</div>

<script>
const CAMERAS = [["左工位左相机",92],["左工位右相机",97],["右工位左相机",98],["右工位右相机",93]];
const VARS = ["X0","Y0","X1","Y1"];
const VCOLOR = {X0:"#2266cc", Y0:"#22aa44", X1:"#dd8800", Y1:"#8844cc"};
const HIST_LEN = 300;

const $ = id => document.getElementById(id);
const cells = {};      // db -> var -> {val, dev, ref, th}
const lamps = {};      // db -> span
const trendCvs = {};   // "db,var" -> canvas

// ---------- 构建相机面板 ----------
const panelsDiv = $("panels");
CAMERAS.forEach(([name, db]) => {
  cells[db] = {};
  const div = document.createElement("div");
  div.className = "cam";
  let h = "<h3>" + name + " (DB" + db + ")</h3><table>" +
    "<tr><th>变量</th><th>当前值</th><th>偏差</th><th>参考坐标</th><th>阈值±</th><th>触发</th></tr>";
  VARS.forEach((v, i) => {
    h += "<tr><td><b>" + v + "</b></td>" +
      "<td class='cell bg-unknown' id='val-" + db + "-" + v + "'>----</td>" +
      "<td class='cell bg-unknown' id='dev-" + db + "-" + v + "'>----</td>" +
      "<td><input id='ref-" + db + "-" + v + "' title='参考坐标'></td>" +
      "<td><input id='th-" + db + "-" + v + "' title='报警阈值' style='width:56px'></td>";
    if (i === 0) h += "<td rowspan='4'><span class='lamp idle' id='lamp-" + db + "'></span></td>";
    h += "</tr>";
  });
  h += "</table>" +
    "<div style='margin-top:3px;font-size:12px;color:#555'>PLC配方号(DB+6): " +
    "<span class='cell bg-unknown' style='display:inline-block;min-width:80px' " +
    "id='rno-" + db + "'>----</span></div>";
  div.innerHTML = h;
  panelsDiv.appendChild(div);
  lamps[db] = $("lamp-" + db);
  VARS.forEach(v => {
    cells[db][v] = {
      val: $("val-" + db + "-" + v),
      dev: $("dev-" + db + "-" + v),
      ref: $("ref-" + db + "-" + v),
      th:  $("th-"  + db + "-" + v)
    };
    const commit = () => sendParams(db);
    cells[db][v].ref.addEventListener("change", commit);
    cells[db][v].th.addEventListener("change", commit);
    cells[db][v].ref.addEventListener("keydown", e => { if (e.key === "Enter") cells[db][v].ref.blur(); });
    cells[db][v].th.addEventListener("keydown", e => { if (e.key === "Enter") cells[db][v].th.blur(); });
  });
});

// ---------- 构建趋势图 ----------
const trendsDiv = $("trends");
CAMERAS.forEach(([name, db]) => {
  VARS.forEach(v => {
    const d = document.createElement("div");
    d.className = "trend";
    d.innerHTML = "<div class='tt'>" + name + " " + v + " (DB" + db + ")</div>";
    const cv = document.createElement("canvas");
    d.appendChild(cv);
    trendsDiv.appendChild(d);
    trendCvs[db + "," + v] = cv;
  });
});

// 小数位数选项
for (let i = 0; i <= 6; i++) {
  const o = document.createElement("option");
  o.value = i; o.textContent = i;
  $("decimals").appendChild(o);
}

// ---------- 网络 ----------
async function post(path, obj) {
  try {
    await fetch(path, { method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(obj || {}) });
  } catch (e) { /* 状态轮询会显示连接异常 */ }
}

function sendParams(db) {
  const cameras = {};
  const vars = {};
  VARS.forEach(v => { vars[v] = { ref: cells[db][v].ref.value, th: cells[db][v].th.value }; });
  cameras[db] = vars;
  post("/api/set_params", { cameras: cameras });
}

$("btnConn").onclick    = () => post("/api/connect", { ip: $("ip").value, rack: $("rack").value, slot: $("slot").value });
$("btnDisconn").onclick = () => post("/api/disconnect", {});
$("btnStart").onclick   = () => post("/api/start", {});
$("btnStop").onclick    = () => post("/api/stop", {});
// 左右工位配方控件: 各自独立的应用/保存/解锁
const STATIONS = ["left", "right"];
const STATION_NAMES = { left: "左工位", right: "右工位" };
const SID = st => st === "left" ? "Left" : "Right";

STATIONS.forEach(st => {
  const S = SID(st);
  $("recipe-" + st).addEventListener("change", () =>
    post("/api/recipe_select", { station: st, name: $("recipe-" + st).value }));
  $("btnApply" + S).onclick = () =>
    post("/api/recipe_apply", { station: st, name: $("recipe-" + st).value });
  $("btnSave" + S).onclick = () =>
    post("/api/recipe_save", { station: st, name: $("recipe-" + st).value });
  $("chkUnlock" + S).onchange = () => {
    $("btnSave" + S).disabled = !$("chkUnlock" + S).checked;
    post("/api/unlock", { station: st, enabled: $("chkUnlock" + S).checked });
  };
  $("recipeno-" + st).addEventListener("change", () =>
    post("/api/recipe_no", { station: st, value: $("recipeno-" + st).value }));
});
$("btnClear").onclick   = () => post("/api/clear_history", {});
$("chkSignal").onchange = () => post("/api/signal", { enabled: $("chkSignal").checked });
$("chkAutoStart").onchange = () => post("/api/auto_start", { enabled: $("chkAutoStart").checked });
$("chkAutoSwitch").onchange = () => post("/api/auto_switch", { enabled: $("chkAutoSwitch").checked });
$("decimals").onchange  = () => post("/api/decimals", { value: parseInt($("decimals").value, 10) });

function setIfNotFocused(id, val) {
  const el = $(id);
  if (document.activeElement !== el && el.value !== String(val)) el.value = val;
}

function bgcls(st) {
  return st === "normal" ? "cell bg-normal"
       : st === "alarm"  ? "cell bg-alarm"
       : st === "badref" ? "cell bg-badref"
       : "cell bg-unknown";
}

// ---------- 状态轮询 ----------
async function poll() {
  try {
    const s = await (await fetch("/api/state")).json();
    $("status").textContent = s.status || "";
    setIfNotFocused("ip", s.ip);
    setIfNotFocused("rack", s.rack);
    setIfNotFocused("slot", s.slot);
    if (document.activeElement !== $("chkSignal")) $("chkSignal").checked = !!s.send_signal;
    if (document.activeElement !== $("chkAutoStart")) $("chkAutoStart").checked = !!s.auto_start;
    if (document.activeElement !== $("chkAutoSwitch")) $("chkAutoSwitch").checked = !!s.auto_switch;
    STATIONS.forEach(st => {
      const chk = $("chkUnlock" + SID(st));
      if (document.activeElement !== chk) {
        chk.checked = !!((s.unlock || {})[st]);
        $("btnSave" + SID(st)).disabled = !chk.checked;
      }
    });
    if (document.activeElement !== $("decimals")) $("decimals").value = s.decimals;

    // 配方 datalist 与当前配方 (按工位独立)
    STATIONS.forEach(st => {
      const names = ((s.recipes || {})[st]) || [];
      const dl = $("recipelist-" + st);
      const opts = names.join("|");
      if (dl.dataset.opts !== opts) {
        dl.innerHTML = "";
        names.forEach(n => {
          const o = document.createElement("option");
          o.value = n;
          dl.appendChild(o);
        });
        dl.dataset.opts = opts;
      }
      const inp = $("recipe-" + st);
      const cur = ((s.recipe || {})[st]) || "";
      if (document.activeElement !== inp && inp.value !== cur &&
          !names.includes(inp.value)) {
        inp.value = cur;
      }
      setIfNotFocused("recipeno-" + st, ((s.recipe_no || {})[st]) || "");
    });

    // 相机面板
    CAMERAS.forEach(([name, db]) => {
      const c = s.cameras ? s.cameras[db] : null;
      if (!c) return;
      VARS.forEach(v => {
        const cell = cells[db][v];
        if (c.values && c.values[v] !== undefined) {
          cell.val.textContent = Number(c.values[v]).toFixed(s.decimals);
        } else {
          cell.val.textContent = "----";
        }
        cell.dev.textContent = (c.dev_texts && c.dev_texts[v]) || "----";
        const cls = bgcls(c.states && c.states[v]);
        cell.val.className = cls;
        cell.dev.className = cls;
        if (document.activeElement !== cell.ref) cell.ref.value = (c.refs && c.refs[v]) || "";
        if (document.activeElement !== cell.th)  cell.th.value  = (c.ths  && c.ths[v])  || "";
      });
      lamps[db].className = "lamp " + (c.lamp || "idle");

      // PLC当前配方号(DB+6): 灰=无期望仅显示/未读到, 绿=与配方一致, 黄=不一致
      const rnoEl = $("rno-" + db);
      if (c.recipe_no === null || c.recipe_no === undefined) {
        rnoEl.textContent = "----";
        rnoEl.className = "cell bg-unknown";
      } else {
        rnoEl.textContent = c.recipe_no_text || String(c.recipe_no);
        rnoEl.className = "cell " + (c.recipe_no_state === "normal" ? "bg-normal"
                                     : c.recipe_no_state === "badref" ? "bg-badref"
                                     : "bg-unknown");
      }
    });

    drawTrends(s);
  } catch (e) {
    $("status").textContent = "Web服务连接失败: " + e;
  }
  setTimeout(poll, 200);
}

// ---------- 趋势图绘制(与 tkinter 版逻辑一致) ----------
function drawTrends(s) {
  CAMERAS.forEach(([name, db]) => {
    const c = s.cameras ? s.cameras[db] : null;
    if (!c) return;
    VARS.forEach(v => drawOne(trendCvs[db + "," + v], c, db, v, s));
  });
}

function drawOne(cv, c, db, v, s) {
  const ctx = cv.getContext("2d");
  const W = cv.width = cv.clientWidth || 200;
  const H = cv.height = cv.clientHeight || 95;
  const dec = s.decimals;
  const series = (s.history && s.history[db + "," + v]) || [];
  const thTxt = ((c.ths && c.ths[v]) || "0").trim();
  const th = parseFloat(thTxt);
  const thVal = isNaN(th) ? 0 : th;
  const refTxt = ((c.refs && c.refs[v]) || "").trim();
  const ref = refTxt === "" ? NaN : parseFloat(refTxt);

  ctx.clearRect(0, 0, W, H);
  ctx.font = "7px sans-serif";

  let ys = series.slice();
  if (!isNaN(ref)) ys.push(ref - thVal, ref + thVal);
  if (!ys.length) {
    ctx.fillStyle = "#888";
    ctx.textAlign = "center";
    ctx.fillText("暂无数据", W / 2, H / 2);
    return;
  }
  let yMin = Math.min.apply(null, ys), yMax = Math.max.apply(null, ys);
  if (yMax - yMin < 1e-9) { yMin -= 0.5; yMax += 0.5; }
  const mg = (yMax - yMin) * 0.1;
  yMin -= mg; yMax += mg;

  const padL = 40, padR = 8, padT = 10, padB = 4;
  const pw = W - padL - padR, ph = H - padT - padB;
  if (pw < 20 || ph < 20) return;
  const xOf = i => padL + pw * i / (HIST_LEN - 1);
  const yOf = val => padT + ph * (1 - (val - yMin) / (yMax - yMin));

  ctx.strokeStyle = "#999";
  ctx.lineWidth = 1;
  ctx.strokeRect(padL, padT, pw, ph);

  ctx.textAlign = "right";
  for (let i = 0; i < 3; i++) {
    const val = yMin + (yMax - yMin) * i / 2;
    const y = yOf(val);
    ctx.strokeStyle = "#f0f0f0";
    ctx.beginPath(); ctx.moveTo(padL, y); ctx.lineTo(padL + pw, y); ctx.stroke();
    ctx.fillStyle = "#555";
    ctx.fillText(val.toFixed(dec), padL - 3, y + 2);
  }

  if (!isNaN(ref)) {
    ctx.strokeStyle = VCOLOR[v];
    ctx.setLineDash([4, 3]);
    ctx.beginPath(); ctx.moveTo(padL, yOf(ref)); ctx.lineTo(padL + pw, yOf(ref)); ctx.stroke();
    ctx.setLineDash([2, 3]);
    ctx.strokeStyle = "#bbbbbb";
    [ref - thVal, ref + thVal].forEach(b => {
      if (b > yMin && b < yMax) {
        ctx.beginPath(); ctx.moveTo(padL, yOf(b)); ctx.lineTo(padL + pw, yOf(b)); ctx.stroke();
      }
    });
    ctx.setLineDash([]);
  }

  const start = HIST_LEN - series.length;
  ctx.lineWidth = 1.2;
  for (let i = 1; i < series.length; i++) {
    const alarm = !isNaN(ref) && Math.abs(series[i] - ref) > thVal;
    ctx.strokeStyle = alarm ? "#cc2222" : VCOLOR[v];
    ctx.beginPath();
    ctx.moveTo(xOf(start + i - 1), yOf(series[i - 1]));
    ctx.lineTo(xOf(start + i), yOf(series[i]));
    ctx.stroke();
  }

  if (series.length) {
    ctx.fillStyle = VCOLOR[v];
    ctx.font = "bold 8px sans-serif";
    ctx.textAlign = "right";
    ctx.fillText(series[series.length - 1].toFixed(dec), padL + pw - 3, padT + 8);
  }
}

poll();
</script>
</body>
</html>
"""


class WebHandler(BaseHTTPRequestHandler):
    """内嵌 Web 服务请求处理: GET / 返回页面, GET /api/state 返回快照,
    POST /api/<指令> 将操作转入主线程指令队列"""

    server_version = "CameraMonitorHTTP/1.0"

    def log_message(self, fmt, *args):
        pass   # 静默访问日志, 避免10ms级轮询刷爆控制台

    @property
    def app(self):
        return self.server.app

    def _send(self, body, ctype):
        if isinstance(body, str):
            body = body.encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def _send_json(self, obj, code=200):
        body = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def _read_json(self):
        length = int(self.headers.get("Content-Length") or 0)
        if length <= 0:
            return {}
        return json.loads(self.rfile.read(length).decode("utf-8"))

    def do_GET(self):
        try:
            if self.path in ("/", "/index.html"):
                self._send(INDEX_HTML, "text/html; charset=utf-8")
            elif self.path == "/api/state":
                self._send_json(self.app.web_snapshot())
            else:
                self._send_json({"error": "not found"}, 404)
        except (BrokenPipeError, ConnectionResetError):
            pass
        except Exception as e:
            try:
                self._send_json({"error": str(e)}, 500)
            except Exception:
                pass

    def do_POST(self):
        try:
            name = self.path[len("/api/"):].replace("/", "_") \
                if self.path.startswith("/api/") else ""
            actions = {"connect", "disconnect", "start", "stop", "signal",
                       "decimals", "clear_history", "recipe_apply",
                       "recipe_save", "set_params", "unlock", "recipe_no",
                       "auto_start", "auto_switch", "recipe_select"}
            if name not in actions:
                self._send_json({"error": "unknown action"}, 404)
                return
            payload = self._read_json()
            self.app.web_queue.put((name, payload))
            self._send_json({"ok": True})
        except (BrokenPipeError, ConnectionResetError):
            pass
        except Exception as e:
            try:
                self._send_json({"error": str(e)}, 500)
            except Exception:
                pass


class CameraFrame(ttk.LabelFrame):
    """一个相机的监控面板: 4 行 变量/当前值/偏差/参考值/阈值"""

    def __init__(self, master, title, db_no, field_cb=None):
        super().__init__(master, text="%s (DB%d)" % (title, db_no))
        self.db_no = db_no
        self.value_labels = {}   # name -> Label 当前值
        self.dev_labels = {}     # name -> Label 偏差
        self.ref_vars = {}       # name -> StringVar 参考值
        self.th_vars = {}        # name -> StringVar 该变量独立的报警阈值

        # 表头
        for col, text in enumerate(["变量", "当前值", "偏差", "参考坐标", "阈值±"]):
            ttk.Label(self, text=text, font=("", 9, "bold")).grid(
                row=0, column=col, padx=4, pady=2)
        ttk.Label(self, text="触发", font=("", 9, "bold")).grid(
            row=0, column=5, padx=4, pady=2)
        # 触发信号灯: 灰=无触发, 黄=已接收触发待发送(1秒窗口), 绿=已发送OK/NG
        self.trigger_lamp = tk.Label(self, text="", width=3, bg=BG_UNKNOWN,
                                     relief="solid", bd=1)
        self.trigger_lamp.grid(row=1, column=5, padx=4, pady=2,
                               rowspan=len(VARIABLES), sticky="ns")

        # PLC当前配方号: DB偏移量6(INT), 与所属工位当前配方的配方号比对
        rno_row = len(VARIABLES) + 1
        ttk.Label(self, text="配方号", font=("", 9, "bold")).grid(
            row=rno_row, column=0, padx=4, pady=2)
        self.recipe_no_label = tk.Label(self, text="----", width=14,
                                        bg=BG_UNKNOWN, relief="solid", bd=1)
        self.recipe_no_label.grid(row=rno_row, column=1, padx=4, pady=2)
        ttk.Label(self, text="(DB+%d INT)" % RECIPE_NO_OFFSET,
                  foreground="#777").grid(row=rno_row, column=2, padx=4,
                                          pady=2, sticky="w")

        # 4 个变量行, 每行各自带阈值输入框
        for row, (name, offset, size) in enumerate(VARIABLES, start=1):
            ttk.Label(self, text=name, font=("", 10, "bold")).grid(
                row=row, column=0, padx=4, pady=2)

            val = tk.Label(self, text="----", width=12, bg=BG_UNKNOWN,
                           relief="solid", bd=1)
            val.grid(row=row, column=1, padx=4, pady=2)
            self.value_labels[name] = val

            dev = tk.Label(self, text="----", width=12, bg=BG_UNKNOWN,
                           relief="solid", bd=1)
            dev.grid(row=row, column=2, padx=4, pady=2)
            self.dev_labels[name] = dev

            var = tk.StringVar()
            ref = ttk.Entry(self, textvariable=var, width=12)
            ref.grid(row=row, column=3, padx=4, pady=2)
            self.ref_vars[name] = var

            th_var = tk.StringVar(value="0.1")
            th_entry = ttk.Entry(self, textvariable=th_var, width=8)
            th_entry.grid(row=row, column=4, padx=4, pady=2)
            self.th_vars[name] = th_var

        self.grid_columnconfigure(3, weight=1)

        # 参考值/阈值变化时通知主窗口(用于镜像到 Web 状态)
        if field_cb is not None:
            for n, var in self.ref_vars.items():
                var.trace_add("write",
                              lambda *a, _n=n: field_cb(db_no, "ref", _n))
            for n, var in self.th_vars.items():
                var.trace_add("write",
                              lambda *a, _n=n: field_cb(db_no, "th", _n))

    def get_threshold(self, name):
        """获取指定变量的报警阈值, 非法输入按 0 处理"""
        try:
            return float(self.th_vars[name].get().strip() or "0")
        except ValueError:
            return 0.0

    def has_alarm(self, data):
        """判断本次采样是否存在任一变量超出各自阈值 (有参考值才参与判断)"""
        for name, value in data.items():
            text = self.ref_vars[name].get().strip()
            if not text:
                continue
            try:
                ref = float(text)
            except ValueError:
                continue
            if abs(value - ref) > self.get_threshold(name):
                return True
        return False

    def update_data(self, data, decimals=2):
        """更新显示并判断报警 (每个变量使用各自阈值)
        返回 (states, dev_texts): 各变量状态(normal/alarm/badref/unknown)
        与偏差显示文本, 供 Web 界面镜像显示"""
        states, dev_texts = {}, {}
        for name, value in data.items():
            th = self.get_threshold(name)
            self.value_labels[name].config(text="%.*f" % (decimals, value))
            states[name] = "unknown"
            dev_texts[name] = "----"
            text = self.ref_vars[name].get().strip()
            if text:
                try:
                    ref = float(text)
                    dev = value - ref
                    dev_text = "%+.*f" % (decimals, dev)
                    dev_texts[name] = dev_text
                    if abs(dev) > th:
                        self.dev_labels[name].config(text=dev_text, bg=BG_ALARM)
                        self.value_labels[name].config(bg=BG_ALARM)
                        states[name] = "alarm"
                    else:
                        self.dev_labels[name].config(text=dev_text, bg=BG_NORMAL)
                        self.value_labels[name].config(bg=BG_NORMAL)
                        states[name] = "normal"
                except ValueError:
                    self.dev_labels[name].config(text="参考非法", bg=BG_BADREF)
                    self.value_labels[name].config(bg=BG_UNKNOWN)
                    dev_texts[name] = "参考非法"
                    states[name] = "badref"
            else:
                self.dev_labels[name].config(text="----", bg=BG_UNKNOWN)
                self.value_labels[name].config(bg=BG_UNKNOWN)
        return states, dev_texts

    def apply_recipe(self, params):
        """将配方参数填入面板 (params: {X0, X0_threshold, ...})
        兼容旧格式: 无 X0_threshold 时回退到全局 threshold"""
        global_th = params.get("threshold")
        for name, var in self.ref_vars.items():
            if name in params:
                var.set(str(params[name]))
            th_key = name + "_threshold"
            if th_key in params:
                self.th_vars[name].set(str(params[th_key]))
            elif global_th is not None:
                self.th_vars[name].set(str(global_th))

    def collect_recipe(self):
        """收集当前面板上的参考值与每个变量的阈值"""
        params = {}
        for name, var in self.ref_vars.items():
            text = var.get().strip()
            if text:
                try:
                    params[name] = float(text)
                except ValueError:
                    pass
            params[name + "_threshold"] = self.get_threshold(name)
        return params

    def update_recipe_no(self, value, expect):
        """更新PLC当前配方号显示 (value=None表示未读到)
        expect=None: 无期望值仅显示原值(灰); 相等=绿; 不等=黄并提示期望值"""
        if value is None:
            self.recipe_no_label.config(text="----", bg=BG_UNKNOWN)
            return
        if expect is None:
            self.recipe_no_label.config(text=str(value), bg=BG_UNKNOWN)
        elif value == expect:
            self.recipe_no_label.config(text=str(value), bg=BG_NORMAL)
        else:
            self.recipe_no_label.config(text="%d (应为%d)" % (value, expect),
                                        bg=BG_BADREF)

    def set_trigger_lamp(self, state):
        """触发信号灯: idle=灰, pending=黄(等待发送窗口), sent=绿(已发送)"""
        colors = {"idle": BG_UNKNOWN, "pending": "#ffcc00", "sent": "#66dd66"}
        self.trigger_lamp.config(bg=colors.get(state, BG_UNKNOWN))

    def reset_unknown(self):
        for name in self.value_labels:
            self.value_labels[name].config(text="----", bg=BG_UNKNOWN)
            self.dev_labels[name].config(text="----", bg=BG_UNKNOWN)
        self.set_trigger_lamp("idle")
        self.recipe_no_label.config(text="----", bg=BG_UNKNOWN)


class MainWindow(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("相机数据监控器 - S7 %s" % PLC_IP)
        self.geometry("960x760")

        self.client = snap7.client.Client()
        self.connected = False
        self.job = None   # after 定时器 id
        self.history = {}   # (db号, 变量名) -> [采样值...]
        # 拍照触发状态(按DB号): last=上次触发值, pending=上升沿待发送,
        # due=允许发送的时间戳, sent=本次上升沿是否已发送
        self.trigger_state = {}
        # 自动切换配方记忆(按工位): 上次处理过的配方号, 变化才触发切换
        self._auto_switch_last = {"left": None, "right": None}
        self._last_trend_redraw = 0.0   # 上次趋势图重绘时间戳

        self._build_top()
        self._build_recipe_bar()
        self._build_groups()
        self._build_trend()

        self.status_var = tk.StringVar(value="未连接")
        tk.Label(self, textvariable=self.status_var, anchor="w",
                 relief="sunken", bd=1).pack(side="bottom", fill="x")

        self.protocol("WM_DELETE_WINDOW", self.on_close)

        self.init_recipes()
        self._init_web()          # 启动内嵌 Web 服务 (0.0.0.0:10209)
        self._web_drain_loop()    # 持续清空 Web 指令队列

        # 自动开始监控: 勾选后启动时先ping PLC IP, 可达才连接并开始监控
        if self.auto_start_var.get():
            self._auto_start_monitor()

    # ---------------- Web 服务 ----------------
    def _init_web(self):
        """启动内嵌 Web 服务: 0.0.0.0:10209, 提供与 tkinter 界面相同功能的
        HTML 页面。Web 线程只读写 web_state(加锁), 不直接访问 Tkinter 控件;
        页面操作经 web_queue 转交主线程执行。"""
        self.web_lock = threading.Lock()
        self.web_queue = queue.Queue()
        self.web_state = {
            "connected": False,
            "monitoring": False,
            "status": self.status_var.get(),
            "ip": self.ip_var.get(),
            "rack": self.rack_var.get(),
            "slot": self.slot_var.get(),
            "send_signal": bool(self.send_signal_var.get()),
            "auto_start": bool(self.auto_start_var.get()),
            "auto_switch": bool(self.auto_switch_var.get()),
            "decimals": self.get_decimals(),
            "recipes": self._web_recipes(),
            "recipe": {"left": self.recipe_vars["left"].get(),
                       "right": self.recipe_vars["right"].get()},
            "recipe_no": {"left": self.recipe_no_vars["left"].get(),
                          "right": self.recipe_no_vars["right"].get()},
            "unlock": {"left": False, "right": False},
            "cameras": {},
            "history": {},   # "db号,变量名" -> [采样值...]
        }
        for _name, db in CAMERAS:
            cam = {"values": {}, "states": {}, "dev_texts": {},
                   "refs": {}, "ths": {}, "lamp": "idle",
                   "recipe_no": None, "recipe_no_text": "----",
                   "recipe_no_state": "unknown"}
            g = self._group_by_db(db)
            if g is not None:
                for v in g.ref_vars:
                    cam["refs"][v] = g.ref_vars[v].get()
                    cam["ths"][v] = g.th_vars[v].get()
            self.web_state["cameras"][db] = cam

        # 界面变量变化时镜像到 web_state (Web 线程据此生成快照)
        self.ip_var.trace_add("write",
            lambda *_: self._set_web("ip", self.ip_var.get()))
        self.rack_var.trace_add("write",
            lambda *_: self._set_web("rack", self.rack_var.get()))
        self.slot_var.trace_add("write",
            lambda *_: self._set_web("slot", self.slot_var.get()))
        self.send_signal_var.trace_add("write", lambda *_:
            self._set_web("send_signal", bool(self.send_signal_var.get())))
        self.auto_start_var.trace_add("write", lambda *_:
            self._set_web("auto_start", bool(self.auto_start_var.get())))
        self.auto_switch_var.trace_add("write", lambda *_:
            self._set_web("auto_switch", bool(self.auto_switch_var.get())))
        self.decimals_var.trace_add("write",
            lambda *_: self._set_web("decimals", self.get_decimals()))
        for st in ("left", "right"):
            self.recipe_vars[st].trace_add("write", lambda *_a, _s=st:
                self._set_web_recipe(_s, self.recipe_vars[_s].get()))
            self.recipe_no_vars[st].trace_add("write", lambda *_a, _s=st:
                self._set_web("recipe_no",
                              {**self.web_state["recipe_no"],
                               _s: self.recipe_no_vars[_s].get()}))
        self.status_var.trace_add("write",
            lambda *_: self._set_web("status", self.status_var.get()))

        try:
            httpd = ThreadingHTTPServer((WEB_HOST, WEB_PORT), WebHandler)
            httpd.app = self
            threading.Thread(target=httpd.serve_forever, daemon=True,
                             name="camera-web").start()
            self._web_httpd = httpd
            self.status_var.set(
                "%s  |  Web页面: http://<本机IP>:%d/" % (self.status_var.get(), WEB_PORT))
        except Exception as e:
            self._web_httpd = None
            print("Web服务启动失败 (%s:%d): %s" % (WEB_HOST, WEB_PORT, e))

    def _set_web(self, key, value):
        if getattr(self, "web_lock", None) is None:
            return
        with self.web_lock:
            self.web_state[key] = value

    def _on_web_field(self, db_no, kind, name):
        """CameraFrame 参考值/阈值输入框变化时镜像到 web_state
        注意: trace 可能在 _init_web() 之前(如启动应用配方时)触发,
        此处必须做空值保护"""
        web_lock = getattr(self, "web_lock", None)
        if web_lock is None:
            return
        g = self._group_by_db(db_no)
        if g is None:
            return
        var = (g.ref_vars if kind == "ref" else g.th_vars)[name]
        with web_lock:
            self.web_state["cameras"][db_no]["refs" if kind == "ref"
                                             else "ths"][name] = var.get()

    def _group_by_db(self, db_no):
        for g in self.groups:
            if g.db_no == db_no:
                return g
        return None

    def web_snapshot(self):
        """供 Web 线程读取的线程安全状态快照 (JSON 可序列化)"""
        with self.web_lock:
            cams = {}
            for db, cam in self.web_state["cameras"].items():
                cams[db] = {
                    "values": dict(cam["values"]),
                    "states": dict(cam["states"]),
                    "dev_texts": dict(cam["dev_texts"]),
                    "refs": dict(cam["refs"]),
                    "ths": dict(cam["ths"]),
                    "lamp": cam["lamp"],
                    "recipe_no": cam["recipe_no"],
                    "recipe_no_text": cam["recipe_no_text"],
                    "recipe_no_state": cam["recipe_no_state"],
                }
            return {
                "connected": self.web_state["connected"],
                "monitoring": self.web_state["monitoring"],
                "status": self.web_state["status"],
                "ip": self.web_state["ip"],
                "rack": self.web_state["rack"],
                "slot": self.web_state["slot"],
                "send_signal": self.web_state["send_signal"],
                "auto_start": self.web_state["auto_start"],
                "auto_switch": self.web_state["auto_switch"],
                "unlock": dict(self.web_state["unlock"]),
                "decimals": self.web_state["decimals"],
                "recipes": {k: list(v)
                            for k, v in self.web_state["recipes"].items()},
                "recipe": dict(self.web_state["recipe"]),
                "recipe_no": dict(self.web_state["recipe_no"]),
                "cameras": cams,
                "history": {k: list(v)
                            for k, v in self.web_state["history"].items()},
            }

    def _web_drain_loop(self):
        """每 50ms 清空 Web 指令队列 (Tkinter 控件只能在主线程操作)"""
        try:
            while True:
                cmd, payload = self.web_queue.get_nowait()
                self._handle_web_command(cmd, payload)
        except queue.Empty:
            pass
        self.after(50, self._web_drain_loop)

    @staticmethod
    def _web_station(p):
        """取 Web 指令中的工位字段, 缺省/非法时回退到左工位"""
        st = p.get("station")
        return st if st in ("left", "right") else "left"

    def _handle_web_command(self, cmd, p):
        """在主线程执行 Web 页面发来的指令"""
        try:
            if cmd == "connect":
                self.ip_var.set(str(p.get("ip", self.ip_var.get())))
                self.rack_var.set(str(p.get("rack", self.rack_var.get())))
                self.slot_var.set(str(p.get("slot", self.slot_var.get())))
                if not self.connected:
                    self.do_connect()
            elif cmd == "disconnect":
                self.do_disconnect()
            elif cmd == "start":
                self.do_start()
            elif cmd == "stop":
                self.do_stop()
            elif cmd == "signal":
                self.send_signal_var.set(bool(p.get("enabled")))
            elif cmd == "auto_start":
                self.auto_start_var.set(bool(p.get("enabled")))
                self.on_option_toggle()
            elif cmd == "auto_switch":
                self.auto_switch_var.set(bool(p.get("enabled")))
                self.on_option_toggle()
            elif cmd == "unlock":
                st = self._web_station(p)
                self.unlock_vars[st].set(bool(p.get("enabled")))
            elif cmd == "recipe_no":
                st = self._web_station(p)
                self.recipe_no_vars[st].set(str(p.get("value", "")))
            elif cmd == "decimals":
                self.decimals_var.set(str(p.get("value", 2)))
            elif cmd == "clear_history":
                self.clear_history()
            elif cmd == "recipe_select":
                # 页面选择配方名: 仅回填配方号输入框, 不应用配方
                st = self._web_station(p)
                self.recipe_vars[st].set(str(p.get("name", "")))
            elif cmd == "recipe_apply":
                st = self._web_station(p)
                self.recipe_vars[st].set(str(p.get("name", "")))
                self.apply_recipe(st)
            elif cmd == "recipe_save":
                st = self._web_station(p)
                self.recipe_vars[st].set(str(p.get("name", "")))
                self.save_recipe(st)
            elif cmd == "set_params":
                # p: {"cameras": {"92": {"X0": {"ref": "98.1", "th": "0.1"}, ...}}}
                for db_s, varmap in (p.get("cameras") or {}).items():
                    g = self._group_by_db(int(db_s))
                    if g is None:
                        continue
                    for var, pp in (varmap or {}).items():
                        if var not in g.ref_vars or not isinstance(pp, dict):
                            continue
                        if "ref" in pp:
                            g.ref_vars[var].set(str(pp["ref"]))
                        if "th" in pp:
                            g.th_vars[var].set(str(pp["th"]))
        except Exception as e:
            self.status_var.set("Web指令 %s 失败: %s" % (cmd, e))

    # ---------------- 配方 (左右工位独立) ----------------
    def init_recipes(self):
        """启动时按工位加载配方; 无配置文件则创建并写入默认配方"""
        try:
            ensure_config_file()
            self.recipes, self.default_recipes, self.options = load_recipes()
        except Exception as e:
            self.recipes = {"left": {}, "right": {}}
            self.default_recipes = {"left": "", "right": ""}
            self.options = {"auto_start": False, "auto_switch_recipe": False}
            messagebox.showwarning("配方加载失败",
                                   "读取 %s 失败: %s" % (CONFIG_FILE, e))
        # 持久化选项回填到勾选框
        self.auto_start_var.set(bool(self.options.get("auto_start")))
        self.auto_switch_var.set(bool(self.options.get("auto_switch_recipe")))
        for st in ("left", "right"):
            names = sorted(self.recipes.get(st, {}))
            self.recipe_combos[st]["values"] = names
            default = self.default_recipes.get(st, "")
            if default in self.recipes.get(st, {}):
                self.recipe_vars[st].set(default)
            elif names:
                self.recipe_vars[st].set(names[0])
            if self.recipe_vars[st].get():
                self.apply_recipe(st)

    def _on_recipe_name_changed(self, station):
        """配方名下拉框选择/输入变化: 若该配方存在, 立即回填其配方号"""
        recipes = getattr(self, "recipes", None)
        if not recipes:
            return
        name = self.recipe_vars[station].get()
        entry = recipes.get(station, {}).get(name)
        if not entry:
            return
        rno = entry.get("recipe_no")
        self.recipe_no_vars[station].set("" if rno is None else str(rno))

    def apply_recipe(self, station):
        """应用指定工位的配方: 只影响该工位自己的两个相机面板,
        同时回填该配方保存的配方号(无则清空输入框)"""
        name = self.recipe_vars[station].get()
        entry = self.recipes.get(station, {}).get(name, {})
        cameras = entry.get("cameras", {})
        for g in self.station_groups.get(station, []):
            params = cameras.get(str(g.db_no))
            if params:
                g.apply_recipe(params)
        rno = entry.get("recipe_no")
        self.recipe_no_vars[station].set("" if rno is None else str(rno))
        self.status_var.set("已应用%s配方: %s" % (STATION_NAMES[station], name))
        self.redraw_trend()

    def save_recipe(self, station):
        """以输入框中的名称保存指定工位的配方: 已存在则覆盖修改, 否则创建"""
        name = (self.recipe_vars[station].get() or "").strip()
        if not name:
            messagebox.showwarning("提示", "请输入%s配方名称" % STATION_NAMES[station])
            return
        existed = name in self.recipes.get(station, {})
        cameras = {}
        for g in self.station_groups.get(station, []):
            cameras[str(g.db_no)] = g.collect_recipe()
        entry = {"cameras": cameras}
        # 配方号(外键为配方名): 整数, 留空表示该配方不校验配方号
        rno_text = self.recipe_no_vars[station].get().strip()
        if rno_text:
            try:
                entry["recipe_no"] = int(rno_text)
            except ValueError:
                messagebox.showwarning("提示", "配方号必须为整数")
                return
        self.recipes.setdefault(station, {})[name] = entry
        # 该工位尚无默认配方时, 将本次保存的名称设为默认
        if not self.default_recipes.get(station):
            self.default_recipes[station] = name
        try:
            save_recipes(self.recipes, self.default_recipes)
        except Exception as e:
            messagebox.showwarning("保存失败", "写入 %s 失败: %s" % (CONFIG_FILE, e))
            return
        if name not in self.recipe_combos[station]["values"]:
            self.recipe_combos[station]["values"] = sorted(self.recipes[station])
        self.recipe_vars[station].set(name)
        if hasattr(self, "web_state"):
            self._set_web("recipes", self._web_recipes())
        self.status_var.set("已%s%s配方: %s"
                            % ("修改" if existed else "保存新",
                               STATION_NAMES[station], name))
        # 保存成功后自动取消勾选, 重新锁定保存按钮, 防止连续误操作
        self.unlock_vars[station].set(False)

    def _on_unlock_changed(self, station):
        """解锁勾选框变化: 控制该工位保存配方按钮的禁用状态并镜像到 Web"""
        unlocked = bool(self.unlock_vars[station].get())
        if station in getattr(self, "btn_save_recipes", {}):
            self.btn_save_recipes[station].config(
                state="normal" if unlocked else "disabled")
        if getattr(self, "web_lock", None) is None:
            return
        with self.web_lock:
            self.web_state["unlock"][station] = unlocked

    def on_option_toggle(self):
        """自动开始监控/自动切换配方勾选框变化: 持久化到配置文件并镜像Web,
        重新启用自动切换时清空记忆, 下一轮轮询立即按当前配方号同步一次"""
        self.options["auto_start"] = bool(self.auto_start_var.get())
        self.options["auto_switch_recipe"] = bool(self.auto_switch_var.get())
        if self.options["auto_switch_recipe"]:
            self._auto_switch_last = {"left": None, "right": None}
        try:
            save_recipes(self.recipes, self.default_recipes, self.options)
            self.status_var.set(
                "选项已保存: 自动开始监控=%s, 自动切换配方=%s"
                % ("开" if self.options["auto_start"] else "关",
                   "开" if self.options["auto_switch_recipe"] else "关"))
        except Exception as e:
            self.status_var.set("保存选项失败: %s" % e)

    def _web_recipes(self):
        """各工位配方名列表(供 Web 快照)"""
        return {"left": sorted(self.recipes.get("left", {})),
                "right": sorted(self.recipes.get("right", {}))}

    def _switch_recipe_by_no(self, station, rno_val):
        """按配方号切换指定工位配方: 在该工位配方中查找recipe_no匹配的配方,
        无匹配时回退默认配方149#K; 应用后配方号输入框自动回填, 与PLC读数一致"""
        target = None
        for name, entry in self.recipes.get(station, {}).items():
            if entry.get("recipe_no") == rno_val:
                target = name
                break
        if target is None:
            target = (FALLBACK_RECIPE
                      if FALLBACK_RECIPE in self.recipes.get(station, {})
                      else None)
        if target is None:
            self.status_var.set("%s: 配方号%d无匹配配方, 且回退配方%s不存在"
                                % (STATION_NAMES[station], rno_val,
                                   FALLBACK_RECIPE))
            return
        if self.recipe_vars[station].get() != target:
            self.recipe_vars[station].set(target)
        self.apply_recipe(station)
        self.status_var.set("%s: 配方号%d -> 自动切换配方: %s"
                            % (STATION_NAMES[station], rno_val, target))

    def _expected_recipe_no(self, station):
        """取该工位当前配方的期望配方号: 空/非法返回None(不比对, 仅显示)"""
        if station not in ("left", "right"):
            return None
        try:
            t = self.recipe_no_vars[station].get().strip()
            return int(t) if t else None
        except ValueError:
            return None

    def _set_web_recipe(self, station, name):
        """镜像指定工位的当前配方名到 Web 状态"""
        if getattr(self, "web_lock", None) is None:
            return
        with self.web_lock:
            self.web_state["recipe"][station] = name

    # ---------------- 界面 ----------------
    def _build_top(self):
        bar = ttk.Frame(self)
        bar.pack(side="top", fill="x", padx=6, pady=4)

        ttk.Label(bar, text="PLC IP:").pack(side="left")
        self.ip_var = tk.StringVar(value=PLC_IP)
        ttk.Entry(bar, textvariable=self.ip_var, width=14).pack(side="left", padx=2)

        ttk.Label(bar, text="Rack:").pack(side="left", padx=(8, 0))
        self.rack_var = tk.StringVar(value=str(PLC_RACK))
        ttk.Entry(bar, textvariable=self.rack_var, width=4).pack(side="left", padx=2)

        ttk.Label(bar, text="Slot:").pack(side="left", padx=(8, 0))
        self.slot_var = tk.StringVar(value=str(PLC_SLOT))
        ttk.Entry(bar, textvariable=self.slot_var, width=4).pack(side="left", padx=2)

        self.btn_connect = ttk.Button(bar, text="连 接", command=self.do_connect)
        self.btn_connect.pack(side="left", padx=6)
        self.btn_disconnect = ttk.Button(bar, text="断 开", command=self.do_disconnect,
                                         state="disabled")
        self.btn_disconnect.pack(side="left")

        self.btn_start = ttk.Button(bar, text="开始监控", command=self.do_start)
        self.btn_start.pack(side="left", padx=(24, 0))
        self.btn_stop = ttk.Button(bar, text="停止监控", command=self.do_stop,
                                   state="disabled")
        self.btn_stop.pack(side="left", padx=6)

        # 发送OK/NG信号: 勾选后每次轮询向各相机自身DB块偏移量2(INT)写 2=NG / 3=OK
        self.send_signal_var = tk.BooleanVar(value=False)
        ttk.Checkbutton(bar, text="发送OK/NG信号",
                        variable=self.send_signal_var).pack(side="left", padx=(24, 0))

        # 自动开始监控: 勾选(持久化)后启动时先ping PLC IP,
        # 可达才自动连接并开始监控, 不可达则跳过
        self.auto_start_var = tk.BooleanVar(value=False)
        ttk.Checkbutton(bar, text="自动开始监控",
                        variable=self.auto_start_var,
                        command=self.on_option_toggle).pack(side="left", padx=(12, 0))

        # 自动切换配方: 勾选(持久化)后按DB97(左工位)/DB98(右工位)的
        # 配方号自动切换对应工位配方, 无匹配回退默认配方149#K
        self.auto_switch_var = tk.BooleanVar(value=False)
        ttk.Checkbutton(bar, text="自动切换配方",
                        variable=self.auto_switch_var,
                        command=self.on_option_toggle).pack(side="left", padx=(12, 0))

    def _build_recipe_bar(self):
        """左右工位各自独立的配方栏: 每个工位有自己的配方下拉框、
        应用/保存按钮和"解锁"勾选框(勾选后保存按钮才可用, 防止误点击)"""
        bar = ttk.Frame(self)
        bar.pack(side="top", fill="x", padx=6, pady=2)

        self.recipe_vars = {}       # 工位 -> StringVar 当前配方名
        self.recipe_no_vars = {}    # 工位 -> StringVar 配方号(与配方名绑定)
        self.unlock_vars = {}       # 工位 -> BooleanVar 解锁勾选
        self.btn_save_recipes = {}  # 工位 -> 保存配方按钮
        self.recipe_combos = {}     # 工位 -> 配方下拉框

        for idx, st in enumerate(("left", "right")):
            ttk.Label(bar, text="%s配方:" % STATION_NAMES[st],
                      font=("", 10, "bold")).pack(side="left",
                                                  padx=(6 if idx == 0 else 16, 2))
            var = tk.StringVar()
            combo = ttk.Combobox(bar, textvariable=var, width=14)
            combo.pack(side="left", padx=2)
            # 配方号输入框: 与配方名绑定(外键), 保存时随配方入库,
            # 应用时回填; 轮询将其与PLC DB偏移量6的读数比对
            ttk.Label(bar, text="配方号:").pack(side="left", padx=(6, 0))
            rno_var = tk.StringVar()
            ttk.Entry(bar, textvariable=rno_var, width=6).pack(side="left", padx=2)
            self.recipe_no_vars[st] = rno_var
            ttk.Button(bar, text="应用配方",
                       command=lambda s=st: self.apply_recipe(s)).pack(side="left", padx=2)
            # 保存配方默认禁用, 勾选"解锁"后才允许点击, 防止误点击覆盖配方
            btn_save = ttk.Button(bar, text="保存配方",
                                  command=lambda s=st: self.save_recipe(s),
                                  state="disabled")
            btn_save.pack(side="left", padx=2)
            unlock = tk.BooleanVar(value=False)
            unlock.trace_add("write",
                             lambda *_a, _s=st: self._on_unlock_changed(_s))
            ttk.Checkbutton(bar, text="解锁", variable=unlock).pack(side="left", padx=2)

            # 选择/输入配方名时立即回填该配方保存的配方号
            # (不依赖"应用配方"按钮, 防止选择后配方号输入框不更新)
            var.trace_add("write",
                          lambda *_a, _s=st: self._on_recipe_name_changed(_s))
            self.recipe_vars[st] = var
            self.unlock_vars[st] = unlock
            self.btn_save_recipes[st] = btn_save
            self.recipe_combos[st] = combo

    def _build_groups(self):
        body = ttk.Frame(self)
        body.pack(side="top", fill="both", expand=True, padx=6, pady=4)
        self.groups = []
        self.station_groups = {"left": [], "right": []}
        for i, (name, db) in enumerate(CAMERAS):
            g = CameraFrame(body, name, db, field_cb=self._on_web_field)
            g.grid(row=i // 2, column=i % 2, padx=6, pady=6, sticky="nsew")
            self.groups.append(g)
            # 前两个相机为左工位, 后两个为右工位 (与 STATION_DBS 一致)
            st = "left" if i < 2 else "right"
            g.station = st
            self.station_groups[st].append(g)
        body.grid_rowconfigure(0, weight=1)
        body.grid_rowconfigure(1, weight=1)
        body.grid_columnconfigure(0, weight=1)
        body.grid_columnconfigure(1, weight=1)

    def _build_trend(self):
        """趋势图: 4 相机 x 4 变量共 16 格(4x4), 每个变量独立折线图,
        仅用 tkinter Canvas(标准库)绘制, 虚线基线为参考坐标, 红色段为超差"""
        box = ttk.LabelFrame(self, text="趋势图 (4相机 x 4变量, 虚线基线 = 参考坐标, 红色段 = 超差)")
        box.pack(side="top", fill="both", padx=6, pady=4)

        bar = ttk.Frame(box)
        bar.pack(side="top", fill="x")
        ttk.Button(bar, text="清除历史", command=self.clear_history).pack(side="left", padx=4)
        ttk.Label(bar, text="小数位数:").pack(side="left", padx=(12, 2))
        self.decimals_var = tk.StringVar(value="2")
        dec_spin = ttk.Spinbox(bar, textvariable=self.decimals_var, width=3,
                               from_=0, to=6, command=self.on_decimals_change)
        dec_spin.pack(side="left")
        self.decimals_var.trace_add("write", lambda *_: self.on_decimals_change())
        self.trend_info = tk.Label(bar, text="", fg="#555")
        self.trend_info.pack(side="left", padx=8)

        grid = ttk.Frame(box)
        grid.pack(side="top", fill="both", expand=True, padx=4, pady=4)
        self.trend_canvases = {}   # (db号, 变量名) -> Canvas
        for ci, (cam_name, db) in enumerate(CAMERAS):
            for vi, (var_name, _off, _sz) in enumerate(VARIABLES):
                sub = ttk.LabelFrame(grid, text="%s %s (DB%d)"
                                     % (cam_name, var_name, db))
                sub.grid(row=ci, column=vi, padx=3, pady=3, sticky="nsew")
                cv = tk.Canvas(sub, height=95, bg="#ffffff",
                               highlightthickness=1, highlightbackground="#999")
                cv.pack(side="top", fill="both", expand=True)
                self.trend_canvases[(db, var_name)] = cv
        for r in range(len(CAMERAS)):
            grid.grid_rowconfigure(r, weight=1)
        for c in range(len(VARIABLES)):
            grid.grid_columnconfigure(c, weight=1)

    def get_decimals(self):
        try:
            return max(0, min(6, int(self.decimals_var.get().strip())))
        except (ValueError, tk.TclError):
            return 2

    def on_decimals_change(self):
        """小数位数变化: 立即重绘趋势图, 下轮轮询刷新数值显示"""
        if hasattr(self, "trend_canvases"):
            self.redraw_trend()

    # ---------------- 趋势图 ----------------
    def clear_history(self):
        self.history.clear()
        if hasattr(self, "web_state"):
            with self.web_lock:
                self.web_state["history"].clear()
        self.redraw_trend()

    def record_history(self, db_no, data):
        batch = {}
        for name, value in data.items():
            key = (db_no, name)
            series = self.history.setdefault(key, [])
            series.append(value)
            if len(series) > HISTORY_LEN:
                del series[:len(series) - HISTORY_LEN]
            batch["%d,%s" % (db_no, name)] = value
        # 同步镜像到 Web 历史(线程安全)
        with self.web_lock:
            for k, v in batch.items():
                s = self.web_state["history"].setdefault(k, [])
                s.append(v)
                if len(s) > HISTORY_LEN:
                    del s[:len(s) - HISTORY_LEN]

    def redraw_trend(self):
        # 变量颜色, 基线为同色虚线
        colors = {"X0": "#2266cc", "Y0": "#22aa44",
                  "X1": "#dd8800", "Y1": "#8844cc"}
        for g in self.groups:
            for name, color in colors.items():
                self._draw_one_trend(g, name, color)
        # 状态栏摘要: 最后采样点的偏差
        infos = []
        dec = self.get_decimals()
        for g in self.groups:
            for name, color in colors.items():
                series = self.history.get((g.db_no, name))
                if not series:
                    continue
                text = g.ref_vars[name].get().strip()
                try:
                    dev = series[-1] - float(text)
                    infos.append("%s %s %+0.*f" % (name, g.db_no, dec, dev))
                except ValueError:
                    pass
        self.trend_info.config(text="最新偏差: " + "  ".join(infos[:8]) if infos else "")

    def _draw_one_trend(self, g, name, color):
        cv = self.trend_canvases[(g.db_no, name)]
        cv.delete("all")
        th = g.get_threshold(name)   # 每个变量使用各自阈值
        dec = self.get_decimals()

        cv.update_idletasks()
        W = max(cv.winfo_width(), 120)
        H = max(cv.winfo_height(), 70)
        pad_l, pad_r, pad_t, pad_b = 40, 8, 10, 4
        pw, ph = W - pad_l - pad_r, H - pad_t - pad_b
        if pw < 20 or ph < 20:
            return

        # 该变量的参考值
        ref = None
        text = g.ref_vars[name].get().strip()
        if text:
            try:
                ref = float(text)
            except ValueError:
                pass

        # Y 轴范围: 覆盖该变量全部采样值与基线±阈值
        ys = list(self.history.get((g.db_no, name), []))
        if ref is not None:
            ys += [ref - th, ref + th]
        if not ys:
            cv.create_text(W // 2, H // 2, text="暂无数据", fill="#888", font=("", 9))
            return
        y_min, y_max = min(ys), max(ys)
        if y_max - y_min < 1e-9:
            y_min -= 0.5
            y_max += 0.5
        margin = (y_max - y_min) * 0.1
        y_min -= margin
        y_max += margin

        def x_of(i):    # 采样点均匀分布, 旧点靠左
            return pad_l + pw * i / max(HISTORY_LEN - 1, 1)

        def y_of(v):
            return pad_t + ph * (1 - (v - y_min) / (y_max - y_min))

        # 边框、Y 轴刻度(按设定小数位)与参考基线(同色虚线)
        cv.create_rectangle(pad_l, pad_t, pad_l + pw, pad_t + ph, outline="#999")
        for i in range(3):
            v = y_min + (y_max - y_min) * i / 2
            y = y_of(v)
            cv.create_line(pad_l, y, pad_l + pw, y, fill="#f0f0f0")
            cv.create_text(pad_l - 3, y, text="%.*f" % (dec, v), anchor="e",
                           fill="#555", font=("", 7))
        if ref is not None:
            cv.create_line(pad_l, y_of(ref), pad_l + pw, y_of(ref),
                           fill=color, dash=(4, 3))
            # 阈值边界线(灰色点线)
            for v in (ref - th, ref + th):
                if y_min < v < y_max:
                    cv.create_line(pad_l, y_of(v), pad_l + pw, y_of(v),
                                   fill="#bbbbbb", dash=(2, 3))

        # 数据折线: 超差段标红
        series = self.history.get((g.db_no, name), [])
        if series:
            start = HISTORY_LEN - len(series)
            prev = None
            for i, v in enumerate(series):
                pt = (x_of(start + i), y_of(v))
                if prev is not None:
                    alarm = ref is not None and abs(v - ref) > th
                    cv.create_line(prev[0], prev[1], pt[0], pt[1],
                                   fill="#cc2222" if alarm else color,
                                   width=1.2)
                prev = pt
            # 最新值标注(右上角, 同色)
            cv.create_text(pad_l + pw - 3, pad_t + 2,
                           text="%.*f" % (dec, series[-1]),
                           anchor="ne", fill=color, font=("", 8, "bold"))

    # ---------------- 逻辑 ----------------
    def _auto_start_monitor(self):
        """自动开始监控: 先ping PLC IP, 不可达则不自动连接和监控;
        可达则try连接, 连接成功自动开始监控"""
        ip = self.ip_var.get().strip()
        if not ip:
            self.status_var.set("自动开始监控: PLC IP为空, 跳过自动连接")
            return
        self.status_var.set("自动开始监控: 正在ping %s ..." % ip)
        self.update_idletasks()
        if not ping_host(ip):
            self.status_var.set("自动开始监控: %s 不可达, 不自动连接和监控" % ip)
            return
        try:
            self.do_connect()
        except Exception as e:
            self.status_var.set("自动开始监控: 连接异常: %s" % e)
            return
        if self.connected:
            self.do_start()
            self.status_var.set("自动开始监控: 已连接 %s 并开始监控" % ip)

    def do_connect(self):
        try:
            rack = int(self.rack_var.get().strip() or "0")
            slot = int(self.slot_var.get().strip() or "0")
        except ValueError:
            messagebox.showwarning("提示", "Rack/Slot 必须为整数")
            return
        try:
            if self.connected:
                self.client.disconnect()
            self.client.connect(self.ip_var.get().strip(), rack, slot)
            self.connected = True
            self.status_var.set("已连接 %s (rack=%d slot=%d)"
                                % (self.ip_var.get().strip(), rack, slot))
            self.btn_connect.config(state="disabled")
            self.btn_disconnect.config(state="normal")
        except Exception as e:
            self.connected = False
            self.status_var.set("连接失败: %s" % e)
            messagebox.showerror("连接失败", str(e))
        self._set_web("connected", self.connected)

    def do_disconnect(self):
        self.stop_polling()
        try:
            if self.connected:
                self.client.disconnect()
        except Exception:
            pass
        self.connected = False
        self.status_var.set("未连接")
        for g in self.groups:
            g.reset_unknown()
        self.btn_connect.config(state="normal")
        self.btn_disconnect.config(state="disabled")
        self._set_web("connected", False)
        self._set_web("monitoring", False)
        with self.web_lock:
            for g in self.groups:
                self.web_state["cameras"][g.db_no]["lamp"] = "idle"

    def do_start(self):
        if not self.connected:
            messagebox.showwarning("提示", "请先连接 PLC")
            return
        self.btn_start.config(state="disabled")
        self.btn_stop.config(state="normal")
        self.trigger_state.clear()   # 重新监控时重新学习触发值, 避免误触发
        self._set_web("monitoring", True)
        self.poll_once()

    def do_stop(self):
        self.stop_polling()
        self.btn_start.config(state="normal")
        self.btn_stop.config(state="disabled")
        self._set_web("monitoring", False)

    def stop_polling(self):
        if self.job is not None:
            self.after_cancel(self.job)
            self.job = None

    def poll_once(self):
        """读取全部相机并预约下一轮"""
        read_failed = False
        for g in self.groups:
            try:
                data = {}
                for name, offset, size in VARIABLES:
                    buf = self.client.db_read(g.db_no, offset, size)
                    data[name] = get_real(buf, 0)
                states, dev_texts = g.update_data(data, self.get_decimals())
                self.record_history(g.db_no, data)
                # 镜像到 Web 状态(线程安全)
                with self.web_lock:
                    cam = self.web_state["cameras"][g.db_no]
                    cam["values"] = dict(data)
                    cam["states"] = states
                    cam["dev_texts"] = dev_texts

                # PLC当前配方号: DB偏移量6(INT), 与所属工位当前配方的配方号比对
                rno_buf = self.client.db_read(g.db_no, RECIPE_NO_OFFSET, 2)
                rno_val = get_int(rno_buf, 0)
                expect = self._expected_recipe_no(getattr(g, "station", None))
                g.update_recipe_no(rno_val, expect)
                with self.web_lock:
                    cam = self.web_state["cameras"][g.db_no]
                    cam["recipe_no"] = rno_val
                    if expect is None:
                        cam["recipe_no_text"] = str(rno_val)
                        cam["recipe_no_state"] = "unknown"
                    elif rno_val == expect:
                        cam["recipe_no_text"] = str(rno_val)
                        cam["recipe_no_state"] = "normal"
                    else:
                        cam["recipe_no_text"] = "%d (应为%d)" % (rno_val, expect)
                        cam["recipe_no_state"] = "badref"

                # 自动切换配方: 左工位看DB97配方号, 右工位看DB98配方号,
                # 配方号变化时匹配对应工位配方, 无匹配回退默认配方149#K
                if self.auto_switch_var.get():
                    st = getattr(g, "station", None)
                    if st in ("left", "right") and RECIPE_NO_DB[st] == g.db_no \
                            and rno_val != self._auto_switch_last.get(st):
                        self._auto_switch_last[st] = rno_val
                        self._switch_recipe_by_no(st, rno_val)

                # 拍照触发: DB偏移量4(INT), 检测上升沿
                trig_buf = self.client.db_read(g.db_no, 4, 2)
                trig = get_int(trig_buf, 0)
                st = self.trigger_state.setdefault(
                    g.db_no, {"last": None, "pending": False,
                              "due": 0.0, "sent": False})
                if st["last"] is not None and trig > st["last"]:
                    # 上升沿: 1秒后允许发送一次OK/NG
                    st["pending"] = True
                    st["sent"] = False
                    st["due"] = time.time() + 1.0
                st["last"] = trig
                # 触发信号灯: 已发送=绿, 等待发送窗口=黄, 否则灰
                lamp = "sent" if st["sent"] else ("pending" if st["pending"]
                                                  else "idle")
                g.set_trigger_lamp(lamp)
                with self.web_lock:
                    self.web_state["cameras"][g.db_no]["lamp"] = lamp

                # 上升沿后间隔1秒, 每个触发仅发送一次:
                # 任一变量超差写 NG(2), 全部在阈值内写 OK(3), 写入本相机DB偏移量2
                if (self.send_signal_var.get() and st["pending"]
                        and not st["sent"] and time.time() >= st["due"]):
                    signal = 2 if g.has_alarm(data) else 3
                    buf = bytearray(2)
                    set_int(buf, 0, signal)
                    self.client.db_write(g.db_no, 2, buf)
                    st["sent"] = True
            except Exception as e:
                read_failed = True
                self.status_var.set("读取 DB%d 失败: %s" % (g.db_no, e))
        if read_failed:
            # 读取异常(通常是连接断开): 停止监控
            self.do_stop()
            self.connected = False
            self.btn_connect.config(state="normal")
            self.btn_disconnect.config(state="disabled")
            self._set_web("connected", False)
            return
        # 趋势图重绘限频: 数据仍每轮采集, 图形按 TREND_REDRAW_MS 刷新
        now = time.time()
        if now - self._last_trend_redraw >= TREND_REDRAW_MS / 1000.0:
            self._last_trend_redraw = now
            self.redraw_trend()
        self.job = self.after(POLL_INTERVAL_MS, self.poll_once)

    def on_close(self):
        self.stop_polling()
        try:
            if self.connected:
                self.client.disconnect()
        except Exception:
            pass
        try:
            if getattr(self, "_web_httpd", None) is not None:
                self._web_httpd.shutdown()
        except Exception:
            pass
        self.destroy()


if __name__ == "__main__":
    app = MainWindow()
    app.mainloop()
