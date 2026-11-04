"""应用入口。

用法：
    data-router [--data-dir PATH] [-v]

数据目录默认 D:/JustSmart/DataRouterPy（D 盘不可用则回退 ~/AppData/DataRouterPy）。
"""

from __future__ import annotations

import argparse
import logging
from pathlib import Path

from datarouter.config import default_data_dir, load_config


def parse_args(argv=None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(prog="data-router", description=__doc__)
    parser.add_argument("--data-dir", type=Path, default=None,
                        help="数据目录（默认 D:/JustSmart/DataRouterPy）")
    parser.add_argument("-v", "--verbose", action="store_true", help="调试日志")
    return parser.parse_args(argv)


def main(argv=None) -> None:
    args = parse_args(argv)
    logging.basicConfig(
        level=logging.DEBUG if args.verbose else logging.INFO,
        format="%(asctime)s %(levelname)s %(name)s: %(message)s",
    )

    data_dir = args.data_dir or default_data_dir()
    configs = load_config(data_dir)
    print(f"数据目录: {data_dir}")

    from datarouter.ui import RouterApp

    app = RouterApp(data_dir, configs)
    app.run()


if __name__ == "__main__":
    main()
