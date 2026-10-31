"""相机采集 Worker：每台相机一个 asyncio 任务，掉线自动重连，互不影响。

事件采样：订阅 resultChanged，从 HmiResult.cells 提取配置单元格并落库，随后 ready。
轮询采样：按固定周期 queryCellResults。
"""

from __future__ import annotations

import asyncio
import logging
import queue
from datetime import datetime

from .cogsocket import CogSocketClient, CogSocketError, parse_cells
from .config import CameraConfig, CellConfig
from .storage import Storage

log = logging.getLogger(__name__)


def judge(value, cell: CellConfig) -> bool | None:
    """阈值判定：返回 True=OK False=NG None=无阈值或无法判定。"""
    if cell.lower is None and cell.upper is None:
        return None
    try:
        v = float(value)
    except (TypeError, ValueError):
        return None
    if cell.lower is not None and v < cell.lower:
        return False
    if cell.upper is not None and v > cell.upper:
        return False
    return True


class CameraWorker:
    """驱动单台相机的连接-订阅-落库循环。"""

    def __init__(self, cfg: CameraConfig, storage: Storage, events: queue.Queue):
        self.cfg = cfg
        self.storage = storage
        self.events = events  # UI 侧队列，消息为 (camera, kind, payload)
        self.client = CogSocketClient(cfg.host, cfg.port, cfg.username, cfg.password)
        self._task: asyncio.Task | None = None
        self._poll_tasks: list[asyncio.Task] = []
        self._running = False

    @property
    def running(self) -> bool:
        return self._running

    async def start(self) -> None:
        self._running = True
        self._task = asyncio.create_task(self._run())

    async def stop(self) -> None:
        self._running = False
        for task in self._poll_tasks:
            task.cancel()
        self._poll_tasks.clear()
        if self._task:
            self._task.cancel()
        await self.client.close()

    def _push(self, kind: str, payload=None) -> None:
        try:
            self.events.put_nowait((self.cfg.name, kind, payload))
        except queue.Full:
            pass

    async def _run(self) -> None:
        while self._running:
            try:
                await self.client.connect()
                self._push("connected")
                await self._open_and_collect()
            except asyncio.CancelledError:
                raise
            except Exception as exc:
                log.warning("camera %s error: %s", self.cfg.name, exc)
                self._push("error", str(exc))
            await self.client.close()
            if self._running:
                self._push("disconnected")
                await asyncio.sleep(self.cfg.reconnect_interval)

    async def _open_and_collect(self) -> None:
        cell_names = [c.name for c in self.cfg.cells]
        if not cell_names:
            return
        sid = await self.client.open_session(cell_names)
        self._push("session", sid)

        event_cells = [c for c in self.cfg.cells if c.sample_mode == "event"]
        poll_cells = [c for c in self.cfg.cells if c.sample_mode == "poll"]

        if event_cells:
            done = asyncio.Event()

            def on_result(hmi_result):
                try:
                    values = parse_cells(hmi_result or {})
                    ts = datetime.now()
                    for cell in event_cells:
                        if cell.name in values:
                            self._store(ts, cell, values[cell.name])
                    self._push("result", values)
                finally:
                    # 必须 ready，相机才会推送下一帧
                    asyncio.ensure_future(self._safe_ready())
                    done.set()

            await self.client.listen(f"{sid}/resultChanged", on_result)
            await self.client.ready()

        for cell in poll_cells:
            self._poll_tasks.append(asyncio.create_task(self._poll_loop(sid, cell)))

        # 保持任务存活，由外层重连循环管理生命周期
        await asyncio.Future()

    async def _safe_ready(self) -> None:
        try:
            await self.client.ready()
        except Exception as exc:
            log.debug("ready failed: %s", exc)

    async def _poll_loop(self, sid: str, cell: CellConfig) -> None:
        try:
            while self._running:
                results = await self.client.post(f"{sid}/queryCellResults", [[cell.name]])
                values = {}
                for item in results or []:
                    loc = item.get("location") or item.get("name")
                    if loc:
                        values[loc] = item.get("data")
                    name = item.get("name")
                    if name:
                        values[name] = item.get("data")
                if cell.name in values:
                    self._store(datetime.now(), cell, values[cell.name])
                    self._push("result", {cell.name: values[cell.name]})
                await asyncio.sleep(cell.poll_interval)
        except asyncio.CancelledError:
            pass
        except Exception as exc:
            log.warning("poll %s failed: %s", cell.name, exc)

    def _store(self, ts: datetime, cell: CellConfig, value) -> None:
        ok = judge(value, cell)
        self.storage.insert_sample(ts, self.cfg.name, cell.name, value, ok)


class Monitor:
    """管理所有相机 Worker 与事件总线。"""

    def __init__(self, storage: Storage):
        self.storage = storage
        self.events: queue.Queue = queue.Queue(maxsize=1000)
        self.workers: dict[str, CameraWorker] = {}

    async def start_camera(self, cfg: CameraConfig) -> None:
        worker = self.workers.get(cfg.name)
        if worker and worker.running:
            return
        worker = CameraWorker(cfg, self.storage, self.events)
        self.workers[cfg.name] = worker
        await worker.start()

    async def stop_camera(self, name: str) -> None:
        worker = self.workers.pop(name, None)
        if worker:
            await worker.stop()

    async def stop_all(self) -> None:
        for name in list(self.workers):
            await self.stop_camera(name)
