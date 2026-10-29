# Audit Log Server for Cognex In-Sight

Cognex In-Sight Vision Systems 审计日志集中存储与可视化服务器。一个 Java 25 单 jar 可执行程序，监听 TCP 5140 端口（Fluentd 默认转发端口），同时提供现代化 Swing 桌面界面浏览和筛选多台相机的审计日志。

同时提供 **.NET Framework 4.8 便携 Launcher**（Costura.Fody 单文件 EXE，内嵌 JRE 25 + fat jar），双击即可运行，Win10 工控机零外部依赖。

---

## 特性

- 📡 **多相机客户端监听** — RFC 5424 Syslog TCP 服务（默认端口 5140），支持 Fluentd `octet-count framing` 和 `NL framing` 两种帧格式
- 🔐 **可选 TLS/SSL** — 在 `app.properties` 中启用，加载 PKCS12 证书链后支持相机客户端双向认证
- 💾 **H2 内嵌存储** — 文件模式持久化，带时间戳/主机/严重级索引，自增主键
- 🖥️ **FlatLaf 现代化 UI** — 过滤（主机 + 严重级）、分页、自动滚动、严重级色彩渲染、一键导出 JSON / 裁剪旧行
- 🧵 **虚拟线程高并发** — Java 25 `Executors.newVirtualThreadPerTaskExecutor()`，一台 PC 同时处理数十台相机连接无压力
- 📦 **Fat Jar 单文件分发** — 所有依赖打入一个 jar，Windows / Linux 直接 `java -jar` 运行
- 🚀 **便携 Launcher 单文件 EXE** — net48 + Costura.Fody merge DLL + 内嵌 jlink 精简 JRE 25，Win10 LTSC 原生支持，双击即跑

## 技术栈

| 组件 | 版本 | 备注 |
| --- | --- | --- |
| OpenJDK | 25.0.2 LTS | `sourceCompatibility` / `targetCompatibility` = Java 25 |
| Gradle | 9.7.1 | 项目自带 `gradle-dist/` 免安装 |
| FlatLaf | 3.7.1 | `FlatIntelliJLaf`（默认）/ `FlatDarkLaf` 可选 |
| H2 Database | 2.5.252 | 文件模式，`MODE=PostgreSQL` |
| Gson | 2.11.0 | 结构化数据序列化 |
| SLF4J Simple | 2.0.16 | 日志 |
| .NET Framework | 4.8 | Launcher 目标框架，Win10 自带 |
| Costura.Fody | 6.0.0 | Launcher DLL 合并为单文件 EXE |
| jlink | Zulu JDK 25 | 精简 JRE（仅保留 java.desktop/logging/sql/prefs/instrument/naming/security.jgss） |

## 快速开始

### 方式一：便携 EXE（推荐现场部署）

一键构建单文件 EXE（JRE + Launcher + 应用全内嵌，约 30 MB）：

```bash
# bash / WSL — 完整流程
./bundle-with-timestamp.sh

# 复用已有 jre.zip 跳过 jlink（秒级完成）
./bundle-with-timestamp.sh --skip-jlink

# PowerShell
.\bundle-with-timestamp.ps1
```

产物位于 `dist/Cognex相机日志服务器_{timestamp}.exe`，直接双击运行即可。

> 前置：系统安装 Zulu JDK 25（jlink）+ dotnet SDK 8+/9+（能 target net48）。

### 方式二：Fat Jar + 系统 Java（开发调试）

```bash
# bash / WSL
./build-with-timestamp.sh

# PowerShell（Windows 原生）
.\build-with-timestamp.ps1
```

脚本先跑 `gradle fatJar`，再把产物重命名为带毫秒级时间戳后缀，例如：

```
audit-log-server-1.0.0-fat_20261027173012457.jar
```

运行：

```bash
java -jar audit-log-server-*.jar
java -jar audit-log-server-*.jar --autostart   # 启动即自动打开服务器
```

> 若本地没有系统 Gradle，脚本会优先使用项目内 `gradle-dist/gradle-9.7.1/bin/gradle`。

### 3. 配置

首次运行会在配置目录读取 `app.properties`（不存在则使用内置默认值）。目录自动创建：
- D 盘存在 → `D:\JustSmart\CognexLoggingServer\config\`
- D 盘不存在 → `%APPDATA%\CognexLoggingServer\config\`

模板位于 `src/main/resources/app.properties`：

```properties
# Syslog / TLS
syslog.port=5140
syslog.tls.enabled=false
syslog.tls.keystore=./certs/server.p12
syslog.tls.keystore.password=changeit
syslog.tls.truststore=./certs/client-trust.p12
syslog.tls.truststore.password=changeit

# H2 Database (file mode)
h2.db.path=./auditlog/auditlog

