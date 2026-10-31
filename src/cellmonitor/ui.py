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
from .config import (
    CameraConfig, CellConfig,
    CELL_DATA_TYPES, NUMERIC_DATA_TYPES, CELL_RECORD_MODES,
    save_config,
)
from .storage import Storage, export_xlsx, timestamp_suffix
from .worker import Monitor

from cellmonitor import __version__

log = logging.getLogger(__name__)

C = theme.COLORS

STATUS_TEXT = {
    "offline": "离线",
    "connecting": "连接中",
    "connected": "已连接",
    "disconnected": "已断开",
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
            type_tag = f"[{cell.data_type}]" if cell.data_type != "auto" else ""
            mode_tag = "记录" if cell.is_simple_record else ""
            prefix = " ".join(t for t in (type_tag, mode_tag) if t)
            label_text = f"{cell.name} {prefix}".strip()
            if cell.note:
                label_text += f"（{cell.note}）"
            tk.Label(row, text=label_text, width=34, anchor="w",
                     bg=C["chrome_lighter"], fg=C["ink"], font=theme.FONT).pack(side="left")
            value = tk.Label(row, text="—", bg=C["chrome_lighter"], fg=C["teal"],
                             font=("Arial", 11, "bold"))
            value.pack(side="left")
            self.value_labels[cell.name] = value
            if cell.is_simple_record:
                tk.Label(row, text="（仅记录·不判定）", bg=C["chrome_lighter"],
                         fg=C["chrome_dark"], font=theme.FONT).pack(side="right")
            elif cell.is_numeric:
                limits = ""
                if cell.lower is not None:
                    limits += f"≥{cell.lower:g}"
                if cell.upper is not None:
                    limits += (" " if limits else "") + f"≤{cell.upper:g}"
                if limits:
                    tk.Label(row, text=limits, bg=C["chrome_lighter"],
                             fg=C["chrome_dark"], font=theme.FONT).pack(side="right")
            elif cell.data_type in ("string", "bool", "other"):
                tk.Label(row, text="（非数值·无阈值）", bg=C["chrome_lighter"],
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
                  "connected": C["teal"], "disconnected": C["chrome_darker"],
                  "error": C["red_dot"]}
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
        self._build_menubar()
        self.root.after(100, self._poll_events)

    def _build_menubar(self) -> None:
        menubar = tk.Menu(self.root, bg=C["chrome"], fg=C["ink"], font=theme.FONT)
        help_menu = tk.Menu(menubar, tearoff=0)
        help_menu.add_command(label="使用说明", command=self.show_help)
        help_menu.add_command(label="关于", command=self.show_about)
        menubar.add_cascade(label="帮助", menu=help_menu)
        self.root.config(menu=menubar)

    def show_help(self) -> None:
        text = (
            "CameraCellMonitor 使用说明\n\n"
            "1. 添加相机：点击「添加相机」，输入名称、IP、端口\n"
            "   （固件 22.2+ 默认 80，6.x+ 默认 8087）。\n"
            "2. 配置单元格：点击「配置单元格」打开交互式表格编辑器：\n"
            "   - 添加/编辑/删除单元格，可上移下移调整顺序\n"
            "   - 数据类型：auto（未知·运行时推断）/ int / float / string / bool / other\n"
            "   - 记录模式：simple（仅记录·不判定）/ advanced（记录+阈值判定）\n"
            "   - 上下限仅在 advanced + 数值类型 时生效；simple 模式恒为 NULL\n"
            "   - 也可点「文本编辑」用 CSV 格式批量编辑（含回退兼容旧格式）\n"
            "3. 连接：点击相机面板上的「连接」或工具栏「连接全部」。\n"
            "4. 趋势图：点击「趋势图」选择单元格，查看历史数据折线图\n"
            "   （红色段 = 超差，虚线 = 阈值基线）。\n"
            "5. 导出：点击「导出xlsx」按相机生成报表。\n\n"
            "数据目录：" + str(self.data_dir) + "\n"
            "  ├─ db/    SQLite 数据库（所有采样点）\n"
            "  ├─ xlsx/  导出的报表\n"
            "  └─ charts/ 生成的趋势图 PNG"
        )
        messagebox.showinfo("使用说明", text)

    def show_about(self) -> None:
        text = (
            "CameraCellMonitor\n"
            f"版本 {__version__}\n\n"
            "Cognex In-Sight 单元格数据监控器\n"
            "（CogSocket 订阅 + SQLite/xlsx 落库 + 趋势图表）\n\n"
            "隶属于 CameraViewerTauri 仓库的 cell-monitor 分支。"
        )
        messagebox.showinfo("关于", text)

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
        """打开交互式单元格编辑器（表格 + 表单），可回退到文本编辑。"""
        cells = [CellConfig(**c.__dict__) for c in (current or [])]
        dlg = CellEditorDialog(self.root, cells)
        return dlg.result or []

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
        if cell_cfg and cell_cfg.data_type in ("string", "bool", "other"):
            messagebox.showinfo("提示",
                                f"单元格「{cell_name}」为 {cell_cfg.data_type} 类型，"
                                "无数值趋势可绘制。")
            return
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


# ---------- 单元格文本格式 ----------
# 名称,数据类型,记录模式,event|poll,轮询间隔秒,下限,上限,单位,注解
_CELL_TEXT_HINT = (
    "每行一个单元格：名称,数据类型,记录模式,event|poll,轮询间隔秒,下限,上限,单位,注解\n"
    "数据类型：auto（未知·运行时推断）/ int / float / string / bool / other\n"
    "记录模式：simple（仅记录·不判定）/ advanced（记录+阈值判定）\n"
    "上下限仅对 advanced + int/float/auto 有效；simple 模式忽略阈值\n"
    "示例：C36,float,advanced,event,0,175,182,mm,焊缝宽度\n"
    "      Status,string,simple,event,0,,,,,判定结果"
)


def _cells_to_text(cells: list[CellConfig]) -> str:
    lines = []
    for c in cells:
        lines.append(",".join([
            c.name, c.data_type, c.record_mode, c.sample_mode,
            f"{c.poll_interval:g}",
            "" if c.lower is None else f"{c.lower:g}",
            "" if c.upper is None else f"{c.upper:g}",
            c.unit, c.note,
        ]))
    return "\n".join(lines)


def _text_to_cells(text: str) -> list[CellConfig]:
    cells = []
    for line in (text or "").splitlines():
        line = line.strip()
        if not line:
            continue
        parts = [p.strip() for p in line.split(",")]
        name = parts[0]
        if not name:
            continue

        def num(i):
            if len(parts) > i and parts[i] not in ("", None):
                try:
                    return float(parts[i])
                except ValueError:
                    return None
            return None

        # v1 旧格式（无 data_type/record_mode/unit）：第 2 段为 event/poll
        if len(parts) > 1 and parts[1] in ("event", "poll"):
            data_type = "auto"
            record_mode = "advanced"
            mode = parts[1]
            interval = num(2) or 1.0
            lower, upper = num(3), num(4)
            unit = ""
            note = ",".join(parts[5:]) if len(parts) > 5 else ""
        # v2 中间格式（有 data_type，无 record_mode）：第 2 段为数据类型且第 3 段为 event/poll
        elif (len(parts) > 2 and parts[1] in CELL_DATA_TYPES
              and parts[2] in ("event", "poll")):
            data_type = parts[1]
            record_mode = "advanced"
            mode = parts[2]
            interval = num(3) or 1.0
            lower, upper = num(4), num(5)
            unit = parts[6] if len(parts) > 6 else ""
            note = ",".join(parts[7:]) if len(parts) > 7 else ""
        # v3 新格式：名称,数据类型,记录模式,采样模式,...
        else:
            data_type = parts[1] if len(parts) > 1 and parts[1] else "auto"
            if data_type not in CELL_DATA_TYPES:
                data_type = "auto"
            record_mode = parts[2] if len(parts) > 2 and parts[2] else "advanced"
            if record_mode not in CELL_RECORD_MODES:
                record_mode = "advanced"
            mode = parts[3] if len(parts) > 3 and parts[3] else "event"
            if mode not in ("event", "poll"):
                mode = "event"
            interval = num(4) or 1.0
            lower, upper = num(5), num(6)
            unit = parts[7] if len(parts) > 7 else ""
            note = ",".join(parts[8:]) if len(parts) > 8 else ""

        cells.append(CellConfig(name=name, data_type=data_type, record_mode=record_mode,
                                sample_mode=mode, poll_interval=interval,
                                lower=lower, upper=upper, unit=unit, note=note))
    return cells


class CellFormDialog(tk.Toplevel):
    """单单元格编辑表单：上下限仅对数值类型可用。"""

    def __init__(self, parent, cell: CellConfig | None = None):
        super().__init__(parent)
        self.title("单元格设置")
        self.result: CellConfig | None = None
        self.transient(parent)
        self.configure(bg=C["chrome"])
        theme.title_tab(self, "单元格设置")

        cell = cell or CellConfig(name="")
        self._cell = CellConfig(**cell.__dict__)

        body = tk.Frame(self, bg=C["chrome"])
        body.pack(fill="both", expand=True, padx=8, pady=6)

        self._name_var = tk.StringVar(value=cell.name)
        self._type_var = tk.StringVar(value=cell.data_type)
        self._record_var = tk.StringVar(value=cell.record_mode)
        self._mode_var = tk.StringVar(value=cell.sample_mode)
        self._interval_var = tk.StringVar(value=f"{cell.poll_interval:g}")
        self._lower_var = tk.StringVar(value="" if cell.lower is None else f"{cell.lower:g}")
        self._upper_var = tk.StringVar(value="" if cell.upper is None else f"{cell.upper:g}")
        self._unit_var = tk.StringVar(value=cell.unit)
        self._note_var = tk.StringVar(value=cell.note)

        def row(r, label, widget):
            tk.Label(body, text=label, bg=C["chrome"], fg=C["ink"],
                     font=theme.FONT).grid(row=r, column=0, sticky="e", padx=4, pady=3)
            widget.grid(row=r, column=1, sticky="we", padx=4, pady=3)

        self._name_entry = tk.Entry(body, textvariable=self._name_var, width=30)
        row(0, "单元格名称", self._name_entry)

        self._type_combo = ttk.Combobox(body, textvariable=self._type_var, width=28,
                                        values=CELL_DATA_TYPES, state="readonly")
        self._type_combo.bind("<<ComboboxSelected>>", lambda e: self._update_limit_state())
        row(1, "数据类型", self._type_combo)

        self._record_combo = ttk.Combobox(body, textvariable=self._record_var, width=28,
                                          values=CELL_RECORD_MODES, state="readonly")
        self._record_combo.bind("<<ComboboxSelected>>", lambda e: self._update_limit_state())
        row(2, "记录模式", self._record_combo)

        self._mode_combo = ttk.Combobox(body, textvariable=self._mode_var, width=28,
                                        values=("event", "poll"), state="readonly")
        self._mode_combo.bind("<<ComboboxSelected>>", lambda e: self._on_mode_change())
        row(3, "采样模式", self._mode_combo)

        self._interval_entry = tk.Entry(body, textvariable=self._interval_var, width=30)
        row(4, "轮询间隔(秒)", self._interval_entry)

        self._lower_entry = tk.Entry(body, textvariable=self._lower_var, width=30)
        row(5, "下限", self._lower_entry)

        self._upper_entry = tk.Entry(body, textvariable=self._upper_var, width=30)
        row(6, "上限", self._upper_entry)

        self._unit_entry = tk.Entry(body, textvariable=self._unit_var, width=30)
        row(7, "单位", self._unit_entry)

        self._note_entry = tk.Entry(body, textvariable=self._note_var, width=30)
        row(8, "注解", self._note_entry)

        self._hint_label = tk.Label(body, text="", bg=C["chrome"], fg=C["teal"],
                                    font=theme.FONT, justify="left", wraplength=320)
        self._hint_label.grid(row=9, column=0, columnspan=2, sticky="w", padx=4, pady=(6, 0))

        body.columnconfigure(1, weight=1)

        btns = tk.Frame(self, bg=C["chrome"])
        btns.pack(pady=6)
        theme.beos_button(btns, "确定", self._ok, default=True).pack(side="left", padx=4)
        theme.beos_button(btns, "取消", self.destroy).pack(side="left", padx=4)

        self._update_limit_state()
        self._on_mode_change()

        self.grab_set()
        parent.wait_window(self)

    def _update_limit_state(self) -> None:
        """根据数据类型与记录模式决定上下限是否可用，并更新提示。"""
        simple = self._record_var.get() == "simple"
        numeric = self._type_var.get() in NUMERIC_DATA_TYPES
        limits_enabled = (not simple) and numeric
        for ent in (self._lower_entry, self._upper_entry):
            ent.config(state="normal" if limits_enabled else "disabled")

        if simple:
            self._hint_label.config(
                text="简单记录模式：仅记录单元格数值，不做上下限阈值判定，判定结果恒为 NULL。",
                fg=C["teal"])
        elif numeric:
            self._hint_label.config(
                text="高级模式·数值类型：上下限阈值生效。auto 类型在运行时按实际值推断。",
                fg=C["teal"])
        else:
            self._hint_label.config(
                text=f"高级模式·非数值类型（{self._type_var.get()}）：上下限不生效，判定结果为 NULL。",
                fg=C["red_dot"])

    def _on_mode_change(self) -> None:
        poll = self._mode_var.get() == "poll"
        self._interval_entry.config(state="normal" if poll else "disabled")

    def _ok(self) -> None:
        name = self._name_var.get().strip()
        if not name:
            messagebox.showerror("错误", "请填写单元格名称。", parent=self)
            return
        try:
            interval = float(self._interval_var.get()) if self._interval_var.get() else 1.0
        except ValueError:
            messagebox.showerror("错误", "轮询间隔必须是数字。", parent=self)
            return

        def parse_num(var):
            s = var.get().strip()
            if not s:
                return None
            try:
                return float(s)
            except ValueError:
                return None

        simple = self._record_var.get() == "simple"
        lower = None if simple else parse_num(self._lower_var)
        upper = None if simple else parse_num(self._upper_var)
        self.result = CellConfig(
            name=name,
            data_type=self._type_var.get(),
            record_mode=self._record_var.get(),
            sample_mode=self._mode_var.get(),
            poll_interval=interval,
            lower=lower, upper=upper,
            unit=self._unit_var.get().strip(),
            note=self._note_var.get().strip(),
        )
        self.destroy()


class CellEditorDialog(tk.Toplevel):
    """交互式单元格编辑器：表格 + 增删改 + 文本编辑回退。"""

    COLUMNS = ("name", "type", "record", "mode", "interval", "lower", "upper", "unit", "note")
    HEADINGS = ("名称", "类型", "记录", "模式", "间隔", "下限", "上限", "单位", "注解")
    WIDTHS = (100, 60, 60, 55, 55, 65, 65, 45, 130)

    def __init__(self, parent, cells: list[CellConfig]):
        super().__init__(parent)
        self.title("订阅单元格")
        self.result: list[CellConfig] | None = None
        self.transient(parent)
        self.configure(bg=C["chrome"])
        theme.title_tab(self, "订阅单元格", f"{len(cells)} 个")

        self._cells: list[CellConfig] = [CellConfig(**c.__dict__) for c in cells]

        top = tk.Frame(self, bg=C["chrome"])
        top.pack(fill="both", expand=True, padx=8, pady=6)

        self._tree = ttk.Treeview(top, columns=self.COLUMNS, show="headings", height=8)
        for col, head, w in zip(self.COLUMNS, self.HEADINGS, self.WIDTHS):
            self._tree.heading(col, text=head)
            self._tree.column(col, width=w, anchor="w")
        self._tree.pack(side="left", fill="both", expand=True)
        self._tree.bind("<Double-1>", lambda e: self._edit())

        sb = ttk.Scrollbar(top, orient="vertical", command=self._tree.yview)
        sb.pack(side="right", fill="y")
        self._tree.configure(yscrollcommand=sb.set)

        side = tk.Frame(self, bg=C["chrome"])
        side.pack(fill="x", padx=8, pady=(0, 4))
        theme.beos_button(side, "添加", self._add).pack(side="left", padx=3)
        theme.beos_button(side, "编辑", self._edit).pack(side="left", padx=3)
        theme.beos_button(side, "删除", self._delete).pack(side="left", padx=3)
        theme.beos_button(side, "上移", self._move_up).pack(side="left", padx=3)
        theme.beos_button(side, "下移", self._move_down).pack(side="left", padx=3)
        theme.beos_button(side, "文本编辑", self._text_edit).pack(side="left", padx=3)

        btns = tk.Frame(self, bg=C["chrome"])
        btns.pack(pady=6)
        theme.beos_button(btns, "确定", self._ok, default=True).pack(side="left", padx=4)
        theme.beos_button(btns, "取消", self.destroy).pack(side="left", padx=4)

        self._refresh()
        self.grab_set()
        parent.wait_window(self)

    def _refresh(self) -> None:
        for item in self._tree.get_children():
            self._tree.delete(item)
        for c in self._cells:
            self._tree.insert("", "end", values=(
                c.name, c.data_type, c.record_mode, c.sample_mode, f"{c.poll_interval:g}",
                "" if c.lower is None else f"{c.lower:g}",
                "" if c.upper is None else f"{c.upper:g}",
                c.unit, c.note,
            ))
        theme.title_tab(self, "订阅单元格", f"{len(self._cells)} 个")

    def _selected_index(self) -> int | None:
        sel = self._tree.selection()
        if not sel:
            return None
        return self._tree.index(sel[0])

    def _add(self) -> None:
        dlg = CellFormDialog(self)
        if dlg.result:
            self._cells.append(dlg.result)
            self._refresh()

    def _edit(self) -> None:
        idx = self._selected_index()
        if idx is None:
            messagebox.showinfo("提示", "请先选择一个单元格。", parent=self)
            return
        dlg = CellFormDialog(self, self._cells[idx])
        if dlg.result:
            self._cells[idx] = dlg.result
            self._refresh()

    def _delete(self) -> None:
        idx = self._selected_index()
        if idx is None:
            return
        if messagebox.askyesno("确认", f"删除单元格「{self._cells[idx].name}」？", parent=self):
            del self._cells[idx]
            self._refresh()

    def _move_up(self) -> None:
        idx = self._selected_index()
        if idx and idx > 0:
            self._cells[idx], self._cells[idx - 1] = self._cells[idx - 1], self._cells[idx]
            self._refresh()
            self._tree.selection_set(self._tree.get_children()[idx - 1])

    def _move_down(self) -> None:
        idx = self._selected_index()
        if idx is not None and idx < len(self._cells) - 1:
            self._cells[idx], self._cells[idx + 1] = self._cells[idx + 1], self._cells[idx]
            self._refresh()
            self._tree.selection_set(self._tree.get_children()[idx + 1])

    def _text_edit(self) -> None:
        text = CellDialog(self, "文本编辑单元格", _CELL_TEXT_HINT,
                          _cells_to_text(self._cells)).result
        if text is not None:
            self._cells = _text_to_cells(text)
            self._refresh()

    def _ok(self) -> None:
        self.result = self._cells
        self.destroy()


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
