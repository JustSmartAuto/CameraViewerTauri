# CameraCellMonitor — Cognex In-Sight 单元格数据监控器

通过 **CogSocket API（WebSocket / JSON-RPC）** 订阅 Cognex In-Sight 智能相机电子表格的指定单元格，持续采集检测数据并落库，用于**计算数据波动情况、调试生产用视觉判断阈值**。

> 隶属于 [CameraViewerTauri](https://github.com/JustSmartAuto/CameraViewerTauri) 仓库的 `cell-monitor` 分支。
> 界面与交互参考 `显示软件=20260321`（CameraHelper，WinForms 多相机宫格显示软件）。

## 功能目标

- **多相机接入**：同时连接多台 In-Sight 相机（CogSocket `ws://<相机IP>:<端口>/ws`，固件 22.2+ 默认端口 80，6.x+ 为 8087），单相机掉线自动重连不影响其他相机
- **多单元格订阅**：每相机可配置多个电子表格单元格（如测量值、分数、判定结果），支持按结果事件触发采样或按固定周期轮询
- **单元格数据类型**：支持 `auto / int / float / string / bool / other`，设置时类型未知可用 `auto`（运行时按值推断）；上下限阈值**仅对数值类型（int/float/auto）生效**
- **记录模式**：
  - **simple（简单记录）**：仅记录单元格数据，不做阈值判定，判定结果恒为 NULL，适合单纯采集数据的场景
  - **advanced（高级记录）**：记录数据并按上下限阈值判定 OK/NG，适合调试视觉判断阈值
- **交互式单元格配置**：表格化编辑器（增删改 + 上下移），单单元格表单中上下限输入框会根据数据类型与记录模式自动启用/禁用；同时保留 CSV 文本批量编辑（兼容旧格式）
- **双路落库**：
  - **xlsx 导出**：每个单元格一个工作表（时间戳/数值/原始值/判定，NG 行标红），并额外生成 **「合表」**——同一次拍照（同一时间戳）下所有单元格合并为一行，表头为「单元格名（注解）」，便于观察同一次检测中各数据的相互关系
  - **SQLite3**：增量写入所有采样点（时间戳、相机、单元格、值、OK/NG），便于 SQL 统计波动（均值/标准差/CPK/超差率）
- **可视化**：折线图、散点图、趋势图（虚线基线 = 参考值/阈值，红色段 = 超差），**中文字体内嵌 `assets/`**（matplotlib 直接加载 TTF，避免图表标题/图例乱码）
- **导出文件带时间戳**：所有导出文件（xlsx 报表、图表图片、数据备份等）文件名均追加时间戳后缀（`_yyyyMMdd_HHmmss`），便于区分多次导出的版本，避免覆盖
- **默认数据目录**：应用数据默认存放于 `D:/JustSmart/CameraCellMonitor`（如 D 盘不存在或无权限则回退 `~/AppData/CameraCellMonitor`），启动时自动递归创建目录（含 xlsx/图表/数据库子目录）
- **阈值调试辅助**：基于历史数据回放 + 超差率统计，辅助调整视觉判断阈值上下限
- **界面自适应**：相机宫格三列等宽；单元格名称与数值按显示宽度定宽（CJK 计 2 宽）并省略截断，**鼠标悬停显示完整文本**（含完整数值精度与注解），长数据不会撑破布局
- **网格纵向滚动**：相机数量多时超出可视区域的行可通过滚轮/滚动条查看，滚轮仅在网格区域内生效，不影响日志区
- **异常隔离**：单个单元格取值/判定/落库抛异常时仅记录告警并跳过该单元格，不干扰同一次采样的其他单元格，也不中断采集循环
- **菜单栏**：帮助 → 使用说明 / 关于（含版本号）

## 技术要点

| 项 | 方案 |
|---|---|
| 语言/环境 | Python 3.13 |
| 包管理 | [uv](https://github.com/astral-sh/uv)（`pyproject.toml` + `uv.lock`） |
| 相机通信 | CogSocket WebSocket（JSON-RPC：GET/POST/PUT/DELETE + 事件订阅），参考 `reference/CogSocket&WebApi.md` |
| 数据存储 | `openpyxl`（xlsx）+ 标准库 `sqlite3` |
| 图表 | `matplotlib`（中文字体从 `assets/*.ttf` 加载，不依赖系统字体） |
| 界面 | tkinter / Web（参照 CameraHelper 的多相机宫格交互） |
| 打包 | PyInstaller 单文件 exe，命名 `exe_{yyyyMMddHHmmss}`（如 `exe_20261031113000`），logo 使用 `assets/app_icon.ico`（`--icon` + `--add-data assets`） |

## 采样机制（何时取单元格的值）

单元格取值时机由其 `sample_mode` 决定：

| 模式 | 触发方式 | 说明 |
|---|---|---|
| `event` | 相机 `resultChanged` 事件 | 订阅 `{sid}/resultChanged`，每当相机完成一次检测并产出新的 `HmiResult` 时，回调 `on_result` 调用 `parse_cells()` 提取配置单元格的值并落库；处理完毕后调用 `ready()` 应答，相机才继续下一帧（背压控制） |
| `poll` | 软件定时轮询 | 每隔 `poll_interval` 秒主动调用 `queryCellResults` 拉取指定单元格的当前值 |

阈值判定（OK/NG）仅在 **advanced 记录模式 + 数值类型（int/float/auto）** 时进行；simple 模式或非数值类型的单元格判定结果恒为 NULL，但数据照常落库。

## 目录

| 路径 | 说明 |
|---|---|
| `reference/CogSocket&WebApi.md` | CogSocket & HMI/Platform Web API 参考（连接、单元格读写、事件订阅） |
| `reference/Cognex In-Sight 3D 相机电子表格函数参考（整理版）.md` | 电子表格函数参考 |
| `reference/相机数据监控器_web_2610171547.py` | 参考实现：S7 PLC 相机坐标监控器（tkinter + 内嵌 Web），可复用其 Web/配方/趋势图交互 |
| `assets/app_icon.ico` | 应用图标（由 `monitor.jpg` 拉伸方形 + 16% 圆角生成） |
| `assets/monitor.jpg` / `monitor.png` | 图标源图 |

## 开发 / 运行

```bash
uv sync                              # 安装依赖
uv run cell-monitor                  # 启动应用
uv run cell-monitor --simulate       # 启动内置模拟相机离线体验完整流程
uv run python build_exe.py           # PyInstaller 打包 dist/exe_{yyyyMMddHHmmss}.exe
```

源码结构：`src/cellmonitor/`（`cogsocket` 通信、`worker` 采集调度、`storage` 落库、`stats` 统计、`charts` 图表、`ui` tkinter 宫格界面、`simulator` 模拟相机）。

## License

[LICENSE](LICENSE) © JustSmartAuto
