// CogSocket 传输 + HMI 会话封装（TODO #7/#21）
//
// 纯前端 WebSocket 直连相机（方案见 CogSocket&WebApi.md 第 18 节），不经过 Rust。
// 会话流程：ws /ws → @/hello → GET 根 info（v3 cam0/hmi，旧版回退 system）
//          → openSession(A0:Z599) → login → listen resultChanged（每帧立即 ready）
//          → 15s keepAlive；业务调用 setCellValue/getLatestResult 等。
//
// SDK（assets/cogsocket/cogsocket.js）是 AMD/Node 双 shim，顶层立即调用 define()。
// 本页又与 Monaco 的 AMD loader 共存，因此用 Function 沙箱注入伪 module 加载，
// 完全不触碰全局 define/require/module。

const SDK_URL = "assets/cogsocket/cogsocket.js";
const DEFAULT_PORT = 80;
const CONNECT_TIMEOUT_MS = 8000;
const REQUEST_TIMEOUT_MS = 10000;
const KEEPALIVE_MS = 15000;
const SESSION_CELL_RANGE = "A0:Z599";

let sdkClassPromise = null;

/**
 * 加载并返回 CogSocket 类（只加载一次）。
 * 浏览器/Tauri：fetch 源码 + Function 沙箱；测试环境可通过 options.sdkClass 注入。
 */
async function loadCogSocketClass(sdkClassOverride) {
  if (sdkClassOverride) return sdkClassOverride;
  if (!sdkClassPromise) {
    sdkClassPromise = (async () => {
      const resp = await fetch(SDK_URL);
      if (!resp.ok) throw new Error(`加载 cogsocket.js 失败: HTTP ${resp.status}`);
      const code = await resp.text();
      // SDK 顶层：try{ define(f) }catch{ f(require,exports,module) }
      // 传入 undefined 的 define 会抛 ReferenceError，进而走 catch，用我们的伪 module。
      const factory = new Function(
        "module",
        "exports",
        "require",
        "define",
        `${code}\n;return module.exports;`
      );
      const fakeModule = { exports: {} };
      const CogSocketClass = factory(
        fakeModule,
        fakeModule.exports,
        undefined,
        undefined
      );
      if (typeof CogSocketClass !== "function") {
        throw new Error("cogsocket.js 加载异常：未得到 CogSocket 构造函数");
      }
      return CogSocketClass;
    })();
  }
  return sdkClassPromise;
}

/**
 * 从相机格 URL 文本解析 host/port。
 * 接受："192.168.0.1"、"192.168.0.1:8087"、"http://192.168.0.1:8087/xxx"。
 */
