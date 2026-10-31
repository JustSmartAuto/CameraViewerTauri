# CameraCellMonitor — Cognex In-Sight 单元格数据监控器

通过 **CogSocket API（WebSocket / JSON-RPC）** 订阅 Cognex In-Sight 智能相机电子表格的指定单元格，持续采集检测数据并落库，用于**计算数据波动情况、调试生产用视觉判断阈值**。

> 隶属于 [CameraViewerTauri](https://github.com/JustSmartAuto/CameraViewerTauri) 仓库的 `cell-monitor` 分支。
> 界面与交互参考 `显示软件=20260321`（CameraHelper，WinForms 多相机宫格显示软件）。

## 功能目标

- **多相机接入**：同时连接多台 In-Sight 相机（CogSocket `ws://<相机IP>:<端口>/ws`，固件 22.2+ 默认端口 80，6.x+ 为 8087），单相机掉线自动重连不影响其他相机
- **多单元格订阅**：每相机可配置多个电子表格单元格（如测量值、分数、判定结果），支持按结果事件触发采样或按固定周期轮询
- **双路落库**：
  - **xlsx 导出**：按 相机/日期/单元格 生成工作表，便于人工查阅与报表
  - **SQLite3**：增量写入所有采样点（时间戳、相机、单元格、值、OK/NG），便于 SQL 统计波动（均值/标准差/CPK/超差率）
- **可视化**：折线图、散点图、趋势图（虚线基线 = 参考值/阈值，红色段 = 超差），**中文字体内嵌 `assets/`**（matplotlib 直接加载 TTF，避免图表标题/图例乱码）
- **阈值调试辅助**：基于历史数据回放 + 超差率统计，辅助调整视觉判断阈值上下限

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

## 目录

| 路径 | 说明 |
|---|---|
| `reference/CogSocket&WebApi.md` | CogSocket & HMI/Platform Web API 参考（连接、单元格读写、事件订阅） |
| `reference/Cognex In-Sight 3D 相机电子表格函数参考（整理版）.md` | 电子表格函数参考 |
| `reference/相机数据监控器_web_2610171547.py` | 参考实现：S7 PLC 相机坐标监控器（tkinter + 内嵌 Web），可复用其 Web/配方/趋势图交互 |
| `assets/app_icon.ico` | 应用图标（由 `monitor.jpg` 拉伸方形 + 16% 圆角生成） |
| `assets/monitor.jpg` / `monitor.png` | 图标源图 |

## License

[LICENSE](LICENSE) © JustSmartAuto
