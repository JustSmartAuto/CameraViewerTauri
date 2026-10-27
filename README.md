# Audit Log Server for Cognex In-Sight

Cognex In-Sight Vision Systems 审计日志集中存储与可视化服务器。一个 Java 25 单 jar 可执行程序，监听 TCP 5140 端口（Fluentd 默认转发端口），同时提供现代化 Swing 桌面界面浏览和筛选多台相机的审计日志。

---

## 特性

- 📡 **多相机客户端监听** — RFC 5424 Syslog TCP 服务（默认端口 5140），支持 Fluentd `octet-count framing` 和 `NL framing` 两种帧格式
- 🔐 **可选 TLS/SSL** — 在 `app.properties` 中启用，加载 PKCS12 证书链后支持相机客户端双向认证
- 💾 **H2 内嵌存储** — 文件模式持久化，带时间戳/主机/严重级索引，自增主键
- 🖥️ **FlatLaf 现代化 UI** — 过滤（主机 + 严重级）、分页、自动滚动、严重级色彩渲染、一键导出 JSON / 裁剪旧行
- 🧵 **虚拟线程高并发** — Java 25 `Executors.newVirtualThreadPerTaskExecutor()`，一台 PC 同时处理数十台相机连接无压力
- 📦 **Fat Jar 单文件分发** — 所有依赖打入一个 jar，Windows / Linux 直接 `java -jar` 运行

## 技术栈

| 组件 | 版本 | 备注 |
| --- | --- | --- |
| OpenJDK | 25.0.2 LTS | `sourceCompatibility` / `targetCompatibility` = Java 25 |
| Gradle | 9.7.1 | 项目自带 `gradle-dist/` 免安装 |
| FlatLaf | 3.7.1 | `FlatIntelliJLaf`（默认）/ `FlatDarkLaf` 可选 |
| H2 Database | 2.5.252 | 文件模式，`MODE=PostgreSQL` |
| Gson | 2.11.0 | 结构化数据序列化 |
| SLF4J Simple | 2.0.16 | 日志 |

## 快速开始

### 1. 构建

```bash
# bash / WSL
./build-with-timestamp.sh

# PowerShell（Windows 原生）
.\build-with-timestamp.ps1
```

脚本先跑 `gradle fatJar`，再把产物复制到项目根目录并追加毫秒级时间戳后缀，例如：

```
audit-log-server-1.0.0-fat_20261027173012457.jar
```

> 若本地没有系统 Gradle，脚本会优先使用项目内 `gradle-dist/gradle-9.7.1/bin/gradle`。
> 若目录不存在，解压 [gradle-9.7.1-bin.zip](https://services.gradle.org/distributions/gradle-9.7.1-bin.zip) 到 `gradle-dist/` 即可。

### 2. 运行

```bash
java -jar audit-log-server-1.0.0-fat_20261027173012457.jar
```

带 `--autostart` 参数可在界面弹出的同时立即启动服务器，适合无人值守场景：

```bash
java -jar audit-log-server-*.jar --autostart
```

### 3. 配置

首次运行会在当前目录读取 `app.properties`（不存在则使用内置默认值）。模板位于 `src/main/resources/app.properties`：

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
audit-log-server/
├── build.gradle                       # Gradle 构建（含 fatJar task）
├── settings.gradle
├── build-with-timestamp.sh            # bash / WSL 打包脚本
├── build-with-timestamp.ps1           # PowerShell 打包脚本
├── gradle-dist/gradle-9.7.1/          # 可选：本地 Gradle 发行包
│
├── src/main/java/com/auditlog/
│   ├── AuditLogApp.java               # 入口（theme → H2 → MainFrame → autostart）
│   ├── config/AppConfig.java          # 属性加载
│   ├── server/
│   │   ├── SyslogParser.java          # RFC 5424 解析（含 octet-count + NL framing）
│   │   ├── SyslogMessage.java         # 解析结果 record
│   │   └── SyslogServer.java          # TCP 监听 + TLS 支持 + 虚拟线程
│   ├── db/
│   │   ├── H2Store.java               # H2 内嵌存储（schema / insert / query / prune）
│   │   └── AuditRow.java              # UI 行 record
│   └── ui/
│       ├── MainFrame.java             # Swing 主窗体（FlatLaf + 过滤 + 导出）
│       └── ServerController.java      # Server ↔ DB ↔ UI 粘合层
│
├── src/main/resources/
│   └── app.properties                 # 内置默认配置
│
├── .gitignore
├── LICENSE
├── convert_audit_log.py               # 配套工具：Fluentd JSON → XLSX/MD
└── .trae/skills/audit-log-converter/  # Trae 项目技能
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

## UI 功能速览

| 控件 | 作用 |
| --- | --- |
| **Start/Stop Server** | 启动或停止 syslog 监听（按钮文字随状态切换） |
| **Host 下拉** | 按主机名筛选，可键入 `%` 通配符；刷新时自动下拉所有已见主机 |
| **Severity min / max** | 0~7 范围筛选，配合严重级色彩（红=error+，黄=warning，绿=notice/info/debug） |
| **Refresh** | 重新从 H2 加载 |
| **auto-scroll to newest** | 新事件到达时自动滚到顶部 |
| **Export JSON** | 把 `audit_event` 全表导出为 JSON 数组文件（分页读取，支持百万行） |
| **Prune** | 保留最近 N 条，删除最旧数据，避免数据库无限增长 |
| 底部状态栏 | 实时显示运行状态 / 监听端口 / 是否启用 TLS |

## 参考资料

- Cognex 官方指南：[How to Create an Audit Logging Server for In-Sight Vision Systems Cameras](https://support.cognex.com/en/help-articles/how-to-create-an-audit-logging-server-for-in-sight-vision-systems-cameras)
- RFC 5424：[The Syslog Protocol](https://www.rfc-editor.org/rfc/rfc5424.html)
- RFC 6587：[Transmission of Syslog Messages over TCP](https://www.rfc-editor.org/rfc/rfc6587.html)（octet-counting / transparent-framing）
- 本仓库配套技能：`.trae/skills/audit-log-converter/` — 把 Fluentd 导出的审计日志 JSON 一键转为 XLSX + Markdown 分析报告
- 实测样本：`111.json`（OP90-125 相机 805 条审计事件，2026-08-06 ~ 2026-10-26）

## License

沿用远端仓库 `LICENSE` 文件。
