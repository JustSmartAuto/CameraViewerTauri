"""文件中转执行器。

对应 CameraViewerDotnet/CameraHelperLens 的 FileTransformer + TransformAction：
每套启用的转存配置一个 Router 线程，扫描监控目录，等待文件写入稳定后，
按配置把文件移动到目标文件夹并重命名：

    文件主名 = 内容分隔符 拆出 内容段[...]
    文件夹内容 = 内容段[文件夹内容序号] → 路径分隔符 拆出 文件夹段[...]
    文件名内容 = 内容段[文件名内容序号] → 文件名分隔符 拆出 文件名段[...]
    新文件夹 = watch_path \\ Σ(folder_parts)（name=文件夹段[i]；datetime=当前时间 strftime(fmt)）
    新文件名 = Σ(filename_parts)（同上） + 扩展名
"""

from __future__ import annotations

import logging
import re
import threading
import time
from datetime import datetime
from pathlib import Path

from .config import PartItem, TransformConfig

LOG = logging.getLogger("datarouter.router")

SCAN_INTERVAL = 0.5     # 空闲扫描周期（秒）
STABILITY_WAIT = 0.2    # 稳定判定单次等待（秒）
MAX_RETRY_SLEEP = 1.0   # 重试后再试（秒，正在写入的文件会在下轮再试）

_NET_TOKEN_RE = re.compile(
    "yyyy|MMMM|dddd|ffff|fff|MMM|ddd|ff|HH|hh|mm|ss|MM|dd|yy|tt"
)

_NET_TOKEN_MAP = {
    "yyyy": "%Y", "yy": "%y", "MMMM": "%B", "MMM": "%b",
    "dddd": "%A", "ddd": "%a", "MM": "%m", "dd": "%d",
    "HH": "%H", "hh": "%I", "mm": "%M", "ss": "%S",
    "ff": "%f", "fff": "%f", "ffff": "%f", "ffffff": "%f",
    "tt": "%p",
}


def net_to_strftime(fmt: str) -> str:
    """把 .NET 风格日期格式（yyyy-MM-dd 等）一次性转换为 strftime；
    含 % 的（已是 strftime）原样返回。"""
    if not fmt or "%" in fmt:
        return fmt
    return _NET_TOKEN_RE.sub(lambda m: _NET_TOKEN_MAP.get(m.group(0), m.group(0)), fmt)


def _strftime(part: PartItem, now: datetime) -> str:
    try:
        return now.strftime(net_to_strftime(part.fmt))
    except ValueError:
        LOG.warning("日期格式无效: %r", part.fmt)
        return ""


def build_folder_name(fmt_parts: list[PartItem], folder_parts: list[str], sep: str,
                      now: datetime) -> str:
    """按配置生成新文件夹名（不含监控根目录）。"""
    pieces: list[str] = []
    for part in fmt_parts:
        if part.type == "name":
            if 0 <= part.source_index < len(folder_parts):
                piece = folder_parts[part.source_index]
            else:
                piece = ""
        elif part.type == "datetime":
            piece = _strftime(part, now)
        else:
            piece = ""
        piece = piece.strip(" \\/")
        if piece:
            pieces.append(piece)
    return sep.join(pieces)


def build_file_stem(fmt_parts: list[PartItem], file_parts: list[str], sep: str,
                    now: datetime) -> str:
    """按配置生成新文件名（不含扩展名）。"""
    pieces: list[str] = []
    for part in fmt_parts:
        if part.type == "name":
            if 0 <= part.source_index < len(file_parts):
                piece = file_parts[part.source_index].strip()
                if piece:
                    pieces.append(piece)
        elif part.type == "datetime":
            piece = _strftime(part, now)
            if piece:
                pieces.append(piece)
    return sep.join(pieces)


