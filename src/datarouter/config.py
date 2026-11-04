"""转存配置的读写。

配置文件位于应用数据目录下的 TransformConfig.json，
为一个「转存配置列表」（支持按名称多套并存），结构对标
CameraViewerDotnet 的 ConfigService.TransformConfig：

    each: {
        "name": "相机1",                    # 配置名（界面「名称」）
        "enabled": true,                    # 启用转存
        "watch_path": "D:/存图中转",         # 监控（来源）目录
        "source_splitter": ",",             # 内容分隔符：拆分文件主名
        "folder_idx": 1,                    # 文件夹内容序号：内容第几段作为文件夹内容
        "filename_idx": 2,                  # 文件名内容序号：内容第几段作为文件名内容
        "folder_splitter": "+",             # 路径分隔符：拆分「文件夹内容」
        "folder_parts": [                   # 新文件夹组成（按顺序拼接）
            {"source_index": 0, "type": "name", "fmt": ""},   # 类型 name=取来源段
            {"source_index": 7, "type": "datetime", "fmt": "%Y%m%d"}  # datetime=按格式取当前时间
        ],
        "filename_splitter": "_",           # 文件名分隔符：拆分「文件名内容」
        "filename_parts": [...]             # 新文件名组成（同 folder_parts，去掉扩展名）
    }
"""

from __future__ import annotations

import json
import sys
from dataclasses import dataclass, field
from pathlib import Path

CONFIG_FILE = "TransformConfig.json"


@dataclass
class PartItem:
    """新路径/新文件名的一个组成段。"""

    source_index: int = 0   # 原路径序号：拆分后取第几段（类型为 name 时生效）
    type: str = "name"      # name=取来源段 / datetime=按格式取当前时间
    fmt: str = ""           # datetime 格式化（strftime 或 .NET 风格，均可）


# —— 序列化辅助 ——————————————————————————————————————————

def part_to_dict(part: PartItem | dict) -> dict:
    if isinstance(part, dict):
        part = part_from_dict(part)
    return {"SourceIndex": part.source_index, "PartType": part.type, "Format": part.fmt}


def part_from_dict(node: dict) -> PartItem:
    ptype = str(node.get("PartType") or node.get("type") or "name").lower()
    return PartItem(
        source_index=int(node.get("SourceIndex", node.get("source_index", 0)) or 0),
        type=ptype,
        fmt=str(node.get("Format", node.get("fmt", "")) or ""),
    )


@dataclass
class TransformConfig:
    """一套文件转存配置。"""

    name: str = "相机1"
    enabled: bool = True
    watch_path: str = ""
    source_splitter: str = ","
    folder_idx: int = 0
    filename_idx: int = 1
    folder_splitter: str = "+"
    folder_parts: list[PartItem] = field(default_factory=list)
    filename_splitter: str = "_"
    filename_parts: list[PartItem] = field(default_factory=list)

    def to_dict(self) -> dict:
        return {
            "Name": self.name,
            "Enabled": self.enabled,
            "WatchPath": self.watch_path,
            "SourceSpliter": self.source_splitter,
            "FolderIndex": self.folder_idx,
            "FileNameIndex": self.filename_idx,
            "FolderSpliter": self.folder_splitter,
            "FolderPartList": [part_to_dict(p) for p in self.folder_parts],
            "FileNameSpliter": self.filename_splitter,
            "FileNamePartList": [part_to_dict(p) for p in self.filename_parts],
        }

    @staticmethod
    def from_dict(node: dict) -> "TransformConfig":
        part_list = node.get("FolderPartList") or node.get("folder_parts") or []
        file_list = node.get("FileNamePartList") or node.get("filename_parts") or []
        return TransformConfig(
            name=str(node.get("Name", node.get("name", "")) or ""),
            enabled=bool(node.get("Enabled", node.get("enabled", True))),
            watch_path=str(node.get("WatchPath", node.get("watch_path", "")) or ""),
            source_splitter=str(node.get("SourceSpliter", node.get("source_splitter", ",")) or "," or ","),
            folder_idx=int(node.get("FolderIndex", node.get("folder_idx", 0)) or 0),
            filename_idx=int(node.get("FileNameIndex", node.get("filename_idx", 1)) or 1),
            folder_splitter=str(node.get("FolderSpliter", node.get("folder_splitter", "+")) or "+" or "+"),
            folder_parts=[part_from_dict(p) for p in part_list],
            filename_splitter=str(node.get("FileNameSpliter", node.get("filename_splitter", "_")) or "_" or "_"),
            filename_parts=[part_from_dict(p) for p in file_list],
        )


# —— 数据目录 ————————————————————————————————————————————

def default_data_dir() -> Path:
    """默认应用数据目录：D:/JustSmart/DataRouterPy，D 盘不可用则回退 ~/AppData/DataRouterPy。"""
    candidate = Path("D:/JustSmart/DataRouterPy")
    if candidate.drive.casefold() == "d:":
        try:
            candidate.mkdir(parents=True, exist_ok=True)
            # 试探写入权限
            probe = candidate / ".probe"
            probe.write_text("ok", encoding="utf-8")
            probe.unlink(missing_ok=True)
            return candidate
        except OSError:
            pass
    if sys.platform == "win32":
        fallback = Path.home() / "AppData" / "DataRouterPy"
    else:
        fallback = Path.home() / ".datarouterpy"
    fallback.mkdir(parents=True, exist_ok=True)
    return fallback


def config_path(data_dir: Path) -> Path:
    return data_dir / CONFIG_FILE


def load_config(data_dir: Path) -> list[TransformConfig]:
    """读取配置列表；兼容旧版（单个对象）；不存在/解析失败时返回默认集合。"""
    path = config_path(data_dir)
    data: dict | list | None = None
    if path.exists():
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            data = None
    result: list[TransformConfig]
    if isinstance(data, list):
        result = [TransformConfig.from_dict(n) for n in data if isinstance(n, dict)]
    elif isinstance(data, dict):
        nodes = data.get("Transforms") if isinstance(data.get("Transforms"), list) else [data]
        result = [TransformConfig.from_dict(n) for n in nodes if isinstance(n, dict)]
    else:
        result = []
    if not result:
        result = [TransformConfig(name="相机1")]
    return result


def save_config(data_dir: Path, configs: list[TransformConfig]) -> None:
    path = config_path(data_dir)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps([c.to_dict() for c in configs], ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