export function parseCameraTarget(input) {
  let s = String(input || "").trim();
  if (!s) throw new Error("相机地址为空");
  s = s.replace(/^[a-zA-Z][\w+.-]*:\/\//, ""); // 去协议头
  s = s.split(/[/?#]/)[0]; // 只留 host[:port]@ 不处理（凭据走独立输入框）
  s = s.split("@").pop();
  let host = s;
  let port = DEFAULT_PORT;
  const m = s.match(/^(\[[^\]]+\]):(\d+)$/); // IPv6 字面量 [::1]:80
  if (m) {
    host = m[1].slice(1, -1);
    port = Number(m[2]);
  } else if (s.startsWith("[")) {
    host = s.slice(1, s.indexOf("]"));
  } else {
    const idx = s.lastIndexOf(":");
    if (idx >= 0 && /^\d+$/.test(s.slice(idx + 1))) {
      host = s.slice(0, idx);
      port = Number(s.slice(idx + 1));
    }
  }
  if (!host) throw new Error("无法从 URL 解析相机地址");
  if (!Number.isInteger(port) || port <= 0 || port > 65535) {
    throw new Error("端口非法");
  }
  return { host, port };
}

export class CogConnection {
  /**
   * @param {object} opts
   * @param {string} opts.host
   * @param {number} opts.port
   * @param {string} opts.user
   * @param {string} opts.password
   * @param {(level:string,msg:string)=>void} [opts.onLog]
   * @param {(state:string)=>void} [opts.onState]
   * @param {(result:object)=>void} [opts.onResult] 每帧 HmiResult（已自动 ready）
   * @param {Function} [opts.sdkClass] 测试注入
   * @param {Function} [opts.WebSocketImpl] 测试注入
   */
  constructor(opts) {
    this.host = opts.host;
    this.port = opts.port;
    this.user = opts.user || "admin";
    this.password = opts.password == null ? "" : opts.password;
    this.onLog = opts.onLog || (() => {});
    this.onState = opts.onState || (() => {});
    this.onResult = opts.onResult || (() => {});
    this.sdkClassOverride = opts.sdkClass;
    this.WebSocketImpl = opts.WebSocketImpl || (typeof WebSocket !== "undefined" ? WebSocket : null);

    this.state = "closed"; // closed | connecting | open | error
    this.root = null; // cam0/hmi 或 system
    this.sessionId = null;
    this.cameraInfo = null;
    this.latestResult = null;
    this.sock = null;
    this.cog = null;
    this.keepAliveTimer = null;
    this.wentOffline = false; // 本连接是否曾把相机置为离线（关闭时需恢复）
    this._closed = false;
  }

  setState(s) {
    this.state = s;
    this.onState(s);
  }

  log(level, msg) {
    this.onLog(level, typeof msg === "string" ? msg : safeStringify(msg));
  }

  async connect() {
    if (this.state === "open" || this.state === "connecting") return;
    if (!this.WebSocketImpl) throw new Error("当前环境不支持 WebSocket");
    this._closed = false;
    this.setState("connecting");

    const url = `ws://${this.host}:${this.port}/ws`;
    this.log("info", `连接 ${url} …`);
    const ws = new this.WebSocketImpl(url);
    this.sock = ws;
    await new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        try {
          ws.close();
        } catch {
          /* ignore */
        }
        reject(new Error(`WebSocket 连接超时（${CONNECT_TIMEOUT_MS / 1000}s）`));
      }, CONNECT_TIMEOUT_MS);
      ws.onopen = () => {
        clearTimeout(timer);
        resolve();
      };
      ws.onerror = () => {
        clearTimeout(timer);
        reject(new Error("WebSocket 连接失败（检查相机 IP/端口/网络）"));
      };
    });

    const CogSocketClass = await loadCogSocketClass(this.sdkClassOverride);
    // 构造函数会覆盖 ws.onopen/onerror/onclose/onmessage，因此在 open 之后再 new。
    const cog = new CogSocketClass(ws, null, 0);
    this.cog = cog;
    cog.onclose = () => {
      this.log("warn", "WebSocket 已关闭");
      this._cleanupTimers();
      if (!this._closed) this.setState("error");
      else this.setState("closed");
    };
    cog.onerror = () => this.log("warn", "WebSocket 错误");

    // 1) 连接级握手
    await this.post("@/hello", { name: "CameraViewerTauri", model: "Browser" });
    this.log("info", "握手完成（@/hello）");

    // 2) 根路径探测：API v3 = cam0/hmi；5.x/6.x 旧版 = system
    try {
      this.cameraInfo = await this.get("cam0/hmi/info");
      this.root = "cam0/hmi";
    } catch {
      this.cameraInfo = await this.get("system/info");
      this.root = "system";
    }
    this.log("info", `已连接 ${this.cameraInfo?.name || this.host}（根路径 ${this.root}）`);

    // 3) 打开 HMI 会话
    const sessionInfo = {
      $type: "HmiSessionInfo",
      cellNames: [SESSION_CELL_RANGE],
      enableQueuedResults: true,
      includeCustomView: true,
    };
    this.sessionId = await this.post(`${this.root}/openSession`, sessionInfo);
    if (typeof this.sessionId !== "string" || !this.sessionId) {
      throw new Error("openSession 未返回会话 ID");
    }
    this.log("info", `会话已打开 ${this.sessionId}`);

    // 4) 登录
    const access = await this.post(`${this.sessionId}/login`, [
      this.user,
      this.password,
      false,
    ]);
    if (access === "locked") {
      throw new Error("登录后访问级别为 locked，无法进行任何操作");
    }
    this.log("info", `登录成功（访问级别：${access || "未知"}）`);

    // 5) 订阅结果帧：每帧必须回 ready，否则相机停止推送
    await this.addListener(`${this.sessionId}/resultChanged`, (result) => {
      this.latestResult = result;
      this.onResult(result);
      this.post(`${this.sessionId}/ready`, "").catch(() => {});
    });
    // 订阅后主动要一帧，驱动首张结果
    this.post(`${this.sessionId}/ready`, "").catch(() => {});

    // 6) 保活（默认超时 30s，间隔取 15s）
    this.keepAliveTimer = setInterval(() => {
      this.post(`${this.sessionId}/keepAlive`, "").catch(() => {});
    }, KEEPALIVE_MS);

    this.setState("open");
  }

  // ---------------- 基础请求（Promise 化 + 超时） ----------------

  _request(method, path, body, timeoutMs = REQUEST_TIMEOUT_MS) {
    return new Promise((resolve, reject) => {
      if (!this.cog) {
        reject(new Error("CogSocket 未连接"));
        return;
      }
      let settled = false;
      const timer = setTimeout(() => {
        if (settled) return;
        settled = true;
        reject(new Error(`请求超时：${method} ${path}`));
      }, timeoutMs);
      const done = (arg) => {
        if (settled) return;
        settled = true;
        clearTimeout(timer);
        if (arg instanceof Error) {
          const code = arg.number != null ? ` [${arg.number}]` : "";
          reject(new Error(`${arg.message || "请求失败"}${code}`));
        } else {
          resolve(arg);
        }
      };
      try {
        if (method === "get") this.cog.get(path, done);
        else if (method === "put") this.cog.put(path, body, done);
        else this.cog.post(path, body, done);
      } catch (e) {
        clearTimeout(timer);
        reject(e);
      }
    });
  }

  get(path) {
    return this._request("get", path, undefined);
  }

  post(path, body) {
    return this._request("post", path, body);
  }

  put(path, body) {
    return this._request("put", path, body);
  }

  addListener(path, listener) {
    return new Promise((resolve, reject) => {
      if (!this.cog) return reject(new Error("CogSocket 未连接"));
      try {
        this.cog.addListener(path, listener, (arg) => {
          if (arg instanceof Error) reject(arg);
          else resolve(arg);
        });
      } catch (e) {
        reject(e);
      }
    });
  }

  // ---------------- HMI 业务方法 ----------------

  /** 当前作业名 */
  async getJobName() {
    return this.get(`${this.root}/job/name`);
  }

  /** State 对象（online/softOnline/...） */
  async getState() {
    return this.get(`${this.root}/state`);
  }

  /** 在线/离线（软在线）。offline=true 时记住，断开时自动恢复。 */
  async setSoftOnline(online) {
    await this.put(`${this.sessionId}/softOnline`, !!online);
    if (!online) this.wentOffline = true;
    else this.wentOffline = false;
  }

  /** 手动触发一次拍照（需 IS.OPS） */
  manualTrigger() {
    return this.post(`${this.sessionId}/manualTrigger`, null);
  }

  /** 所有单元格名映射：{ "A3": "Acquisition.Trigger", ... } */
  getAllCellNames() {
    return this.post(`${this.sessionId}/getAllCellNames`, null);
  }

  /** 主动拉取最新一帧 HmiResult */
  getLatestResult() {
    return this.post(`${this.sessionId}/getLatestResult`, null);
  }

  /**
   * 设置单个单元格值（setCellValue）。
   * @param {string} name 单元格名或地址（如 "MyEditInt"、"A0"）
   * @param {number|string|boolean|object} value
   */
  setCell(name, value) {
    return this.post(`${this.sessionId}/setCellValue`, [name, value]);
  }

  /**
   * 批量设置（setCellValues）。注意协议 body 是"包一层对象的数组"。
   * @param {Record<string, unknown>} map
   */
  setCells(map) {
    return this.post(`${this.sessionId}/setCellValues`, [map]);
  }

  // ---------------- 断开清理 ----------------

  _cleanupTimers() {
    if (this.keepAliveTimer) {
      clearInterval(this.keepAliveTimer);
      this.keepAliveTimer = null;
    }
  }

  async disconnect() {
    this._closed = true;
    this._cleanupTimers();
    // 若本会话曾把相机置离线，尽力恢复在线（仿参考软件 FrmGrid 关窗行为）
    if (this.wentOffline && this.sessionId && this.cog) {
      try {
        await this.put(`${this.sessionId}/softOnline`, true);
      } catch {
        /* ignore */
      }
      this.wentOffline = false;
    }
    if (this.sessionId && this.cog) {
      try {
        await this.post(`${this.sessionId}/dispose`, null);
      } catch {
        /* ignore */
      }
    }
    try {
      this.cog?.close();
    } catch {
      /* ignore */
    }
    this.cog = null;
    this.sock = null;
    this.sessionId = null;
    this.setState("closed");
  }
}

function safeStringify(v) {
  try {
    return JSON.stringify(v);
  } catch {
    return String(v);
  }
}
