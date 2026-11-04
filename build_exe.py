"""PyInstaller 打包：单文件 exe，命名 DataRouterPy_{yyyyMMddHHmmss}。

用法：uv run python build_exe.py
"""

from __future__ import annotations

import shutil
import subprocess
import sys
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent
ASSETS = ROOT / "assets"


def make_icon() -> Path | None:
    """由 router.png 生成 app_icon.ico（需 Pillow，失败则退回 png）。"""
    png = ASSETS / "router.png"
    try:
        from PIL import Image

        img = Image.open(png)
        icon = ASSETS / "app_icon.ico"
        img.save(icon, sizes=[(16, 16), (32, 32), (48, 48), (64, 64), (128, 128)])
        return icon
    except Exception as exc:  # noqa: BLE001
        print(f"图标生成失败（将不设置 exe 图标）: {exc}")
        return None


def main() -> None:
    stamp = datetime.now().strftime("%Y%m%d%H%M%S")
    name = f"DataRouterPy_{stamp}"
    cmd = [
        sys.executable, "-m", "PyInstaller",
        "--onefile", "--windowed", "--noconfirm", "--clean",
        "--name", name,
        "--add-data", f"{ASSETS}{';'}assets",
        str(ROOT / "src" / "datarouter" / "app.py"),
    ]
    icon = make_icon()
    if icon is not None:
        cmd += ["--icon", str(icon)]
    print(" ".join(cmd))
    subprocess.run(cmd, cwd=ROOT, check=True)
    exe = ROOT / "dist" / f"{name}.exe"
    print(f"打包完成: {exe}")


if __name__ == "__main__":
    main()
