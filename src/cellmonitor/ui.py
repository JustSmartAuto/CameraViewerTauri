"""tkinter 多相机宫格界面（BeOS 主题，见 theme.py）。

布局：顶部工具栏（连接/断开、添加相机、导出），中部相机宫格面板，
底部日志。asyncio 事件循环运行于独立线程，通过队列与 UI 线程通信。
"""

from __future__ import annotations

import asyncio
import logging
import threading
import tkinter as tk
from tkinter import messagebox, simpledialog, ttk

from . import charts, stats, theme
from .config import CameraConfig, CellConfig, save_config
from .storage import Storage, export_xlsx, timestamp_suffix
from .worker import Monitor

log = logging.getLogger(__name__)

C = theme.COLORS

STATUS_TEXT = {
    "offline": "离线",
    "connecting": "连接中",
    "connected": "已连接",
    "error": "错误",
}


class CameraPanel(tk.Frame):
    """单相机面板（BeOS 窗口）：黄色标题页签、白色内容区、药丸按钮。"""

    def __init__(self, master, app: "MonitorApp", cfg: CameraConfig):
        super().__init__(master, bg=C["chrome"], highlightthickness=1,
                         highlightbackground=C["chrome_darker"], bd=0)
        self.app = app
        self.cfg = cfg
        self.status = "offline"
        self.value_labels: dict[str, tk.Label] = {}

        tab_text = f"{cfg.name}  ({cfg.host}:{cfg.port})"
        bar, self.status_label = theme.title_tab(self, tab_text, STATUS_TEXT["offline"])

        content = theme.content_frame(self)
        content.pack(fill="both", expand=True, padx=2, pady=2)

        if not cfg.cells:
            tk.Label(content, text="（未配置单元格）", bg=C["chrome_lighter"],
                     fg=C["chrome_dark"], font=theme.FONT).pack(anchor="w", padx=6, pady=4)
        for cell in cfg.cells:
            row = tk.Frame(content, bg=C["chrome_lighter"])
            row.pack(fill="x", padx=6, pady=1)
            label_text = cell.name + (f"（{cell.note}）" if cell.note else "")
            tk.Label(row, text=label_text, width=30, anchor="w",
                     bg=C["chrome_lighter"], fg=C["ink"], font=theme.FONT).pack(side="left")
            value = tk.Label(row, text="—", bg=C["chrome_lighter"], fg=C["teal"],
                             font=("Arial", 11, "bold"))
            value.pack(side="left")
            self.value_labels[cell.name] = value
            limits = ""
            if cell.lower is not None:
                limits += f"≥{cell.lower:g}"
            if cell.upper is not None:
                limits += (" " if limits else "") + f"≤{cell.upper:g}"
            if limits:
                tk.Label(row, text=limits, bg=C["chrome_lighter"],
                         fg=C["chrome_dark"], font=theme.FONT).pack(side="right")

        btns = tk.Frame(self, bg=C["chrome"])
        btns.pack(fill="x", padx=2, pady=(0, 2))
        theme.beos_button(btns, "连接", lambda: self.app.connect_camera(cfg)).pack(side="left", padx=3)
        theme.beos_button(btns, "断开", lambda: self.app.disconnect_camera(cfg.name)).pack(side="left", padx=3)
        theme.beos_button(btns, "趋势图", lambda: self.app.show_chart(cfg)).pack(side="left", padx=3)
        theme.beos_button(btns, "导出xlsx", lambda: self.app.export_camera(cfg)).pack(side="left", padx=3)

    def set_status(self, status: str, detail: str = "") -> None:
        self.status = status
        colors = {"offline": C["chrome_darker"], "connecting": C["yellow_dark"],
                  "connected": C["teal"], "error": C["red_dot"]}
        self.status_label.config(text=STATUS_TEXT.get(status, status),
                                 fg=colors.get(status, C["ink"]))

    def set_value(self, cell: str, value) -> None:
        label = self.value_labels.get(cell)
        if label:
            label.config(text=str(value))


