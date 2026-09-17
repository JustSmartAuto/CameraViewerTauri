/*
 * HMI 网页 i18n 文本替换脚本（Cognex In-Sight HMI，/pages/hmi/）
 * ----------------------------------------------------------------
 * 本脚本通过 WebView2 AddScriptToExecuteOnDocumentCreatedAsync 在“文档创建期”
 * 注入到主窗口的每一个 frame（包括跨域相机 iframe——浏览器同源策略不允许
 * 父页面改写跨域 iframe DOM，但宿主层注入脚本在该 frame 内拥有完整 DOM 权限）。
 *
 * 工作方式：
 *  - 仅在相机 iframe 内且 URL 路径匹配 /pages/hmi/ 时激活；
 *  - 语言完全跟随软件：默认英文（原文），宿主通过 chrome.webview 消息通知语言；
 *  - 只做“整串精确匹配替换”，不做子串替换，避免破坏作业数据/文件名/数值；
 *  - MutationObserver 监听框架后续动态渲染的节点与 title 属性，并可还原回英文；
 *  - 切回英文时按记录的原文逐个还原，框架重建节点后自然显示原始英文。
 *
 * 宿主（C# 侧）协议：
 *  - 子 -> 宿主：{ "__hmiI18n": "ready" }
 *  - 宿主 -> 子：{ "__hmiI18n": "lang", "lang": "zh" | "en" }
 */
