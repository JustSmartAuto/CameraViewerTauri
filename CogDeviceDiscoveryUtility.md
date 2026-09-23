# Cognex Device Discovery Utility 23.4.0 — 技术架构与相机搜索原理分析

> 本文基于对该软件安装包的反编译分析（Electron `app.asar` 解包 + .NET 程序集 ILSpy 反编译）编写，仅供学习研究。

## 一、软件概况

**Cognex Device Discovery Utility**（康耐视设备发现工具）是 Cognex 官方出品的局域网设备扫描/配置工具，用于发现、识别和管理 Cognex 视觉设备（InSight 系列智能相机、DataMan 读码器、3D-A1000/L4000、VisionView 显示屏、Smart Sensor 等），并支持修改设备网络参数（IP/掩码/网关/DNS/DHCP）、闪烁定位（Flash）、恢复出厂设置、打开设备 Web 界面等操作。

它本质上是一个 **Electron 桌面应用 + 内嵌 .NET Framework 控制台子进程** 的混合架构程序，核心设备发现逻辑（CogNamer 私有协议）封装在闭源 .NET 库 `Cognex.CogNamer.Client.dll` 中。

## 二、安装包与文件结构

当前目录是 NSIS 安装程序的解包临时目录（`$PLUGINSDIR`）：

```
$PLUGINSDIR/
├── app-64/                              # 64 位应用主体（即安装后的程序目录）
│   ├── Cognex Device Discovery Utility.exe   # Electron 主程序（~154MB，内含 V8/Chromium）
│   ├── *.dll / *.pak / icudtl.dat / locales/ # Electron/Chromium 运行时
│   └── resources/
│       ├── app.asar                     # Electron 应用源码包（JS，可解包）
│       ├── bin/Release/                 # .NET 发现引擎（被 Electron 作为子进程拉起）
│       │   ├── Cognex.CogNamerProvider.Client.exe     # .NET 4.8 控制台程序（命令处理入口）
│       │   ├── Cognex.CogNamer.Client.dll             # ★ CogNamer 协议核心（发现/通信/加密）
│       │   ├── Cognex.CogNamerProvider.Common.dll     # 进程间消息 DTO / Base64 编解码
│       │   └── Newtonsoft.Json.dll
│       ├── cognamer-web-app/            # Angular 编译产物（前端 UI，express 静态托管）
│       └── elevate.exe                  # Windows 提权小工具
└── *.dll / nsis7z.dll / splash.bmp      # NSIS 安装器组件
```

## 三、整体技术架构

程序是一个 **三层进程协作** 架构：

```
┌─────────────────────────────────────────────────────────┐
│ 渲染进程（Chromium）                                     │
│  Angular 前端 UI (cognamer-web-app)                      │
│  通过 preload.js 暴露的 window.discoveryAPI 收发 IPC      │
└──────────────┬──────────────────────────────────────────┘
               │ Electron IPC (ipcRenderer / ipcMain)
┌──────────────▼──────────────────────────────────────────┐
│ 主进程（Node.js / Electron main）                        │
│  main.js: 创建窗口、起 Express 静态服务器托管前端、         │
│  CogNamerProcess: spawn 子进程 + stdin/stdout 消息转发    │
└──────────────┬──────────────────────────────────────────┘
               │ 子进程 stdin/stdout（Base64+JSON 帧协议）
┌──────────────▼──────────────────────────────────────────┐
│ .NET Framework 4.8 控制台进程                            │
│  Program.Main: 命令循环 + CogNamerListener               │
│  Cognex.CogNamer.Client.dll: UDP 1069 端口 CogNamer 协议 │
└─────────────────────────────────────────────────────────┘
```

### 1. Electron 主进程（`src/main.js`）

- 申请单实例锁（`requestSingleInstanceLock`），重复启动只聚焦已有窗口。
- 启动时做三件事（`initialize`）：创建 IPC 桥 → 拉起 .NET 子进程 → 用 **Express** 在随机空闲端口托管 `cognamer-web-app` 前端，BrowserWindow 加载 `http://localhost:<port>/index.html`（注意 `webSecurity: false`，所以本地页面可以访问设备 Web 接口而不受跨域限制）。
- 用 `electron-localshortcut` 注册 `Ctrl+Shift+F12` 切换隐藏菜单（Query Devices / Dev Tools / Reload）。
- `dispatchApiEvent` 是前端的统一 API 入口：前端 `ipcRenderer.send('api-event', {event, param})`，主进程按事件名分发到 .NET 子进程（refresh、save-options、set-network-settings、send-flash、read/write-cache、EULA 检查等）。

