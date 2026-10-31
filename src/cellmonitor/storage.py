"""SQLite + xlsx 双路落库。

SQLite 增量写入所有采样点（时间戳、相机、单元格、值、OK/NG）；
xlsx 按 相机/日期/单元格 导出工作表，文件名带时间戳后缀。
"""

from __future__ import annotations

import sqlite3
import threading
from datetime import datetime
from pathlib import Path

from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill

SCHEMA = """
CREATE TABLE IF NOT EXISTS samples (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    ts TEXT NOT NULL,               -- ISO8601 本地时间
    camera TEXT NOT NULL,           -- 相机名
    cell TEXT NOT NULL,             -- 单元格名/地址
    value REAL,                     -- 数值（无法解析为数值时为 NULL）
    text TEXT,                      -- 原始文本
    ok INTEGER                      -- 1=合格 0=超差 NULL=无阈值
);
CREATE INDEX IF NOT EXISTS idx_samples_cam_cell ON samples(camera, cell, ts);
"""


def timestamp_suffix(ts: datetime | None = None) -> str:
    return (ts or datetime.now()).strftime("_%Y%m%d_%H%M%S")


class Storage:
    """SQLite 写入器（线程安全，供 asyncio 线程调用）。"""

    def __init__(self, db_path: Path):
        db_path = Path(db_path)
        db_path.parent.mkdir(parents=True, exist_ok=True)
        self._lock = threading.Lock()
        self._conn = sqlite3.connect(str(db_path), check_same_thread=False)
        self._conn.executescript(SCHEMA)
        self._conn.commit()

    def insert_sample(
        self,
        ts: datetime,
        camera: str,
        cell: str,
        raw,
        ok: bool | None,
    ) -> None:
        value = None
        try:
            value = float(raw)
        except (TypeError, ValueError):
            pass
        with self._lock:
            self._conn.execute(
                "INSERT INTO samples (ts, camera, cell, value, text, ok) VALUES (?,?,?,?,?,?)",
                (ts.isoformat(timespec="milliseconds"), camera, cell, value, str(raw), ok),
            )
            self._conn.commit()

    def query(
        self,
        camera: str,
        cell: str,
        start: datetime | None = None,
        end: datetime | None = None,
    ) -> list[tuple[str, float | None, str, int | None]]:
        """返回 [(ts, value, text, ok)]，按时间升序。"""
        sql = "SELECT ts, value, text, ok FROM samples WHERE camera=? AND cell=?"
        args: list = [camera, cell]
        if start:
            sql += " AND ts>=?"
            args.append(start.isoformat(timespec="milliseconds"))
        if end:
            sql += " AND ts<=?"
            args.append(end.isoformat(timespec="milliseconds"))
        sql += " ORDER BY ts"
        with self._lock:
            return self._conn.execute(sql, args).fetchall()

    def cells(self, camera: str) -> list[str]:
        with self._lock:
            rows = self._conn.execute(
                "SELECT DISTINCT cell FROM samples WHERE camera=? ORDER BY cell", (camera,)
            ).fetchall()
        return [r[0] for r in rows]

    def cameras(self) -> list[str]:
        with self._lock:
            rows = self._conn.execute("SELECT DISTINCT camera FROM samples ORDER BY camera").fetchall()
        return [r[0] for r in rows]

    def close(self) -> None:
        with self._lock:
            self._conn.close()


def export_xlsx(
    storage: Storage,
    camera: str,
    out_dir: Path,
    start: datetime | None = None,
    end: datetime | None = None,
    notes: dict[str, str] | None = None,
) -> Path:
    """按 单元格 生成工作表导出（文件名带时间戳后缀）。"""
    wb = Workbook()
    wb.remove(wb.active)
    red = PatternFill("solid", start_color="FFC7CE")
    bold = Font(bold=True)
    notes = notes or {}
    for cell in storage.cells(camera):
        rows = storage.query(camera, cell, start, end)
        ws = wb.create_sheet(title=cell[:31])
        ws.append(["时间戳", "数值", "原始值", "判定"])
        for c in ws[1]:
            c.font = bold
        if notes.get(cell):
            ws["F1"] = "注解"
            ws["F1"].font = bold
            ws["F2"] = notes[cell]
        for ts, value, text, ok in rows:
            ws.append([ts, value, text, "OK" if ok else ("NG" if ok == 0 else "")])
            if ok == 0:
                for c in ws[ws.max_row]:
                    c.fill = red
        if ws.max_row > 1:
            ws.auto_filter.ref = f"A1:D{ws.max_row}"
    out_dir.mkdir(parents=True, exist_ok=True)
    path = out_dir / f"{camera}{timestamp_suffix()}.xlsx"
    wb.save(path)
    return path
