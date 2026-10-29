# Cognex In-Sight 审计日志分析经验总结

> 适用场景：分析由 In-Sight Vision Suite 审计日志（Audit Logging）导出的 JSON 文件。
> 参考官方说明：本目录《Cognex_InSight_Audit_Logging.md》（In-Sight Vision Suite — Audit Logging 文档）。
> 经验来源：2026-10 对 OP90-125 视觉设备导出文件 111.json（805 条事件、跨 2.5 个月记录）的完整分析实战。
> 配套产物：同目录《111.json分析报告.md》。

---

## 1. 记住这条日志的"出身"（来自官方文档的要点）

1. **默认开启**：审计日志由 In-Sight 自动启用，无需人为开启——所以任何一台发过日志的设备**必然有记录**，找不到记录时应怀疑下载途径而非配置。
2. **下载或转发需要 Full 权限**：下载日志/配置转发要求 **Full 用户访问级别**。看到有人下载审计日志，即说明其拥有最高权限，这一点本身就可作为权限与口令管控的检查项。
3. **两种采集方式二选一或并行**：
   - **Download**：一次性导出（本目录 111.json 即该方式产物，JSON 数组、UTC 时间、微秒精度）；
   - **Enable Forwarding**：以 **syslog 转发**持续推送到远程 syslog 服务器。生产环境建议启用转发做集中留存，本地日志缓冲有限、且靠人工下载天然有断档。
4. **触发来源（官方文档四分类）**：①电子表格编辑器变更 ②系统变更 ③复杂工具变更 ④实用程序变更。这正是我们用 `type`/`event` 进行分组统计的理论基础。

## 2. 数据模型速查（以 111.json 实测为准）

每条记录固定如此（字段含义 + 观察到的取值特征）：

| 字段 | 含义 | 实测特征 |
|---|---|---|
| `timestamp` | 事件时间 | ISO 8601、**UTC**、含微秒、带 +00:00；分析时一律先换算现场时区（Asia/Shanghai +8） |
| `hostname` | 设备名 | 单值（OP90-125）；若混分析多设备，此字段是分组键 |
| `severity` | 严重级 | 实测仅 notice/info 两级；**未出现过 error/warning** |
| `type` | 事件大类 | audit-log / network / auth / device-connect / tool-change / cell-change / online-state-change / job-change / acquisition-settings / time / utility |
| `event` | 事件名 | save-job、login-success、internal-data、config-interface-parameter…… |
| `result` | 人读描述 | 作业保存名、工具名（EditRegion）、或空 |
| `structuredData` | 结构化数据 | 族名带 **SD-ID 39975**（固件族常量，非事件序号） |

structuredData 内的四个子块：

| 子块 | 出现条件 | 用途 |
|---|---|---|
| `auditEvent@39975` | 每条必有 | `source`（humanUser/device）、`category`（auditLog/config/accessControl）、**自增 id** |
| `auth@39975` | 有人身份时存在 | `user`（实测全是 admin；network 与 startup 事件无此块，共缺 71/805） |
| `configChange@39975` | 有配置动作时存在 | `old`/`new` 前后值 + `property`；**内容形态随事件类型而变**（见 §6） |
| `timeQuality` | 每条必有 | `tzKnown` 常为 1；**`isSynced` 是理解早期时间可靠性的钥匙**（NTP 配置前=0） |

## 3. 官方文档分类 ↔ JSON type/event 实测对照表