# UI
ui.max.rows=100000
ui.theme=FlatIntelliJLaf   # FlatIntelliJLaf / FlatDarkLaf / FlatLightLaf
```

### 4. Cognex In-Sight 端配置

参考 Cognex 官方文档 [How to Create an Audit Logging Server for In-Sight Vision Systems Cameras](https://support.cognex.com/en/help-articles/how-to-create-an-audit-logging-server-for-in-sight-vision-systems-cameras)：

1. In-Sight Vision Suite → **Utilities → Audit Logging**
2. 勾选 **Enable Forwarding**
3. IP 填写运行本服务的 PC 地址，**端口 5140**（或 `app.properties` 里修改的端口）
4. OK → 相机审计事件立即开始流入

建议同步在 In-Sight 中配置 **NTP Server**，否则相机时间戳会以本地时间上报。

---

## 项目结构

```
FluentdLoggingServer/
├── build.gradle                       # Gradle 构建（含 fatJar task）
├── settings.gradle
├── build-with-timestamp.sh            # bash / WSL：Gradle fat jar + 时间戳
├── build-with-timestamp.ps1           # PowerShell：Gradle fat jar + 时间戳
├── bundle-with-timestamp.sh           # bash / WSL：一键完整打包（fatJar → jlink → dotnet → dist/*.exe）
├── bundle-with-timestamp.ps1          # PowerShell：一键完整打包
├── gradle-dist/gradle-9.7.1/          # 可选：本地 Gradle 发行包
│
├── src/main/java/com/auditlog/
│   ├── AuditLogApp.java               # 入口（单实例锁 → theme → H2 → MainFrame → autostart）
│   ├── config/AppConfig.java          # 属性加载
│   ├── config/PathsUtil.java          # 配置/数据库/锁文件目录解析（D 盘优先，%APPDATA% 回退）
│   ├── i18n/I18n.java                 # 中英文国际化（UTF8 加载 + 托盘菜单强制英文）
│   ├── server/
│   │   ├── SyslogParser.java          # RFC 5424 解析（含 octet-count + NL framing）
│   │   ├── SyslogMessage.java         # 解析结果 record
│   │   └── SyslogServer.java          # TCP 监听 + TLS 支持 + 虚拟线程
│   ├── db/
│   │   ├── H2Store.java               # H2 内嵌存储（schema / insert / query / prune）
│   │   └── AuditRow.java              # UI 行 record
│   ├── util/SingleInstanceLock.java   # 跨进程 FileLock（audit-log-server.lock）
│   └── ui/
│       ├── MainFrame.java             # Swing 主窗体（FlatLaf + 过滤 + 菜单维护 + 危险操作二次确认）
│       ├── TrayManager.java           # 系统托盘（强制英文防 Win10 HWND 乱码）
│       ├── ServerController.java      # Server ↔ DB ↔ UI 粘合层
│       └── WinAutostart.java          # HKCU Run 注册表开机自启
│
├── src/main/resources/
│   ├── app.properties                 # 内置默认配置
│   ├── messages_zh_CN.properties      # 中文文案
│   └── messages_en_US.properties      # 英文文案
│
├── launcher/                          # .NET Framework 4.8 便携 Launcher
│   ├── build-bundle.sh                # Launcher 打包（bash）
│   ├── build-bundle.ps1               # Launcher 打包（PowerShell）
│   ├── Launcher/
│   │   ├── Cognex.Launcher.csproj     # Costura.Fody + net48
│   │   ├── Program.cs                 # Mutex 单实例互斥 + jre.zip/app.jar 解压 + javaw.exe 启动
│   │   └── OpenJDK25U-jre_*.msi       # 可选：完整 JRE 安装包
│
├── dist/                              # 打包产物（已 .gitignore）
├── .gitignore
├── LICENSE
└── README.md
```

## 数据库 Schema

`audit_event` 表自动创建（H2，`MODE=PostgreSQL`）：

| 列 | 类型 | 说明 |
| --- | --- | --- |
| id | BIGINT PK AUTO | 自增主键 |
| ts | TIMESTAMP WITH TIME ZONE | 事件时间（UTC） |
| hostname | VARCHAR(255) | 相机主机名（如 `OP90-125`） |
| severity | INT | RFC 5424 严重级 0~7 |
| severity_name | VARCHAR(32) | emergency / alert / crit / err / warning / notice / info / debug |
| app_name | VARCHAR(255) | syslog 中的 APP-NAME 字段 |
| proc_id | VARCHAR(128) | PROCID |
| msg_id | VARCHAR(255) | MSGID |
| facility | INT | RFC 5424 facility 码 |
| source_ip | VARCHAR(64) | 连接方 IP（本程序反查 socket 得到） |
| message | CLOB | MSG 字段原文 |
| structured_data | CLOB | 解析后的 SD-ID → {param: value} JSON |
| raw_line | CLOB | 原始一行（供排障） |

索引：`ts`、`hostname`、`severity`，按 id 倒序分页查询。

**重要**：默认**不自动清理**任何日志。珍贵的审计日志全量保留，防止复盘时数据缺失。手动清理请通过「维护 → ⚠ 清理旧日志...」入口（含双重确认弹窗）。

## UI 功能速览

### 工具栏

| 控件 | 作用 |
| --- | --- |
| **Start / Restart Server** | 启动服务器；运行中点击触发「⚠ 二次确认」后重启（防现场鼠标误点） |
| **Host 下拉** | 按主机名筛选，可键入 `%` 通配符；刷新时自动下拉所有已见主机 |
| **Severity min / max** | 0~7 范围筛选，配合严重级色彩（红=error+，黄=warning，绿=notice/info/debug） |
| **Refresh** | 重新从 H2 加载 |
| **auto-scroll to newest** | 新事件到达时自动滚到顶部 |
| **⚙ 设置** | 设置对话框（主题、语言、开机自启、托盘行为） |

### 菜单栏

```
┌──────────┬──────────┬──────────┐
│ 文件(F)  │ 维护(M)  │ 帮助(H)  │
├──────────┼──────────┼──────────┤
│ 打开配置目录 │ ⚠清理旧日志...│ 关于 │
│ 打开数据库目录│          │        │
│ 设置...(Ctrl+S)│        │        │
│ 导出 JSON │          │        │
│ ────────  │          │        │
│ 退出      │          │        │
└──────────┴──────────┴──────────┘
```

**维护菜单中的危险操作**：
- **⚠ 清理旧日志...** — 一体化弹窗（输入保留行数 → OK/Cancel → 二次确认 YES/NO → 执行），默认选 NO 防误触，所有弹窗带 WARNING_MESSAGE 图标。

### 底部状态栏

实时显示运行状态 / 监听端口 / 是否启用 TLS。

## 安全设计

| 场景 | 防护 |
| --- | --- |
| **多实例互斥**（核心） | **双层防护**：① Launcher 端 `Global\\...Mutex` 拦截 EXE 多次双击（第二个 EXE 激活已有窗口后退出）；② Java 端 `FileChannel.tryLock()` 锁 `audit-log-server.lock`，拦截 `java -jar` 多开。进程死亡后锁自动释放，无残留 |
| 重启服务器（运行中） | ⚠ YES_NO 确认弹窗，显式提示「监听中断期间新到达日志会丢失」 |
| 清理旧日志 | 双重防护：① 输入弹窗 + 红色警示说明 ② YES_NO 二次确认，默认选 NO |
| 退出应用 | YES_NO 确认（托盘模式） |
| 托盘菜单 CJK 乱码 | 强制英文（Win10 原生 HWND 弹窗 + JDK 代码页组合下会失败） |

### 为什么要双层互斥？

实测发现：**便携 Launcher 无任何互斥检查**，现场多次双击 EXE 后同时跑出 4 个 `javaw.exe`，互相抢 5140 端口和 H2 锁文件，导致「单实例却报端口占用 + 数据库占用」。

| 入口 | Launcher Mutex | Java FileLock |
|------|---------------|---------------|
| 双击便携 EXE（第一次） | ✅ 拿到 | ✅ 拿到 |
| 双击便携 EXE（第二次） | ❌ 被拦截，退出 | —（根本没到 javaw） |
| 直接 `javaw -jar` | —（不经过 Launcher） | ❌ 被拦截，弹提示退出 |
| 进程被任务管理器杀掉 | Mutex 随进程释放 | FileLock 随进程释放 |

## 常见问题

### Q: 为什么不自动 prune？
A: 审计日志是故障复盘的唯一数据源，任何自动清理都是不可接受的数据丢失风险。数据库全量保留是默认且唯一的策略。

### Q: 便携 EXE 解压到哪？
A: 首次运行时自动解压到 `%LOCALAPPDATA%\CognexLogging\`（JRE + app.jar），已解压则跳过，EXE 升级后会覆盖。

### Q: Win10 工控机能直接用吗？
A: 便携 EXE **零外部依赖**（.NET Framework 4.8 是 Win10 LTSC 自带组件），直接双击。fat jar 需要系统 Java 25。

### Q: 启动后没反应？
A: 托盘模式下默认隐藏主窗体，查看系统托盘区（系统托盘图标是齿轮，tooltip 英文）。

### Q: 提示「软件已在运行」但找不到已有窗口？
A: 已有实例可能隐藏在系统托盘里（无可见主窗口）。去托盘区右键图标 → 显示，或在任务管理器结束 `javaw.exe` 后重启。

### Q: 任务管理器强杀后再启动，锁文件会残留吗？
A: **不会**。Mutex（.NET）和 FileLock（Java）都是 OS 级句柄，进程死亡由内核自动回收，不会残留 `audit-log-server.lock`。

### Q: 为什么我会有 4 个 javaw.exe 幽灵进程？
A: 这是修复前的问题——早期 Launcher 没有单实例互斥，多次双击 EXE 就会 spawn 多个 javaw 同时抢端口和 H2。修复后双层互斥确保无论从哪个入口进来都只有一个实例。

## 参考资料

- Cognex 官方指南：[How to Create an Audit Logging Server for In-Sight Vision Systems Cameras](https://support.cognex.com/en/help-articles/how-to-create-an-audit-logging-server-for-in-sight-vision-systems-cameras)
- RFC 5424：[The Syslog Protocol](https://www.rfc-editor.org/rfc/rfc5424.html)
- RFC 6587：[Transmission of Syslog Messages over TCP](https://www.rfc-editor.org/rfc/rfc6587.html)（octet-counting / transparent-framing）

## License

沿用远端仓库 `LICENSE` 文件。
