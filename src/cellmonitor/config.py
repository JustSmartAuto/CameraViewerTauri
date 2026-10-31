"""配置与数据目录管理。

默认数据目录：D:/JustSmart/CameraCellMonitor（D 盘不可用则回退 ~/AppData/CameraCellMonitor），
启动时自动递归创建目录（xlsx / charts / db 子目录）。
"""

from __future__ import annotations

import json
import os
from dataclasses import dataclass, field, asdict
from pathlib import Path

APP_NAME = "CameraCellMonitor"
SUBDIRS = ("xlsx", "charts", "db", "config")


def default_data_dir() -> Path:
    """默认数据目录：优先 D:/JustSmart，失败回退用户 AppData。"""
    candidates = [Path("D:/") / "JustSmart" / APP_NAME, Path.home() / "AppData" / APP_NAME]
    for base in candidates:
        try:
            ensure_subdirs(base)
            return base
        except OSError:
            continue
    raise RuntimeError("无法创建数据目录（D:/JustSmart 与 AppData 均不可用）")


def ensure_subdirs(root: Path) -> None:
    root.mkdir(parents=True, exist_ok=True)
    for sub in SUBDIRS:
        (root / sub).mkdir(parents=True, exist_ok=True)


@dataclass
class CellConfig:
    """单个订阅单元格。"""

    name: str  # 单元格名或地址，如 "A2" / "Measure.Width"
    sample_mode: str = "event"  # event=按结果事件采样；poll=按固定周期轮询
    poll_interval: float = 1.0  # 轮询周期（秒），仅 poll 模式
    lower: float | None = None  # 阈值下限（用于超差判定/统计），None 不判
    upper: float | None = None  # 阈值上限
    unit: str = ""  # 单位标注
    note: str = ""  # 注解：说明该单元格数值的业务含义（如 "焊缝宽度/mm"）


@dataclass
class CameraConfig:
    """单台相机配置。"""

    name: str
    host: str
    port: int = 80
    username: str = "admin"
    password: str = ""
    cells: list[CellConfig] = field(default_factory=list)
    reconnect_interval: float = 5.0
    enabled: bool = True

    @property
    def ws_url(self) -> str:
        return f"ws://{self.host}:{self.port}/ws"


def config_path(data_dir: Path) -> Path:
    return data_dir / "config" / "cameras.json"


def load_config(data_dir: Path) -> list[CameraConfig]:
    path = config_path(data_dir)
    if not path.exists():
        return []
    data = json.loads(path.read_text(encoding="utf-8"))
    cameras = []
    for item in data:
        item = dict(item)
        item["cells"] = [CellConfig(**c) for c in item.get("cells", [])]
        cameras.append(CameraConfig(**item))
    return cameras


def save_config(data_dir: Path, cameras: list[CameraConfig]) -> None:
    path = config_path(data_dir)
    path.parent.mkdir(parents=True, exist_ok=True)
    data = []
    for cam in cameras:
        item = asdict(cam)
        data.append(item)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