| 官方文档类别 | 实测 type | 实测 event | 备注 |
|---|---|---|---|
| 电子表格编辑器变更·单元格表达式 | cell-change | cell-expression | old/new 为 `[{location, data:{value}}...]`；公式可全文 diff |
| 电子表格编辑器变更·启用/禁用状态 | cell-change | cell-enabled-state | **一次数百单元格**——是"整块拷贝/粘贴程序段"的指纹 |
| 电子表格编辑器变更·注释/值/符号标签 | （与 cell-expression 合并记录，未见独立 event） | — | 注释修改与公式修改混在一起，靠值前缀 `'` 识别注释类 |
| 复杂工具变更 | tool-change | internal-data | **`result` 字段 = 工具/对话框名**：EditRegion、EditFloat、EditString、EditCompositeRegion、CheckBox、ListBox、TrainPatMaxRedLine… |
| 系统变更·在线/离线 | online-state-change | online / offline | 与编辑会话一一对应 |
| 系统变更·保存/加载作业 | job-change | save-job / load-job | result="文件名 Saved/Loaded" |
| 系统变更·传感器菜单四项（Results Queue 等） | **实测未出现** | — | 尚无样例，未验证字段形态 |
| 实用程序·连接电子表格 | device-connect | spreadsheet-connect / spreadsheet-disconnect | 唯一以 **info** 级记录的事件（其余均为 notice） |
| 实用程序·启动设置 | utility | startup-settings | **启动作业被切换时出现**——判断新作业"正式投产"的开关事件 |
| 实用程序·网络设置 | network | config-interface-parameter | property="Wired connection"，含完整 IPv4 上下文 |
| 实用程序·备份/还原、固件、出厂设置、用户设置 | **实测未出现** | — | 未发生过这类操作；字段形态未验证 |
| （文档未单列，实测补充）2D 采集 | acquisition-settings | 2D-acquisition | 曝光/光源/白平衡/SurfaceFX/镜头位置，成组下发 |
| （文档未单列，实测补充）时间配置 | time | change-ntp-status / change-timezone / change-ntp-server | 三条成串出现 |
| （文档未单列，实测补充）审计日志自身 | audit-log | startup | source=**device**（全文件唯一非人工事件），即日志/审计功能起效时点 |

> 使用建议：遇到新设备/新固件版本，先用 §5 的 1~3 步生成 type×event 全景表，再对照本表校准，**不要假设未列事件不存在**。

## 4. 分析八步工作流（可直接照做）

1. **解析与校验**：解析 JSON 数组；校验审计 `id` 连续性（实测 0~804 无缺失，可确认导出完整）。用 `max(id)+1 == count` 快速判断。
2. **全景统计**：`severity`、`hostname`、`type`、`event`、`source`、`user`、`isSynced` 各出一张计数表。
3. **时间维度**：换算现场时区后出**按日直方图 + 每日活跃窗口（当日首/末事件）**；对 save-job 补一个"最大单小时保存数"用于看调试强度。
4. **会话链分析**：把 `login → spreadsheet-connect → offline → (编制/调参) → save-job → online → disconnect` 识别为一个编辑会话。login 有成对冗余（见 §7），需按间隔 < 2.5s 聚类去重。
5. **内容 diff**：逐条提取 `configChange.old/new`：
   - 工具编辑：取 `location` 字段统计"哪些单元格被反复编辑"（Top 榜基本就定位了问题区域）；
   - cell-expression：比对公式字符串差异，标出变化的参数位（如 FindPatMaxRedLine 第 18/19 参数 = 搜索窗口，反复收放为鲁棒性调参）。
6. **业务还原**：从公式与中文注释（`'缩口测量`、`'压平检测`）反推检测项、标准值（InRange 边界）、程序段结构（Switch/Jump/~30/B11 主开关）、HMI 组件（PlotString/CheckBox/ListBox/Button/WriteImage），拼出"程序功能地图"。
7. **异常信号扫描**：见 §7 的坑与信号清单（网口频繁启停、共享账号、上线遗留离线等）。
8. **成文**：按 §10 报告骨架输出，附时间线表 + 作业台账 + Top 编辑单元格表 + 风险建议。

## 5. 实测中积累的关键数量关系（基准参考）

以 111.json 为例，供同类分析时做"比例感"对照：

- 全量 805 条 = save-job 231 + auth 176 + tool-change 101 + cell-change 81 + network 70 + acquisition-settings 48 + online-state-change 44 + device-connect 40 + time 3 + audit-log 1 + utility 1。
- 人工事件占比 99.9%（humanUser 804 / device 1）；只有 startup 是 device 来源。
- 登录 176 次 → 2.5s 聚类后 ≈ 96 个会话，**成对登录率约 45%**。
- 电子表格 连接 20 / 断开 20，与上线 22 / 下线 22 几乎互为会话记号。
- network 启用 35 / 禁用 35 **完全对称**（每次启用=同一套静态 IP 配置，意味着这些切换不是改配置，而是开关动作本身）。
- 三个作业文件的保存比：新作业 203 / 首作业 21 / 旧模板 7；加载次数 9 次（其中开发期回滚重载占 5 次）。
- 峰值强度：单日 223 条、单小时 58 次保存（≈1 次/分钟的一边改一边存节奏）。

## 6. configChange 内容形态速查（解析代码必须分支处理）