class MonitorApp:
    def __init__(self, data_dir, cameras: list[CameraConfig], extra_startup: list | None = None):
        self.data_dir = data_dir
        self.cameras = cameras
        self.extra_startup = extra_startup or []
        self.storage = Storage(data_dir / "db" / "cell_monitor.db")
        self.monitor = Monitor(self.storage)
        self.panels: dict[str, CameraPanel] = {}

        self.root = tk.Tk()
        self.root.title("CameraCellMonitor — Cognex In-Sight 单元格数据监控器")
        self.root.geometry("800x600")
        theme.apply(self.root)
        self._build_ui()
        self._start_asyncio_thread()

    # ---------- UI 构建 ----------

    def _build_ui(self) -> None:
        toolbar = theme.window_frame(self.root)
        toolbar.pack(fill="x", padx=8, pady=(8, 4))
        theme.title_tab(toolbar, "CameraCellMonitor", "控制面板")
        bar = tk.Frame(toolbar, bg=C["chrome"])
        bar.pack(fill="x", padx=2, pady=2)
        theme.beos_button(bar, "连接全部", self.connect_all, default=True).pack(side="left", padx=3, pady=3)
        theme.beos_button(bar, "断开全部", self.disconnect_all).pack(side="left", padx=3)
        theme.beos_button(bar, "添加相机", self.add_camera).pack(side="left", padx=3)
        theme.beos_button(bar, "配置单元格", self.edit_camera).pack(side="left", padx=3)
        theme.beos_button(bar, "打开数据目录", self.open_data_dir).pack(side="left", padx=3)
        tk.Label(bar, text=f"数据目录: {self.data_dir}", bg=C["chrome"],
                 fg=C["chrome_darker"], font=theme.FONT).pack(side="right", padx=6)

        self.grid_frame = tk.Frame(self.root, bg=C["bg"])
        self.grid_frame.pack(fill="both", expand=True, padx=6, pady=4)
        self._rebuild_grid()

        logbox = theme.window_frame(self.root)
        logbox.pack(fill="x", padx=8, pady=(4, 8))
        theme.title_tab(logbox, "日志")
        self.log_text = tk.Text(logbox, height=6, state="disabled", bg=C["chrome_lighter"],
                                fg=C["ink"], font=("Consolas", 9), relief="flat",
                                highlightthickness=1, highlightbackground=C["chrome_darker"])
        self.log_text.pack(fill="x", padx=2, pady=(0, 2))

        self.root.protocol("WM_DELETE_WINDOW", self.on_close)
        self.root.after(100, self._poll_events)

    def _rebuild_grid(self) -> None:
        for child in self.grid_frame.winfo_children():
            child.destroy()
        self.panels.clear()
        n = max(len(self.cameras), 1)
        cols = min(3, n)
        for i, cfg in enumerate(self.cameras):
            panel = CameraPanel(self.grid_frame, self, cfg)
            panel.grid(row=i // cols, column=i % cols, sticky="nsew", padx=5, pady=5)
            self.panels[cfg.name] = panel
        for c in range(cols):
            self.grid_frame.columnconfigure(c, weight=1)

    # ---------- asyncio 线程 ----------

    def _start_asyncio_thread(self) -> None:
        self.loop = asyncio.new_event_loop()

        def run():
            asyncio.set_event_loop(self.loop)
            self.loop.run_forever()

        self.thread = threading.Thread(target=run, daemon=True)
        self.thread.start()
        for coro in self.extra_startup:
            asyncio.run_coroutine_threadsafe(coro, self.loop)

    def _submit(self, coro) -> None:
        asyncio.run_coroutine_threadsafe(coro, self.loop)

    # ---------- 相机操作 ----------

    def connect_camera(self, cfg: CameraConfig) -> None:
        self.panels[cfg.name].set_status("connecting")
        self._submit(self.monitor.start_camera(cfg))

    def disconnect_camera(self, name: str) -> None:
        self._submit(self.monitor.stop_camera(name))

    def connect_all(self) -> None:
        for cfg in self.cameras:
            if cfg.enabled:
                self.connect_camera(cfg)

    def disconnect_all(self) -> None:
        self._submit(self.monitor.stop_all())

    # ---------- 事件轮询 ----------

    def _poll_events(self) -> None:
        try:
            while True:
                camera, kind, payload = self.monitor.events.get_nowait()
                panel = self.panels.get(camera)
                if kind in ("connected", "disconnected", "error"):
                    if panel:
                        panel.set_status("connected" if kind == "connected" else kind,
                                         str(payload or ""))
                    self._log(f"[{camera}] {kind} {payload or ''}")
                elif kind == "result" and panel:
                    for cell, value in (payload or {}).items():
                        panel.set_value(cell, value)
        except Exception:
            pass
        self.root.after(100, self._poll_events)

    def _log(self, msg: str) -> None:
        self.log_text.config(state="normal")
        self.log_text.insert("end", msg + "\n")
        self.log_text.see("end")
        self.log_text.config(state="disabled")

    # ---------- 配置 ----------

    def add_camera(self) -> None:
        name = simpledialog.askstring("添加相机", "相机名称：", parent=self.root)
        if not name:
            return
        host = simpledialog.askstring("添加相机", "相机 IP：", parent=self.root)
        if not host:
            return
        port = simpledialog.askinteger("添加相机", "端口（22.2+ 默认 80，6.x+ 默认 8087）：",
                                       initialvalue=80, parent=self.root)
        if not port:
            return
        cells = self._ask_cells()
        cfg = CameraConfig(name=name, host=host, port=port, cells=cells)
        self.cameras.append(cfg)
        save_config(self.data_dir, self.cameras)
        self._rebuild_grid()

    def edit_camera(self) -> None:
        if not self.cameras:
            messagebox.showinfo("提示", "暂无相机，请先添加。")
            return
        names = [c.name for c in self.cameras]
        name = simpledialog.askstring("配置单元格", f"相机名（{', '.join(names)}）：", parent=self.root)
        if not name:
            return
        cfg = next((c for c in self.cameras if c.name == name), None)
        if not cfg:
            messagebox.showerror("错误", f"未找到相机 {name}")
            return
        cfg.cells = self._ask_cells(cfg.cells)
        save_config(self.data_dir, self.cameras)
        self._rebuild_grid()

    def _ask_cells(self, current: list[CellConfig] | None = None) -> list[CellConfig]:
        """简易单元格编辑：每行 `名称[,event|poll,间隔,下限,上限,注解]`。"""
        hint = ("每行一个单元格：名称[,event|poll,轮询间隔秒,下限,上限,注解]\n"
                "示例：C36,event,0,175,182,焊缝宽度mm")
        initial = "\n".join(
            f"{c.name},{c.sample_mode},{c.poll_interval},"
            f"{'' if c.lower is None else c.lower},{'' if c.upper is None else c.upper},{c.note}"
            for c in (current or [])
        )
        text = CellDialog(self.root, "订阅单元格", hint, initial).result
        cells = []
        for line in (text or "").splitlines():
            line = line.strip()
            if not line:
                continue
            parts = [p.strip() for p in line.split(",")]
            name = parts[0]
            mode = parts[1] if len(parts) > 1 and parts[1] else "event"

            def num(i):
                if len(parts) > i and parts[i] not in ("", None):
                    try:
                        return float(parts[i])
                    except ValueError:
                        return None
                return None

            interval = num(2) or 1.0
            cells.append(CellConfig(name=name, sample_mode=mode,
                                    poll_interval=interval,
                                    lower=num(3), upper=num(4),
                                    note=",".join(parts[5:]) if len(parts) > 5 else ""))
        return cells

    # ---------- 图表 / 导出 ----------

    def show_chart(self, cfg: CameraConfig) -> None:
        if not cfg.cells:
            messagebox.showinfo("提示", "该相机未配置单元格。")
            return
        names = [c.name for c in cfg.cells]
        cell_name = simpledialog.askstring("趋势图", f"单元格（{', '.join(names)}）：", parent=self.root)
        if not cell_name:
            return
        cell_cfg = next((c for c in cfg.cells if c.name == cell_name), None)
        points = self.storage.query(cfg.name, cell_name)
        if not points:
            messagebox.showinfo("提示", "该单元格暂无数据。")
            return
        path = self.data_dir / "charts" / f"{cfg.name}_{cell_name}{timestamp_suffix()}.png"
        title = f"{cfg.name} / {cell_name}"
        if cell_cfg and cell_cfg.note:
            title += f"（{cell_cfg.note}）"
        fig = charts.plot_trend(points, title,
                                cell_cfg.lower if cell_cfg else None,
                                cell_cfg.upper if cell_cfg else None, path)
        self._log(f"图表已保存: {path}")
        ChartWindow(self.root, fig, f"{title} 趋势图")

        values = [r[1] for r in points if r[1] is not None]
        if values:
            s = stats.capability(values, cell_cfg.lower if cell_cfg else None,
                                 cell_cfg.upper if cell_cfg else None)
            self._log(
                f"[{cfg.name}/{cell_name}] n={s['n']} 均值={s['mean']:.4f} "
                f"σ={s['stdev']:.4f} 超差率={s.get('out_of_spec_rate', 0)*100:.2f}%"
                + (f" CPK={s['cpk']:.3f}" if "cpk" in s else "")
            )

    def export_camera(self, cfg: CameraConfig):
        try:
            path = export_xlsx(self.storage, cfg.name, self.data_dir / "xlsx",
                               notes={c.name: c.note for c in cfg.cells})
        except Exception as exc:
            messagebox.showerror("导出失败", str(exc))
            return
        self._log(f"xlsx 已导出: {path}")

    def open_data_dir(self) -> None:
        import os
        os.startfile(self.data_dir)  # noqa: S606 - Windows only

    # ---------- 生命周期 ----------

    def on_close(self) -> None:
        self.disconnect_all()
        self.root.after(300, self._finish_close)

    def _finish_close(self) -> None:
        self.loop.call_soon_threadsafe(self.loop.stop)
        self.storage.close()
        save_config(self.data_dir, self.cameras)
        self.root.destroy()

    def run(self) -> None:
        self.root.mainloop()


class CellDialog(tk.Toplevel):
    """多行文本编辑对话框。"""

    def __init__(self, parent, title: str, hint: str, initial: str):
        super().__init__(parent)
        self.title(title)
        self.result = None
        self.transient(parent)
        self.configure(bg=C["chrome"])
        theme.title_tab(self, title)
        tk.Label(self, text=hint, justify="left", bg=C["chrome"], fg=C["ink"],
                 font=theme.FONT).pack(anchor="w", padx=8, pady=4)
        self.text = tk.Text(self, width=60, height=10, bg=C["chrome_lighter"], fg=C["ink"],
                            font=("Consolas", 9), relief="flat", highlightthickness=1,
                            highlightbackground=C["chrome_darker"])
        self.text.pack(padx=8, pady=4)
        self.text.insert("1.0", initial)
        btns = tk.Frame(self, bg=C["chrome"])
        btns.pack(pady=6)
        theme.beos_button(btns, "确定", self._ok, default=True).pack(side="left", padx=4)
        theme.beos_button(btns, "取消", self.destroy).pack(side="left", padx=4)
        self.grab_set()
        parent.wait_window(self)

    def _ok(self):
        self.result = self.text.get("1.0", "end")
        self.destroy()


class ChartWindow(tk.Toplevel):
    """内嵌 matplotlib 图表窗口。"""

    def __init__(self, parent, fig, title: str):
        super().__init__(parent)
        self.title(title)
        self.configure(bg=C["chrome"])
        theme.title_tab(self, title)
        from matplotlib.backends.backend_tkagg import FigureCanvasTkAgg

        canvas = FigureCanvasTkAgg(fig, master=self)
        canvas.draw()
        canvas.get_tk_widget().pack(fill="both", expand=True, padx=2, pady=2)