### 2. 子进程封装（`src/cogNamerProcess.js`）

- `spawn()` 启动 `resources/bin/Release/Cognex.CogNamerProvider.Client`；
- 把前端操作转成命令对象 `{Name, Parameters}` 写入子进程 stdin；
- 逐字符读取子进程 stdout，交给 `communication.js` 组帧，解析出消息后按 `MessageType`（`devices` / `options` / `error` …）转换数据模型并通过 `webContents.send` 推给前端；
- 子进程 stderr 弹错误对话框，子进程退出即 `app.exit()`。

### 3. 前端（`cognamer-web-app`）

Angular（Angular Material，Roboto 字体）编译产物，设备卡片列表、过滤器 chips、主题（默认 custom-dark）、语言本地化等。设备图标/能力优先走设备 Web 接口提供的缓存（`WebFeatures.CacheKey`），否则用内置默认图标。

## 四、进程间通信协议（stdio 帧协议）

Electron 与 .NET 子进程之间通过 **Base64 包裹的 JSON 消息帧** 通信，帧以 `(` 开始、`)` 结束：

```
( eyJNZXNzYWdlVHlwZSI6ImRldmljZXMiLCJQYXlsb2FkIjpb...} )
└┬─┘ └────────── Base64(JSON) ──────────────────────────┘ └┘
开始符                     消息体 {"MessageType":..., "Payload":...}  结束符
```

- 发送方向：`JSON.stringify → ascii/utf8 → Base64 → 加括号`；
- 接收方向：逐字符扫描，遇 `(` 开始缓冲，遇 `)` 截帧、Base64 解码、JSON 反序列化；
- 此设计让二进制安全的文本协议跑在控制台 stdout 上，避免日志污染导致解析错乱（帧外字符被忽略）。

## 五、相机搜索原理（核心）

真正的发现在 `Cognex.CogNamer.Client.dll` 的 `CogNamerListener` / `CogNamerProtocolHandler` 中实现，即 Cognex 私有的 **CogNamer 协议**（类似 NI 的 Indigo/AVnamaer 发现协议），走 **UDP 端口 1069**。

### 1. 协议帧格式（`CogNamerPacket`）

每个 UDP 包的载荷是一个二进制帧：

```
| Magic (4B, 0x4D584E4B = "NKXM" 1296975683) | 版本 (1B, =4) |
| Flags (1B) | Command (varint) | ErrorCode (varint) | 记录数 (varint) | 记录... |
```

- 整数采用 **VarInt**（7bit 小端 LEB128）编码；
- Flags：`0x20` SupportsCommandProbe、`0x40` Broadcast、`0x80` Response；
- 每条记录 = `AttributeType(1B) + 长度(varint) + 数据`，数据可以是 IP（4B 大端）、UTF-8 字符串或二进制属性。

### 2. 命令集（`NamerCommand`）

| 命令 | 含义 |
|---|---|
| NOOP | 探活 |
| Hello | 设备周期广播的自报消息（心跳） |
| Identify | 扫描/点名（发现设备的核心命令） |
| IPRequest / IPAssign | 请求/分配 IP（用于设备重配网络） |
| FactoryReset / RestartSystem | 恢复出厂 / 重启 |
| SetAttribute / GetAttribute | 读写设备属性 |
| Flash | 让设备指示灯闪烁以便肉眼定位 |
| QueryCache | 查询设备 Web 资源缓存 |
| ResetAdminPassword | 重置管理员密码 |

### 3. 搜索流程

1. **监听**：为每个可用网卡创建 UDP socket 绑定 1069 端口，持续接收网络上的 CogNamer 数据包；同时监听 `NetworkChange.NetworkAddressChanged`，网卡/IP 变化时使适配器缓存失效。
2. **主动扫描（Refresh）**：发送 `Identify` 命令。正常模式发非广播帧；若开启“发现配置错误设备”（`misconfiguredDiscovery`），则向 `255.255.255.255:1069` 发送 **UDP 广播** Identify，并携带 `KnownSystems` 记录（已知设备的 IP+主机名列表，单包约 8KB 上限分片），让不在同一子网的设备也能响应。
3. **被动收集**：Cognex 设备上电/运行时周期性发出 `Hello` 广播包（内含主机名、IP、MAC、型号、固件版本、子网掩码、网关、DNS、序列号、能力标志及一组 TLV 设备专有属性），监听器解析后并入 `Hosts` 设备表；`IPRequest` 包（设备刚启动请求配置）也会被捕获。
4. **跨网段发现**：支持配置“远程子网”（RemoteSubnets），对这些子网做定向子网广播（`SendSubnetBroadcast`）。
5. **变化通知**：`Hosts` 表任何增改都会触发 `HostsChanged` 事件，整个设备列表立即经 stdio 帧协议推给 Electron，再推给 Angular 前端刷新列表。