| type/event | old/new 形态 | 抓取要点 |
|---|---|---|
| tool-change/internal-data | `{location, data:{x,y,width,height,angle,...}}` 或 `{value}` | `location` 缺失时看另一侧；区域类小心 Boolean 差异 |
| cell-expression | 数组 `[ {location, data:{value:"公式"} } ]` | 公式当字符串 diff；取 `(` 前函数名归类 |
| cell-enabled-state | 数组，数组（大量单元格） | 数量即"块操作"规模；old/new 都是启用参数表达式 |
| config-interface-parameter | `{property:"Wired connection", old/new:{connected, ipv4}}` | 启用=~connected true，含 gateway/IP/method/DNS 全套 |
| 2D-acquisition | `{property:None, new:{曝光/光源/白平衡/SurfaceFX 子键}}` | 相邻 4~7 条 = 同一次"应用"的成组下发 |
| time / startup-settings | 标量或 `{job: "..."}` | 启动作业切换老值→新值即作业投产记录 |
| 有新值无 auth 块 | — | network 全部无 user；不要把无 user 当"匿名操作"问题 |

## 7. 踩过的坑与值得记牢的信号

**坑（解析/口径类）**

1. **PowerShell 5.1 解析会失败**：`ConvertFrom-Json` 受 JavaScriptSerializer 限制 + 中文路径 GBK 控制台编码问题，直接用 **Python（utf-8 显式解码）** 最稳；Python 脚本输出要 `sys.stdout.reconfigure(encoding='utf-8')`，PowerShell `>` 重定向会写 UTF-16 造成"读不回"假象。
2. **时间一律 UTC → +8 换算后再做按日统计**，否则"按天窗"整体错一天。
3. **登录成对出现**（80/176 与前条间隔 <2.5s），直接数 login 会高估使用次数近一倍；先聚类再统计会话数。
4. **"location; data"占位记录**：偶见 old/new 直接为 `"location; data"` 字符串（本例 5 条），是系统对整块操作合并记录的占位，属正常噪声，勿当解析错误。
5. **审计日志只记成功/操作事件**：没有 NG 率、报警、断电重启信息；"日志无 error" 不等于"设备无异常运行"。
6. **isSynced=0 时期的记录时间戳不可靠**（NTP 配置前，本例只有最初 3 条），大跨度结论只用同步后的数据。
7. **日志首条 = startup（device 来源）**：由此确定"本导出=审计功能启用以来全部记录"；若某设备首条不是 startup，则说明导出并非完整存量（有删/轮转），需向客户求证日志保留策略。

**信号（判断类）**

1. **网口反复 启用/禁用 + 循环"同配置"** = 网络排查动作（本例 10-26 一天 16 轮），而不是有人在玩设置；同时无 save-job、无 cell-change 即可佐证"纯网络问题日"。
2. **上线转为 offline 后紧跟 save-job**属于流程规范；若每日收尾只有 offline 没有 online，是"忘上线遗留"风险信号。
3. **load-job 在开发时段密集出现** = 现场在回滚版本（工作量证据，也是无版本管理的间接证据）。
4. **utility/startup-settings 的 job 变更** = 新作业投产的确切时点，写进时间线。
5. **A 单元格（如 A59"启用调整参数"CheckBox）多次切换** = 参数界面开关被反复试——结合 EditString/ListBox 即还原出 HMI 参数面板；投产后该开关若仍可自由操作是风险建议来源。
6. **单元格启用状态（cell-enabled-state）大批量变更** = 程序段克隆/搬迁——时间点正好落在"新建作业"之日。
7. **admin 账号唯一**（user 全为 admin 且无其他用户出现）= 无法追溯个人责任的合规缺口，应写进建议。

## 8. 指标口径建议（报告级统一用语）

| 指标 | 口径 |
|---|---|
| 记录量/覆盖 | 事件总数 & id 连续性（0..max 无缺） |
| 编辑会话 | spreadsheet-connect 次数（或 login 聚类数） |
| 活跃窗口 | 当日首条 ~ 末条事件（现场时区） |
| 调试强度 | 当日事件峰值 + 单小时 save-job 峰值 |
| 版本/投产 | save-job 按作业计；load-job 时间线；startup-settings 变更时点 |
| 责任归属 | 按 user 计（共享账号则注明不可追溯） |
| 网络稳定性 | config-interface-parameter 启停次数 × 日期分布 |
| 打光/成像参数 | acquisition-settings 按日 + 参数组还原 |

## 9. 一段可复用的底稿脚本（Python 3）

与本次 111.json 分析所用脚本同型，替换路径即可产出核心统计（省去重复造轮子）：

