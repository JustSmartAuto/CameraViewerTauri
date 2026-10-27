"""
convert_audit_log.py — Cognex In-Sight 审计日志 JSON → XLSX + Markdown

用法:
    python convert_audit_log.py <input.json> [--outdir DIR] [--prefix NAME] [--tz 8.0]

功能:
  读取 Fluentd / In-Sight 导出的审计日志 JSON 数组，
  输出 1) 人类可读的 XLSX（明细 + 统计两个 sheet）
       2) 简要统计 Markdown 报告

依赖: openpyxl, pandas
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from collections import Counter
from datetime import datetime, timezone, timedelta
from pathlib import Path

from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side
from openpyxl.utils import get_column_letter


# ============================================================
# 样式常量
# ============================================================
HEADER_FILL = PatternFill("solid", fgColor="1F4E78")
HEADER_FONT = Font(name="微软雅黑", bold=True, color="FFFFFF", size=11)
SECTION_FILL = PatternFill("solid", fgColor="D6E4F0")
SECTION_FONT = Font(name="微软雅黑", bold=True, color="1F4E78", size=11)
NORMAL_FONT = Font(name="微软雅黑", size=10)
ZEBRA_A = PatternFill("solid", fgColor="FFFFFF")
ZEBRA_B = PatternFill("solid", fgColor="F2F7FB")
THIN = Side(style="thin", color="D9DEE7")
THIN_BORDER = Border(left=THIN, right=THIN, top=THIN, bottom=THIN)
CENTER = Alignment(horizontal="center", vertical="center", wrap_text=True)
LEFT = Alignment(horizontal="left", vertical="center", wrap_text=True)


# ============================================================
# 基础工具
# ============================================================
def fmt_ts_utc_to_local(ts: str, offset_hours: float) -> datetime | None:
    """ISO8601 UTC 时间字符串 → 本地 naive datetime。"""
    if not ts:
        return None
    try:
        dt = datetime.fromisoformat(ts.replace("Z", "+00:00"))
    except ValueError:
        return None
    local = dt.astimezone(timezone(timedelta(hours=offset_hours)))
    return local.replace(tzinfo=None)


def find_sd_key(sd: dict, prefix: str) -> str | None:
    for k in sd:
        if k.startswith(prefix):
            return k
    return None


# ============================================================
# 摘要生成（每类事件一条规则）
# ============================================================
def summarize_event(rec: dict) -> str:
    tp = rec.get("type", "")
    ev = rec.get("event", "")
    result = rec.get("result", "") or ""
    sd = rec.get("structuredData", {}) or {}

    cc_key = find_sd_key(sd, "configChange")
    cc = sd.get(cc_key, {}) if cc_key else {}

    # network
    if tp == "network" and ev == "config-interface-parameter":
        new = cc.get("new") or {}
        ipv4 = new.get("ipv4") or {}
        if new.get("connected"):
            ip = (ipv4.get("ip_address_with_mask") or [""])[0]
            gw = ipv4.get("gateway") or ""
            method = ipv4.get("method") or ""
            parts = [f"网口启用: IP {ip}"]
            if method == "manual":
                parts.append("手动")
            if gw:
                parts.append(f"网关 {gw}")
            return ", ".join(parts)
        return "网口禁用"

    # auth
    if tp == "auth" and ev == "login-success":
        ak = find_sd_key(sd, "auth")
        user = sd[ak].get("user", "") if ak else ""
        return f"用户登录: {user}" if user else "用户登录"

    # job-change
    if tp == "job-change":
        if "Saved" in result:
            return f"作业保存: {result.replace(' Saved','').strip()}"
        if "Loaded" in result:
            return f"作业加载: {result.replace(' Loaded','').strip()}"
        return result or "作业变更"

    # utility / startup-settings
    if tp == "utility" and ev == "startup-settings":
        old_j = (cc.get("old") or {}).get("job", "")
        new_j = (cc.get("new") or {}).get("job", "")
        if old_j and new_j and old_j != new_j:
            return f"启动作业: {old_j} → {new_j}"
        return f"启动作业设置: {new_j}"

    # cell-change
    if tp == "cell-change":
        locs = []
        for item in (cc.get("new") or []):
            if isinstance(item, dict) and item.get("location"):
                locs.append(item["location"])
        if not locs:
            for item in (cc.get("old") or []):
                if isinstance(item, dict) and item.get("location"):
                    locs.append(item["location"])
        loc_str = ",".join(locs[:5])
        if len(locs) > 5:
            loc_str += f" 等{len(locs)}处"
        if ev == "cell-expression":
            return f"单元格公式修改: {loc_str}" if loc_str else "单元格公式修改"
        if ev == "cell-enabled-state":
            return f"单元格启用状态变更: {loc_str}" if loc_str else "单元格启用状态变更"
        return f"单元格变更: {loc_str}" if loc_str else "单元格变更"

    # tool-change
    if tp == "tool-change":
        tool = result or ev
        loc = ""
        if cc:
            target = cc.get("new") or cc.get("old") or {}
            if isinstance(target, dict):
                loc = target.get("location", "")
        label_map = {
            "EditRegion": "检测区域编辑",
            "EditFloat": "数值参数编辑",
            "EditCompositeRegion": "复合区域编辑",
            "EditString": "字符串参数编辑",
            "ListBox": "列表选择编辑",
            "CheckBox": "开关状态编辑",
            "TrainPatMaxRedLine": "PatMax 模板训练",
        }
        label = label_map.get(tool, tool)
        return f"{label}: {loc}" if loc else label

    # online-state-change
    if tp == "online-state-change":
        return "转上线" if ev == "online" else "转离线"

    # device-connect
    if tp == "device-connect":
        return "电子表格连接" if ev == "spreadsheet-connect" else "电子表格断开"

    # acquisition-settings
    if tp == "acquisition-settings":
        return f"2D 采集参数下发 ({result})" if result else "2D 采集参数下发"

    # time
    if tp == "time":
        if ev == "change-ntp-server":
            arr = cc.get("new") or [""]
            return f"NTP 服务器: {arr[0]}" if isinstance(arr, list) else f"NTP 服务器: {arr}"
        if ev == "change-timezone":
            return f"时区设置: {cc.get('new','')}"
        if ev == "change-ntp-status":
            return f"NTP 状态: {cc.get('new','')}"
        return f"时间配置变更: {ev}"

    # audit-log / startup
    if tp == "audit-log" and ev == "startup":
        return "设备启动（审计日志起点）"

    return f"{tp}/{ev}"


# ============================================================
# 字段提取
# ============================================================
def extract_user(sd: dict) -> str:
    ak = find_sd_key(sd, "auth")
    return sd[ak].get("user", "") if ak else ""


def extract_source(sd: dict) -> str:
    ak = find_sd_key(sd, "auditEvent")
    src = sd[ak].get("source", "") if ak else ""
    return {"humanUser": "人工操作", "device": "设备自动"}.get(src, src or "")


def extract_category(sd: dict) -> str:
    ak = find_sd_key(sd, "auditEvent")
    return sd[ak].get("category", "") if ak else ""


def extract_audit_id(sd: dict) -> int | None:
    ak = find_sd_key(sd, "auditEvent")
    return sd[ak].get("id") if ak else None


def extract_is_synced(sd: dict) -> str:
    v = (sd.get("timeQuality") or {}).get("isSynced", "0")
    return "已同步" if v == "1" else "未同步"


def extract_old_new(rec: dict) -> tuple:
    sd = rec.get("structuredData", {}) or {}
    cc_key = find_sd_key(sd, "configChange")
    cc = sd.get(cc_key, {}) if cc_key else {}
    old = cc.get("old")
    new = cc.get("new")
    def _fmt(v):
        if v is None:
            return None
        if isinstance(v, str):
            return v
        return json.dumps(v, ensure_ascii=False, separators=(",", ":"))
    return _fmt(old), _fmt(new)


# ============================================================
# JSON 解析
# ============================================================
def parse_json(path: Path, tz_offset: float) -> list[dict]:
    with open(path, encoding="utf-8") as f:
        raw = json.load(f)
    if not isinstance(raw, list):
        raise ValueError("JSON 顶层必须是数组")

    rows = []
    for rec in raw:
        sd = rec.get("structuredData", {}) or {}
        audit_id = extract_audit_id(sd)
        old_s, new_s = extract_old_new(rec)
        ts_local = fmt_ts_utc_to_local(rec.get("timestamp", ""), tz_offset)

        rows.append({
            "时间(北京)": ts_local,
            "审计ID": audit_id,
            "来源": extract_source(sd),
            "严重级": rec.get("severity", ""),
            "分类": extract_category(sd),
            "类型": rec.get("type", ""),
            "事件": rec.get("event", ""),
            "用户": extract_user(sd) or None,
            "结果": (rec.get("result", "") or "") or None,
            "变更内容摘要": summarize_event(rec),
            "旧值": old_s,
            "新值": new_s,
            "时间同步": extract_is_synced(sd),
            "_raw_ts": rec.get("timestamp", ""),
            "_hostname": rec.get("hostname", ""),
        })

    rows.sort(key=lambda r: (
        r["审计ID"] if r["审计ID"] is not None else 10**18,
        r["时间(北京)"] or datetime.min,
    ))
    return rows


# ============================================================
# XLSX 生成
# ============================================================
DETAIL_COLUMNS = [
    ("#", 6), ("审计ID", 8), ("时间(北京)", 22), ("来源", 10),
    ("严重级", 8), ("分类", 12), ("类型", 20), ("事件", 26),
    ("用户", 10), ("结果", 40), ("变更内容摘要", 48),
    ("旧值", 40), ("新值", 40), ("时间同步", 10),
]


def write_xlsx(rows: list[dict], out_path: Path) -> None:
    wb = Workbook()

    # Sheet 1: 明细
    ws = wb.active
    ws.title = "审计日志明细"
    ws.append([c[0] for c in DETAIL_COLUMNS])
    for ci, (_, width) in enumerate(DETAIL_COLUMNS, start=1):
        cell = ws.cell(row=1, column=ci)
        cell.fill = HEADER_FILL
        cell.font = HEADER_FONT
        cell.alignment = CENTER
        cell.border = THIN_BORDER
        ws.column_dimensions[get_column_letter(ci)].width = width

    for idx, r in enumerate(rows, start=1):
        row_vals = [
            idx, r["审计ID"], r["时间(北京)"], r["来源"],
            r["严重级"], r["分类"], r["类型"], r["事件"],
            r["用户"], r["结果"], r["变更内容摘要"],
            r["旧值"], r["新值"], r["时间同步"],
        ]
        ws.append(row_vals)
        fill = ZEBRA_A if idx % 2 == 1 else ZEBRA_B
        for ci in range(1, len(DETAIL_COLUMNS) + 1):
            c = ws.cell(row=idx + 1, column=ci)
            c.font = NORMAL_FONT
            c.fill = fill
            c.border = THIN_BORDER
            c.alignment = LEFT if ci >= 9 else CENTER
        ws.cell(row=idx + 1, column=3).number_format = "YYYY-MM-DD HH:MM:SS"

    ws.freeze_panes = "A2"
    ws.auto_filter.ref = f"A1:{get_column_letter(len(DETAIL_COLUMNS))}{len(rows)+1}"

    # Sheet 2: 统计概览
    ws2 = wb.create_sheet("统计概览")
    _write_summary_sheet(ws2, rows)

    wb.save(out_path)


def _write_summary_sheet(ws, rows):
    r = 1

    def section(title: str):
        nonlocal r
        ws.cell(r, 1, title)
        ws.merge_cells(start_row=r, start_column=1, end_row=r, end_column=2)
        c = ws.cell(r, 1)
        c.fill = SECTION_FILL
        c.font = SECTION_FONT
        r += 1
        ws.cell(r, 1, "项目").font = HEADER_FONT
        ws.cell(r, 2, "值").font = HEADER_FONT
        for cc in (1, 2):
            cell = ws.cell(r, cc)
            cell.fill = HEADER_FILL
            cell.font = HEADER_FONT
            cell.alignment = CENTER
            cell.border = THIN_BORDER
        r += 1

    def kv(k, v):
        nonlocal r
        ws.cell(r, 1, k); ws.cell(r, 2, v)
        for cc in (1, 2):
            ws.cell(r, cc).font = NORMAL_FONT
            ws.cell(r, cc).border = THIN_BORDER
        r += 1

    # 总览
    section("总览")
    audit_ids = [x for x in (row["审计ID"] for row in rows) if x is not None]
    audit_range = ""
    if audit_ids:
        s = sorted(audit_ids)
        missing = set(range(s[0], s[-1] + 1)) - set(s)
        audit_range = f"{s[0]} ~ {s[-1]}"
        audit_range += "（连续无缺失）" if not missing else f"（缺失 {len(missing)} 条）"
    kv("事件总数", len(rows))
    kv("审计ID范围", audit_range)
    ts_list = [row["_raw_ts"] for row in rows if row["_raw_ts"]]
    if ts_list:
        kv("记录跨度(UTC)", f"{ts_list[0]} ~ {ts_list[-1]}")
    src_str = " / ".join(f"{k} {n}" for k, n in Counter(row["来源"] for row in rows).items() if k)
    kv("来源", src_str)
    if rows:
        kv("主机名", rows[0]["_hostname"])

    r += 1
    section("按月分布（北京时间）")
    months = Counter()
    for row in rows:
        dt = row["时间(北京)"]
        if dt:
            months[dt.strftime("%Y-%m")] += 1
    for m, n in sorted(months.items()):
        kv(m, n)

    r += 1
    section("按类型分布")
    for k, n in Counter(row["类型"] for row in rows).most_common():
        kv(k or "(空)", n)

    r += 1
    section("按事件分布")
    for k, n in Counter(row["事件"] for row in rows).most_common():
        kv(k or "(空)", n)

    saves = [row["结果"] for row in rows if row["类型"] == "job-change" and row["事件"] == "save-job" and row["结果"]]
    if saves:
        r += 1
        section("作业保存台账（save-job）")
        for k, n in Counter(saves).most_common():
            kv(k, n)

    ws.column_dimensions["A"].width = 32
    ws.column_dimensions["B"].width = 50


# ============================================================
# Markdown 生成
# ============================================================
def write_markdown(rows: list[dict], out_path: Path) -> None:
    L = []
    hostname = rows[0]["_hostname"] if rows else ""
    total = len(rows)
    audit_ids = sorted([r["审计ID"] for r in rows if r["审计ID"] is not None])

    L.append(f"# {hostname} 审计日志分析报告")
    L.append("")
    L.append(f"> 数据源：`{out_path.parent}` / 共 **{total}** 条事件")
    if rows:
        ts_start = rows[0]["时间(北京)"]
        ts_end = rows[-1]["时间(北京)"]
        if ts_start and ts_end:
            L.append(f"> 时间跨度（北京时间）：**{ts_start:%Y-%m-%d %H:%M} ~ {ts_end:%Y-%m-%d %H:%M}**")
        L.append(f"> UTC：`{rows[0]['_raw_ts']} ~ {rows[-1]['_raw_ts']}`")
    L.append("")
    L.append("---")
    L.append("")

    # 1. 概要
    L.append("## 1. 概要")
    L.append("")
    L.append(f"- **设备**：{hostname}")
    if audit_ids:
        missing = set(range(audit_ids[0], audit_ids[-1] + 1)) - set(audit_ids)
        tag = "连续无缺失" if not missing else f"缺失 {len(missing)} 条"
        L.append(f"- **审计 ID**：{audit_ids[0]} ~ {audit_ids[-1]}（{tag}）")
    src_c = Counter(r["来源"] for r in rows)
    L.append(f"- **来源**：{', '.join(f'{k} {v}' for k, v in src_c.items() if k)}")
    sev_c = Counter(r["严重级"] for r in rows)
    L.append(f"- **严重级**：{', '.join(f'{k} {v}' for k, v in sev_c.items() if k)}")
    L.append("")

    # 2. 按类型
    L.append("## 2. 按事件大类 (type)")
    L.append("")
    L.append("| type | 条数 |")
    L.append("|---|---:|")
    for k, n in Counter(r["类型"] for r in rows).most_common():
        L.append(f"| `{k}` | {n} |")
    L.append("")

    # 3. 按事件
    L.append("## 3. 按事件名 (event)")
    L.append("")
    L.append("| event | 条数 |")
    L.append("|---|---:|")
    for k, n in Counter(r["事件"] for r in rows).most_common():
        L.append(f"| `{k}` | {n} |")
    L.append("")

    # 4. 按月
    L.append("## 4. 按月分布（北京时间）")
    L.append("")
    months = Counter()
    for r in rows:
        dt = r["时间(北京)"]
        if dt:
            months[dt.strftime("%Y-%m")] += 1
    L.append("| 月份 | 条数 | 占比 |")
    L.append("|---|---:|---:|")
    for m, n in sorted(months.items()):
        L.append(f"| {m} | {n} | {n/total*100:.1f}% |")
    L.append("")

    # 5. 活跃日
    L.append("## 5. 活跃日明细（北京时间）")
    L.append("")
    L.append("| 日期 | 条数 | 首次 | 末次 |")
    L.append("|---|---:|---|---|")
    by_day: dict[str, list] = {}
    for r in rows:
        dt = r["时间(北京)"]
        if dt:
            by_day.setdefault(dt.strftime("%Y-%m-%d"), []).append(dt)
    for day in sorted(by_day):
        ts = by_day[day]
        L.append(f"| {day} | {len(ts)} | {min(ts):%H:%M:%S} | {max(ts):%H:%M:%S} |")
    L.append("")

    # 6. 登录
    logins = [r for r in rows if r["类型"] == "auth"]
    if logins:
        L.append("## 6. 登录概览")
        L.append("")
        L.append(f"- 登录成功 **{len(logins)} 次**")
        for u, n in Counter(r["用户"] for r in logins if r["用户"]).most_common():
            L.append(f"- 账户 **{u}**：{n} 次")
        L.append("")

    # 7. 作业台账
    saves = [r for r in rows if r["类型"] == "job-change" and r["事件"] == "save-job" and r["结果"]]
    loads = [r for r in rows if r["类型"] == "job-change" and r["事件"] == "load-job" and r["结果"]]
    if saves or loads:
        L.append("## 7. 作业文件台账")
        L.append("")
        L.append("| 作业 | 保存 | 加载 |")
        L.append("|---|---:|---:|")
        sc = Counter(r["结果"].replace(" Saved","").strip() for r in saves)
        lc = Counter(r["结果"].replace(" Loaded","").strip() for r in loads)
        for job in sorted(set(sc) | set(lc)):
            L.append(f"| `{job}` | {sc.get(job,0)} | {lc.get(job,0)} |")
        L.append("")

    # 8. 网络
    nets = [r for r in rows if r["类型"] == "network"]
    if nets:
        en = sum(1 for r in nets if "网口启用" in r["变更内容摘要"])
        dis = sum(1 for r in nets if "网口禁用" in r["变更内容摘要"])
        L.append("## 8. 网络配置变更")
        L.append("")
        L.append(f"- 网口启用 **{en}** 次 / 禁用 **{dis}** 次")
        ips = set()
        for r in nets:
            nw = r["新值"] or ""
            m = re.search(r'"ip_address_with_mask":\["([^"]+)"\]', nw)
            if m:
                ips.add(m.group(1))
        for ip in sorted(ips):
            L.append(f"- 典型配置：`{ip}`")
        L.append("")

    # 9. 最近 50 条
    L.append("## 9. 最近 50 条变更")
    L.append("")
    L.append("| # | 北京时间 | 类型 | 事件 | 来源 | 变更摘要 |")
    L.append("|---:|---|---|---|---|---|")
    for idx, r in enumerate(rows[-50:], start=1):
        dt = r["时间(北京)"]
        ts_str = dt.strftime("%Y-%m-%d %H:%M:%S") if dt else ""
        summary = r["变更内容摘要"].replace("|", "\\|")
        L.append(f"| {idx} | {ts_str} | `{r['类型']}` | `{r['事件']}` | {r['来源']} | {summary} |")
    L.append("")

    out_path.write_text("\n".join(L), encoding="utf-8")


# ============================================================
# 入口
# ============================================================
def main():
    ap = argparse.ArgumentParser(description="Cognex In-Sight 审计日志 JSON → XLSX + Markdown")
    ap.add_argument("json", help="审计日志 JSON 文件路径")
    ap.add_argument("--outdir", default=None, help="输出目录（默认与输入同目录）")
    ap.add_argument("--prefix", default=None, help="输出文件名前缀（默认用输入文件名）")
    ap.add_argument("--tz", type=float, default=8.0, help="本地时区偏移小时数（默认 8.0 = 北京时间）")
    args = ap.parse_args()

    in_path = Path(args.json).resolve()
    if not in_path.exists():
        print(f"[ERR] 文件不存在: {in_path}", file=sys.stderr)
        sys.exit(1)

    outdir = Path(args.outdir).resolve() if args.outdir else in_path.parent
    outdir.mkdir(parents=True, exist_ok=True)
    prefix = args.prefix or in_path.stem

    print(f"[INFO] 解析 {in_path.name} ...")
    rows = parse_json(in_path, args.tz)
    print(f"[INFO] 共 {len(rows)} 条事件")

    xlsx_path = outdir / f"{prefix}_审计日志明细.xlsx"
    md_path = outdir / f"{prefix}.md"

    print(f"[INFO] 生成 {xlsx_path.name} ...")
    write_xlsx(rows, xlsx_path)

    print(f"[INFO] 生成 {md_path.name} ...")
    write_markdown(rows, md_path)

    print(f"[DONE] 输出目录: {outdir}")
    print(f"        XLSX: {xlsx_path.name}")
    print(f"        MD:   {md_path.name}")


if __name__ == "__main__":
    main()