### 4. 识别与分类

- 设备类型由 `CogNamerNetworkDeviceType` 标志位族（InSight/DataMan/VisionView/SmartSensor…）判定，配合设备专有属性 #46（应用 ID，如 MultiCam=65、Profiler=64、模拟器=3/4）细分型号系列（5000/7000/7500/8000/9000、2800/3800、AT180、A1000、L4000 等）。
- 匹配设备图标：InSight 家族走内置图标映射，DataMan 走 35 项型号→图标字典（`DataManIconUtils`）。
- 配置异常检测：对设备与 PC 两侧做**双向子网掩码比对**（`OnSubnet`，含"主机位全 0 / 全 1"等边界检查），不一致即标记 `misconfigured`，原因细分为设备链路本地地址（169.254.x.x）、PC 链路本地地址、逻辑子网不匹配三类。

### 5. 设备控制能力（按家族区分）

前端按钮由 `DeviceCapabilities` 控制：Identify（闪烁）/FactoryReset/Connect。例如：DataMan 与 InSight 3D 支持识别+连接；InSight 2800/3800 无 Identify；老固件（23.1 之前）的 Smart Sensor 无 Flash；支持 Web 特性的新设备改走 HTTP API（`api/flash/flash`）。

### 6. 其他网络操作

- **修改网络参数**：`SendSetNetwork` 以 MAC + 用户名/密码 + 新 IP/掩码/网关/DNS/主机名 组成记录列表，通过 IPAssign 命令发给设备（凭据经内置 `CogNamerEncrypt` 加密传输）；
- **解析设备**：支持按 MAC、IP、主机名（DNS 或单播 Identify 探测）定位设备（`CogNamerResolver`）；
- **一键套用 PC 网络配置**：读取与设备相连的网卡（以太网/无线、IPv4）的 DHCP/IP/掩码/网关/DNS，用 `NetworkUtilities.SetIPField` 生成同网段地址建议。

## 六、本地持久化

.NET 子进程把状态写入 `%LocalAppData%\Cognex\Device Discovery Utility\`：

| 文件 | 内容 |
|---|---|
| `options` | 发现选项（语言、错误配置发现开关、远程子网列表） |
| `filter` / `chips-filter` | 前端搜索过滤条件 |
| `theme-id` | UI 主题（默认 `custom-dark`） |
| `pinned-devices` | 前端固定的设备列表 |
| `eula-accepted` | 最终用户协议是否已接受 |
| `<contentType>\<MD5(id)>` | 设备 Web 资源（图标/能力）缓存，文件名是自实现的 MD5 哈希（`MD5.Calculate`） |

另有 `last-root` 文件记录上次安装路径，升级安装时清理旧的临时安装目录（`CleanupLastRoot`）。

## 七、小结

| 维度 | 技术选型 |
|---|---|
| 桌面壳 | Electron（Chromium + Node.js） |
| 前端 | Angular + Angular Material |
| 本地服务 | Express（静态托管前端，随机端口） |
| 发现引擎 | .NET Framework 4.8 控制台进程 |
| 发现协议 | CogNamer 私有 UDP 协议，端口 1069，广播/子网广播 + Hello 心跳被动监听 |
| 进程间通信 | stdin/stdout + `(Base64(JSON))` 帧 |
| 数据/配置 | JSON 文件（Newtonsoft.Json）+ MD5 键缓存 |

设计亮点：UI 层（JS/Angular）与网络发现层（C#.NET）彻底解耦，通过极简的 stdio 文本协议桥接；发现协议同时支持"广播主动扫描"与"Hello 心跳被动收集"，并辅以远程子网定向广播和跨子网 KnownSystems 列表，保证设备即使 IP 配置错误也能被找到——这正是工业现场"相机连不上、找不到 IP"场景下的核心需求。
