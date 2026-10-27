---
name: audit-log-converter
description: Converts Cognex In-Sight Fluentd audit log JSON to human readable XLSX and Markdown reports. Use when analyzing audit log JSON, converting fluentd output, or generating audit reports. Do not use for generic JSON or non Cognex formats.
---

# Audit Log Converter

将 Cognex In-Sight Vision Systems 经 Fluentd 导出的审计日志 JSON 数组，转换为人类可读的 XLSX 明细表 + Markdown 统计报告。

## 输入识别

用户提供的 JSON 需满足：
- 顶层为数组，每条元素是一个审计事件对象
- 至少包含 `timestamp`, `hostname`, `severity`, `type`, `event`, `structuredData` 字段
- `structuredData` 内含 `auditEvent@39975`、可选 `configChange@39975` / `auth@39975` / `timeQuality` 等 RFC 5424 风格的结构化数据键

不满足以上特征时停止并告知，不要强行转换。

## 执行步骤

### 1. 运行脚本

```bash
python scripts/convert_audit_log.py <json-path> [--outdir DIR] [--prefix NAME] [--tz 8.0]
```

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `json` | 必填 | 审计日志 JSON 文件路径 |
| `--outdir` | 输入文件所在目录 | 输出目录 |
| `--prefix` | 输入文件名（无扩展名） | 输出文件名前缀 |
| `--tz` | `8.0` | 本地时区偏移小时数，`8.0` = 北京时间，UTC 用 `0.0` |

依赖仅 `openpyxl`，Python 3.x 通常已预装。若缺失，执行 `python -m pip install openpyxl`。

### 2. 输出产物

| 产物 | 说明 |
| --- | --- |
| `<prefix>_审计日志明细.xlsx` | Sheet1 明细（14 列，冻结首行 + 自动筛选 + 斑马纹），Sheet2 统计概览（总览 / 按月 / 按类型 / 按事件 / 作业台账） |
| `<prefix>.md` | 9 章节 Markdown 报告：概要 → 按 type → 按 event → 按月 → 活跃日 → 登录 → 作业台账 → 网络 → 最近 50 条 |

### 3. 核心字段映射

| JSON 字段 | XLSX 列 | 处理规则 |
| --- | --- | --- |
| `timestamp` (UTC) | `时间(北京)` | 按 `--tz` 偏移转换为 naive datetime |
| `structuredData.auditEvent.*.id` | `审计ID` | 可空 |
| `structuredData.auditEvent.*.source` | `来源` | `humanUser`→人工操作，`device`→设备自动 |
| `structuredData.auth.*.user` | `用户` | 可空 |
| `structuredData.timeQuality.isSynced` | `时间同步` | `1`→已同步，`0`→未同步 |
| `type` + `event` + `result` + `structuredData.configChange.*` | `变更内容摘要` | 见下方事件类型摘要规则 |
| `structuredData.configChange.*.old` / `.new` | `旧值` / `新值` | JSON 序列化，`null` 保持 None |

### 4. 事件类型摘要规则

脚本内置 `summarize_event()` 函数，按 `(type, event)` 生成中文可读摘要：

| type | event | 摘要示例 |
| --- | --- | --- |
| `audit-log` | `startup` | 设备启动（审计日志起点） |
| `network` | `config-interface-parameter` | 网口启用: IP 192.168.20.125/24, 手动, 网关 192.168.20.231 / 网口禁用 |
| `auth` | `login-success` | 用户登录: admin |
| `job-change` | `save-job` / `load-job` | 作业保存: 1-20260915.jobx / 作业加载: 1-JZC1B01-1A.jobx |
| `utility` | `startup-settings` | 启动作业: /1-JZC1B01-1A.jobx → /1-20260915.jobx |
| `cell-change` | `cell-expression` | 单元格公式修改: A43,B52,A52 |
| `cell-change` | `cell-enabled-state` | 单元格启用状态变更: A47...等22处 |
| `tool-change` | `internal-data` | 检测区域编辑: A47 / 数值参数编辑: L87 |
| `tool-change` | `training-data` | PatMax 模板训练: B45 |
| `online-state-change` | `online` / `offline` | 转上线 / 转离线 |
| `device-connect` | `spreadsheet-connect` / `disconnect` | 电子表格连接 / 断开 |
| `acquisition-settings` | `2D-acquisition` | 2D 采集参数下发 (Computational Imaging Settings Change) |
| `time` | `change-ntp-server` / `change-timezone` / `change-ntp-status` | NTP 服务器: 192.168.20.96 / 时区设置: Asia/Shanghai |

遇到未覆盖的 `(type, event)` 组合时，摘要回退为 `"{type}/{event}"`，不丢数据。

### 5. 交付

运行完成后，告知用户：
- 产物的绝对路径（XLSX + MD）
- 事件总数、审计 ID 范围是否连续、是否有 `isSynced=0` 的早期记录

## 不要做的事

- 不要改写原始 JSON
- 不要猜测未见过的事件类型摘要，让 `summarize_event()` 回退即可
- 不要为非 Cognex In-Sight / Fluentd 格式的 JSON 运行此脚本（典型特征是没有 `structuredData.auditEvent@39975` 这样的 RFC 5424 SD-ID 键）