(function () {
  "use strict";

  // 防止脚本被重复注入时重复初始化
  if (window.__hmiI18n) return;
  // 只在 iframe（相机网页）内工作，顶层 frame 是本软件自己的界面
  if (window.self === window.top) return;
  if (!/\/pages\/hmi(?:\/|$)/.test(location.pathname)) return;

  window.__hmiI18n = true;

  // ==================== 翻译字典（英文原文 -> 中文） ====================
  // 仅收录实际在 IS8905MX HMI 上观察到、或 Framework.js 中有据可查的“框架文案”。
  // 设备名、作业文件名、用户名、自定义视图名、作业内中文标签、数值、日期时间一律不译。
  var DICT = {
    // —— 状态与主按钮（div.cjsInSightButton 文本） ——
    "Online": "在线",
    "Offline": "离线",
    "Trigger": "触发",
    "Load": "加载",
    "Save": "保存",
    "Settings": "设置",
    "Cancel": "取消",
    "Reset": "复位",
    // 胶片(Filmstrip)/向导通用按钮
    "Close": "关闭",
    "Next": "下一个",
    "Back": "返回",
    "Accept": "接受",
    "Freeze": "冻结",
    "Live Mode": "实时模式",
    // EasyView/Custom View 是 Cognex 视图模式名称
    "EasyView": "简易视图",
    "Custom View": "自定义视图",
    "Custom Views": "自定义视图",
    "Image": "图像",
    "Graphics": "图形",

    // —— 分组标题 ——
    "Validation Options:": "验证选项：",
    "Validation Options": "验证选项",
    "View Options:": "视图选项：",
    "View Options": "视图选项",
    "Run Job Validation": "运行作业验证",
    "System Validation": "系统验证",

    // —— Cookie 提示条 ——
    "Settings are saved as cookies. These cookies are used to improve the user experience.":
      "设置以 Cookie 形式保存，用于提升用户体验。",

    // —— 图像工具栏 title 提示 ——
    "Zoom In": "放大",
    "Zoom Out": "缩小",
    "Fit": "适合窗口",
    "Fill": "填充",
    "Rotate Left": "向左旋转",
    "Rotate Right": "向右旋转",
    "Pan On/Off": "平移 开/关",
    "Show Selected Pixel Data": "显示所选像素数据",
    "Toggle graphics on the Image display": "在图像显示上切换图形",
    "Up": "上",
    "Down": "下",
    "Left": "左",
    "Right": "右",

    // —— 状态/通用 ——
    "Not Supported": "不支持",
    "Not Available": "不可用",
    "Disabled": "已禁用",
    "Please wait...": "请稍候...",

    // —— Settings 对话框（服务器下发的对话框模板文本，Framework.js 中无） ——
    "Result Queue": "结果队列",
    "Layout Position": "布局位置",
    "Override Size": "覆盖尺寸",
    "Width": "宽度",
    "Height": "高度",
    "Top": "顶部",
    "Bottom": "底部",
    "Point Cloud": "点云",
    "Views": "视图",
    "Name": "名称",
    "Value": "值",
    "Header": "表头",
    "Data": "数据",

    // —— 作业验证进度/结果 ——
    "Running Job Validation": "正在运行作业验证",
    "Job Validation Complete": "作业验证完成",
    "Job Validation Done": "作业验证完成",
    "Job Validation Result:": "作业验证结果：",

    // —— 保存作业/胶片（Filmstrip）对话框 ——
    "Save Job": "保存作业",
    "No Image Selected": "未选择图像",
    "No Custom Views": "无自定义视图",
    "No Profile Views": "无剖面视图",
    "Confirm Clear Filmstrip": "确认清除胶片",
    "Are you sure you want to clear the filmstrip?": "确定要清除胶片吗？"
  };

  // 按钮专用词典：这些词在页面其他位置有不同含义（如 OK/NG 检测结果），
  // 仅当文本节点位于 cjs 按钮元素内时才替换
  var BTN_DICT = {
    "OK": "确定"
  };
  var BTN_CLASS_RE = /cjsButton/;

  // 需要翻译的元素属性（title 是工具栏图标的悬浮提示等）
  var ATTRS = ["title", "placeholder", "aria-label"];
  var SKIP_TAGS = { SCRIPT: 1, STYLE: 1, NOSCRIPT: 1 };

  var currentLang = "en";
  var scanQueued = false;
  var restoring = false;
  // MutationObserver 增量批处理：HMI 可能一次性重建大量节点，合并到一个 tick 处理
  var pendingRoots = null;
  var flushQueued = false;

  function flushPending() {
    flushQueued = false;
    var roots = pendingRoots;
    pendingRoots = null;
    if (!roots) return;
    for (var i = 0; i < roots.length; i++) {
      try { scanSubtree(roots[i]); } catch (e) {}
    }
  }

  function addPending(node) {
    if (!pendingRoots) pendingRoots = [];
    pendingRoots.push(node);
    if (!flushQueued) {
      flushQueued = true;
      setTimeout(flushPending, 16);
    }
  }

  // 保留原文首尾空白的替换/还原
  function replacePreservingWs(raw, translated) {
    var lead = /^\s*/.exec(raw)[0];
    var trail = /\s*$/.exec(raw)[0];
    return lead + translated + trail;
  }

  function translateTextNode(node) {
    var raw = node.nodeValue;
    if (!raw) return;
    var key = raw.trim();
    if (!key) return;

    if (currentLang === "zh") {
      // 已翻译过的节点不重复处理
      if (node.__hmiOrig !== undefined) return;
      var zh = DICT[key];
      // 通用词典未命中时，若节点位于 cjs 按钮内，尝试按钮专用词典
      if (zh === undefined) {
        var pe = node.parentElement;
        for (var depth = 0; pe && depth < 3; depth++) {
          if (BTN_CLASS_RE.test(pe.className || "")) {
            zh = BTN_DICT[key];
            break;
          }
          pe = pe.parentElement;
        }
      }
      if (zh) {
        node.__hmiOrig = raw;
        node.nodeValue = replacePreservingWs(raw, zh);
      }
    } else {
      // 英文模式：还原本脚本翻译过的节点
      if (!restoring || node.__hmiOrig === undefined) return;
      node.nodeValue = node.__hmiOrig;
      try { delete node.__hmiOrig; } catch (e) { node.__hmiOrig = undefined; }
    }
  }

  function scanAttrs(el) {
    for (var i = 0; i < ATTRS.length; i++) {
      var a = ATTRS[i];
      var v = el.getAttribute(a);
      if (v == null) continue;
      var key = v.trim();
      if (!key) continue;

      if (currentLang === "zh") {
        if (el.getAttribute("data-hmi-orig-" + a) != null) continue;
        var zh = DICT[key];
        if (zh) {
          el.setAttribute("data-hmi-orig-" + a, v);
          el.setAttribute(a, zh);
        }
      } else if (!restoring) {
        // 英文模式不主动处理属性
      } else {
        var orig = el.getAttribute("data-hmi-orig-" + a);
        if (orig != null) {
          el.setAttribute(a, orig);
          el.removeAttribute("data-hmi-orig-" + a);
        }
      }
    }
  }

  function scanSubtree(root) {
    if (root.nodeType === 3) {
      translateTextNode(root);
      return;
    }
    if (root.nodeType !== 1 && root.nodeType !== 9 && root.nodeType !== 11) return;
    if (root.nodeType === 1 && SKIP_TAGS[root.tagName]) return;

    if (root.nodeType === 1) scanAttrs(root);

    var walker = root.ownerDocument
      ? root.ownerDocument.createTreeWalker(root, 0x4 /* TEXT_NODE */, null)
      : null;
    // document 自身作为 root 时 ownerDocument 为 null
    if (!walker) {
      walker = document.createTreeWalker(root, 0x4, null);
    }
    var nodes = [];
    var n;
    while ((n = walker.nextNode())) {
      var p = n.parentNode;
      if (!p || (p.nodeType === 1 && SKIP_TAGS[p.tagName])) continue;
      nodes.push(n);
    }
    for (var i = 0; i < nodes.length; i++) translateTextNode(nodes[i]);

    // 属性：只遍历元素节点
    var ew = document.createTreeWalker(root, 1 /* ELEMENT_NODE */, null);
    var el;
    while ((el = ew.nextNode())) {
      if (!SKIP_TAGS[el.tagName]) scanAttrs(el);
    }
  }

  function queueScan() {
    if (scanQueued) return;
    scanQueued = true;
    setTimeout(function () {
      scanQueued = false;
      try { scanSubtree(document.documentElement || document); } catch (e) {}
    }, 16);
  }

  function applyLang(lang) {
    if (lang !== "zh" && lang !== "en") return;
    var changed = lang !== currentLang;
    currentLang = lang;
    if (lang === "en") {
      restoring = true;
      try { scanSubtree(document.documentElement || document); } catch (e) {}
      restoring = false;
    } else {
      queueScan();
    }
    return changed;
  }

  function notifyReady() {
    try { window.chrome.webview.postMessage({ __hmiI18n: "ready" }); } catch (e) {}
  }

  // ==================== 与宿主的语言同步 ====================
  window.chrome.webview.addEventListener("message", function (e) {
    var d = e.data;
    if (d && typeof d === "object" && d.__hmiI18n === "lang") {
      applyLang(d.lang);
    }
  });

  // 框架是 RequireJS 单页应用：观察后续所有动态渲染/重绘
  try {
    var observer = new MutationObserver(function (mutations) {
      // 英文模式下无翻译任务，不处理变更
      if (currentLang !== "zh") return;
      for (var i = 0; i < mutations.length; i++) {
        var m = mutations[i];
        if (m.type === "childList") {
          for (var j = 0; j < m.addedNodes.length; j++) {
            var nd = m.addedNodes[j];
            if (nd.nodeType === 1 || nd.nodeType === 3) addPending(nd);
          }
        } else if (m.type === "characterData") {
          translateTextNode(m.target);
        } else if (m.type === "attributes" && m.attributeName) {
          if (m.target.nodeType === 1) scanAttrs(m.target);
        }
      }
    });
    observer.observe(document, {
      subtree: true,
      childList: true,
      characterData: true,
      attributes: true,
      attributeFilter: ATTRS
    });
  } catch (e) {}

  // 文档创建期 documentElement 可能刚出现；DOMContentLoaded/load 再全量扫一遍
  function boot() {
    notifyReady();
    if (currentLang === "zh") queueScan();
  }
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", boot);
  } else {
    boot();
  }
  window.addEventListener("load", function () {
    notifyReady();
    if (currentLang === "zh") queueScan();
  });
  // 初始即上报一次（宿主可能尚未绑定监听，宿主在 ready 时也会主动下发）
  notifyReady();
})();
