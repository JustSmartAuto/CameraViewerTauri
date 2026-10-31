"""模拟 In-Sight 相机 WebSocket 服务器（测试用）。

实现 CogSocket 最小子集：@/hello、openSession、login、ready、keepAlive、
queryCellResults、resultChanged 周期推送、dispose。
"""

from __future__ import annotations

import asyncio
import json
import logging
import math
import random

import websockets

log = logging.getLogger(__name__)


def _cell(location: str, name: str, value) -> dict:
    return {"$type": "HmiFloatResult", "location": location, "name": name, "data": value}


def _resp(msg_id, body=None, error=None) -> str:
    msg = {"$type": "resp", "id": msg_id}
    if error is not None:
        msg["error"] = error
    if body is not None:
        msg["body"] = body
    return json.dumps(msg)


class SimulatedCamera:
    def __init__(self, host: str = "127.0.0.1", port: int = 0, interval: float = 0.5):
        self.host = host
        self.port = port
        self.interval = interval
        self._server = None
        self._seq = 0

    async def __aenter__(self):
        await self.start()
        return self

    async def __aexit__(self, *exc):
        await self.stop()

    async def start(self) -> None:
        self._server = await websockets.serve(self._handle, self.host, self.port)
        self.port = self._server.sockets[0].getsockname()[1]

    async def stop(self) -> None:
        if self._server:
            self._server.close()
            await self._server.wait_closed()

    def _result(self) -> dict:
        self._seq += 1
        t = self._seq * self.interval
        width = 10.0 + 0.5 * math.sin(t / 3.0) + random.gauss(0, 0.08)
        score = max(0.0, min(100.0, 95 + random.gauss(0, 3)))
        return {
            "$type": "HmiResult",
            "id": self._seq,
            "jobStatus": 1,
            "cells": [
                _cell("A1", "Measure.Width", round(width, 4)),
                _cell("A2", "Measure.Score", round(score, 2)),
            ],
        }

    async def _handle(self, ws) -> None:
        session_id = None
        pushing = None
        try:
            async for raw in ws:
                try:
                    msg = json.loads(raw)
                except json.JSONDecodeError:
                    continue
                mtype = msg.get("$type")
                path = msg.get("path", "")
                msg_id = msg.get("id", 0)
                body = msg.get("body")

                if path == "@/hello":
                    await ws.send(_resp(msg_id))
                elif path.endswith("hmi/info") or path.endswith("system/info"):
                    await ws.send(_resp(msg_id, body={
                        "model": "SIM-7800", "ipAddress": self.host,
                        "firmwareVersion": "26.1.0", "hmiProtocolVersion": 3.0,
                    }))
                elif path.endswith("hmi/openSession"):
                    session_id = f"hs/~{random.randint(10**9, 10**10)}"
                    await ws.send(_resp(msg_id, body=session_id))
                elif session_id and path == f"{session_id}/login":
                    await ws.send(_resp(msg_id, body="full"))
                elif session_id and path == f"{session_id}/ready":
                    await ws.send(_resp(msg_id))
                    if pushing is None:
                        pushing = asyncio.create_task(self._push_loop(ws, session_id))
                elif session_id and path == f"{session_id}/keepAlive":
                    await ws.send(_resp(msg_id))
                elif session_id and path == f"{session_id}/dispose":
                    if pushing:
                        pushing.cancel()
                        pushing = None
                    await ws.send(_resp(msg_id))
                elif session_id and path == f"{session_id}/queryCellResults":
                    result = self._result()
                    wanted = []
                    for group in body or []:
                        wanted.extend(group if isinstance(group, list) else [group])
                    matches = [
                        c for c in result["cells"]
                        if c["location"] in wanted or c["name"] in wanted
                    ]
                    await ws.send(_resp(msg_id, body=matches))
                elif session_id and path == f"{session_id}/resultChanged" and mtype == "listen":
                    await ws.send(_resp(msg_id))
                elif session_id and path == f"{session_id}/resultChanged" and mtype == "unlisten":
                    await ws.send(_resp(msg_id))
                else:
                    await ws.send(_resp(msg_id, error=-1, body=f"unknown path {path}"))
        except websockets.ConnectionClosed:
            pass
        finally:
            if pushing:
                pushing.cancel()

    async def _push_loop(self, ws, session_id: str) -> None:
        while True:
            await asyncio.sleep(self.interval)
            await ws.send(json.dumps({
                "$type": "event",
                "path": f"{session_id}/resultChanged",
                "body": self._result(),
            }))