def transform_name(config: TransformConfig, main_name: str | None = None,
                   now: datetime | None = None) -> tuple[str, str] | None:
    """按配置把文件主名转换为目标 (文件夹, 文件名无扩展名)；无法处理返回 None。

    其中文件夹为监控目录下的相对路径（用 \\ 分隔多级）。
    """
    src_splitter = config.source_splitter or ","
    folder_splitter = config.folder_splitter or "+"
    file_splitter = config.filename_splitter or "_"

    src_parts = main_name.split(src_splitter)
    top = max(config.folder_idx, config.filename_idx)
    if len(src_parts) <= top:
        return None
    folder_children = src_parts[config.folder_idx].split(folder_splitter)
    file_children = src_parts[config.filename_idx].split(file_splitter)

    now = now or datetime.now()
    folder = build_folder_name(config.folder_parts, folder_children, "\\", now)
    stem = build_file_stem(config.filename_parts, file_children, file_splitter, now)
    if not folder or not stem:
        return None
    return folder, stem


class Router:
    """一套转存配置的监控线程。"""

    def __init__(self, config: TransformConfig, on_log) -> None:
        self.config = config
        self.on_log = on_log
        self._stop = threading.Event()
        self._thread: threading.Thread | None = None

    # —— 生命周期 ——————————————————————————————————————————

    def start(self) -> None:
        if self._thread and self._thread.is_alive():
            return
        self._stop.clear()
        self._thread = threading.Thread(
            target=self._run, name=f"router-{self.config.name}", daemon=True
        )
        self._thread.start()

    def stop(self) -> None:
        self._stop.set()
        if self._thread:
            self._thread.join(timeout=2)
        self._thread = None

    @property
    def running(self) -> bool:
        return bool(self._thread and self._thread.is_alive())

    # —— 主循环 —————————————————————————————————————————————

    def _run(self) -> None:
        log = self.on_log
        cfg = self.config
        log(f"[{cfg.name}] 启动转存（来源: {cfg.watch_path}）") if log else None
        while not self._stop.is_set():
            try:
                self._scan_once()
            except Exception as exc:  # 单轮失败不影响后续
                LOG.debug("scan error", exc_info=True)
                if log:
                    log(f"[{cfg.name}] 扫描异常: {exc}")
            self._stop.wait(SCAN_INTERVAL)
        if log:
            log(f"[{cfg.name}] 已停止")

    # —— 扫描 + 稳定等待 + 转存 —————————————————————————————

    def _scan_once(self) -> None:
        cfg = self.config
        if not cfg.enabled or not cfg.watch_path:
            return
        watch = Path(cfg.watch_path)
        if not watch.is_dir():
            return
        files = [p for p in watch.iterdir() if p.is_file()
                 and not p.name.startswith(".")]
        if not files:
            return

        # 等待全部文件写入稳定（总大小不再增长才转存）
        sizes = [p.stat().st_size for p in files]
        while not self._stop.is_set():
            time.sleep(STABILITY_WAIT)
            if not watch.is_dir():
                return
            new_files = [p for p in watch.iterdir() if p.is_file()
                         and not p.name.startswith(".")]
            new_sizes = [p.stat().st_size for p in new_files]
            if new_files == files and new_sizes == sizes:
                break
            files, sizes = new_files, new_sizes

        moved = False
        for f in files:
            if self._transform_file(f):
                moved = True
        # 仍有积压时缩小监测间隔？保持固定周期即可，依赖下轮再扫
        _ = moved

    def _transform_file(self, src: Path) -> bool:
        """把单个文件转存到目标位置；成功 True，失败 False（下轮重试）。"""
        cfg = self.config
        log = self.on_log
        main_name = Path(src.name)
        stem, ext = main_name.stem, main_name.suffix
        try:
            result = transform_name(cfg, stem)
        except Exception as exc:
            if log:
                log(f"[{cfg.name}] 解析失败 {main_name.name}: {exc}")
            return False
        if result is None:
            # 段序号超出内容段数量：跳过（只调试记录，避免刷屏）
            LOG.debug("跳过大文件无法解析: %s", src.name)
            if log:
                log(f"[{cfg.name}] 无法解析（序号超界）: {main_name.name}")
            return False
        rel_folder, new_stem = result
        watch = Path(cfg.watch_path)
        target_dir = watch / rel_folder
        target = target_dir / f"{new_stem}{ext}"
        try:
            target_dir.mkdir(parents=True, exist_ok=True)
            if target.exists():
                target.unlink()
            src.replace(target)
        except OSError as exc:
            # 文件可能仍被占用，等待下轮重试
            LOG.debug("move error: %s", exc)
            return False
        if log:
            log(f"[{cfg.name}] {src.name}  →  {target}")
        return True