```python
import json, collections, datetime, sys
sys.stdout.reconfigure(encoding='utf-8')

path = r"D:\JustSmart\项目目录\xxx.json"
data = json.load(open(path, encoding='utf-8'))

def bj(d):  # UTC -> 北京时间
    return datetime.datetime.fromisoformat(d['timestamp']) + datetime.timedelta(hours=8)

n = len(data)
print("TOTAL", n, "SPAN", data[0]['timestamp'], "->", data[-1]['timestamp'])

ids = sorted(int((d['structuredData']['auditEvent@39975'])['id']) for d in data)
print("ID ok:", ids == list(range(ids[0], ids[-1] + 1)))          # 连续性校验

print("TYPE:", collections.Counter(d['type'] for d in data))
print("EVENT:", collections.Counter(d['event'] for d in data))
print("SOURCE:", collections.Counter(d['structuredData']['auditEvent@39975']['source'] for d in data))
print("USER:", collections.Counter(d['structuredData'].get('auth@39975', {}).get('user', 'None') for d in data))

day = collections.Counter(bj(d).strftime('%Y-%m-%d') for d in data)
print("DAILY:", dict(sorted(day.items())))                         # 按日直方图

# 登录会话聚类（间隔<2.5s合并）
logins = sorted(bj(d) for d in data if d['event'] == 'login-success')
sessions = sum(1 for i, t in enumerate(logins) if i == 0 or (t - logins[i-1]).total_seconds() >= 2.5)
print("login/sessions:", len(logins), "/", sessions)

# 作业保存台账：作业文件 × 日
sv = collections.Counter()
for d in data:
    if d.get('event') == 'save-job':
        sv[(str(d['result']).removesuffix(' Saved'), bj(d).strftime('%Y-%m-%d'))] += 1
print("SAVES:", dict(sorted(sv.items())))

# 网络启停按日
net = collections.Counter()
for d in data:
    if d['type'] == 'network':
        newv = d['structuredData']['configChange@39975']['new']
        net[(bj(d).strftime('%Y-%m-%d'), 'enable' if newv.get('connected') else 'disable')] += 1
print("NET:", dict(sorted(net.items())))

# 工具编辑 Top 单元格
loc = collections.Counter()
for d in data:
    if d['type'] == 'tool-change':
        cc = d['structuredData'].get('configChange@39975') or {}
        x = cc.get('new') or cc.get('old') or {}
        if isinstance(x, dict): loc[x.get('location',)] += 1
print("TOP LOC:", loc.most_common(12))
```

## 10. 分析报告骨架（建议章节）

1. 概要结论（设备身份 / 时间跨度 / 数据完整性 / 主要活动一句话概括）
2. 数据格式说明（本报告 §2 的字段表）
3. 数据完整性与质量（id 连续性、isSynced、特殊记录）
4. 事件类型分布（type 表 + event 表）
5. 时间分布（按月/按日 + 活跃窗口表）
6. 分项详析
   - 登录与账户（会话数、共享账号问题）
   - 作业管理（保存/加载台账、启动作业切换时点）
   - 上线状态与编辑会话
   - 程序开发轨迹（Top 编辑单元格 + 关键公式变化叙事）
   - 采集参数配置
   - 网络配置（按日启停 + 解读）
   - 系统时间与 NTP
7. 业务逻辑推断（检测项/标准值/HMI/存图功能区还原）
8. 风险点与建议（分级/优先级）
9. 附录（作业台账、Top 单元格、统计口径）

## 11. 尚未验证/待补充事项

- `warning/error` 级别是否会在故障时出现（实测样本未含固件故障）。
- 官方文档"传感器菜单四项（Results Queue / Job Status / Custom View / Discrete IO）"与"备份还原、固件更新、出厂设置、用户设置"等的实际 type/event 形态——遇到新样例首上次补。
- 本地审计日志的**容量上限与轮转策略**未知：靠 Download 回采的日志只会越来越不完整，**集中部署务必启用 syslog Forwarding**。
- 成对登录的机制（推测为 UI 会话多通道各自上报），不影响统计口径但未深究固件实现。

## 12. 参考文件

- 《Cognex_InSight_Audit_Logging.md》 — In-Sight Vision Suite 审计日志官方操作文档（触发分类、Download/Forwarding 操作步骤、Full 权限说明）
- 《111.json分析报告.md》 — OP90-125 完整分析报告（本经验的第一个实战成果）
- Cognex 文档站：User Settings 帮助页 https://docs.cognex.com/is-usp_2440/web/EN/Help_ISVS/Content/Topics/utilities/user-settings.htm （官方文档内引用的链接）
