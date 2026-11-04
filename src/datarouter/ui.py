"""DataRouterPy 主界面。

对标 CameraViewerDotnet/CameraHelperLens 的「文件转存」设置页
（文件转存界面.png）：

    名称下拉 + 添加/删除 + 启用转存 + 保存应用
    路径 + 浏览/打开
    内容分隔符 / 文件夹内容序号 / 文件名内容序号
    分组框「文件夹」：路径分隔符、部件编辑表、原路径序号、格式化
    分组框「文件名称」：文件名分隔符、部件编辑表、原路径序号、格式化
底部附加：转存日志区 + 状态栏（运行中配置/计数）。
"""

from __future__ import annotations

import os
import queue
import tkinter as tk
from pathlib import Path
from tkinter import filedialog, messagebox, simpledialog, ttk

from . import __version__, theme
from .config import PartItem, TransformConfig, load_config, save_config
from .router import Router

PART_TYPES = ("名称", "时间")
DEFAULT_FMT = "yyyy-MM-dd"


class RouterApp:
    """DataRouterPy 主窗口。"""

    def __init__(self, data_dir: Path, configs: list[TransformConfig]) -> None:
        self.data_dir = data_dir
        self.configs = configs
        self.routers: dict[str, Router] = {}
        self.log_queue: queue.Queue[str] = queue.Queue()
        self.total_moves = 0
        self._loading_idx = 0

        self.root = tk.Tk()
        self.root.title(f"DataRouterPy 文件转存 v{__version__}")
        self.root.geometry("860x660")
        self.root.minsize(760, 560)
        self._set_icon()
        theme.apply(self.root)

        self._build_menu()
        self._build_titlebar()
        self._build_body()
        self._build_statusbar()
        self._load_config_to_ui(self.configs[0])
        self._loading_idx = self._current_index()
        # 初始应用一次（启用中的配置直接开始转存）
        self.apply_all()
        self.root.after(50, self._poll_log)

    # —— 窗口骨架 ————————————————————————————————————————————

    def _set_icon(self) -> None:
        icon = Path(__file__).resolve().parents[2] / "assets" / "router.png"
        if icon.exists():
            self._icon = tk.PhotoImage(file=str(icon))
            self.root.iconphoto(True, self._icon)

    def _build_menu(self) -> None:
        menubar = tk.Menu(self.root)
        help_menu = tk.Menu(menubar, tearoff=0)
        help_menu.add_command(label="使用说明", command=self._show_help)
        help_menu.add_command(label="关于", command=self._show_about)
        menubar.add_cascade(label="帮助", menu=help_menu)
        self.root.config(menu=menubar)

    def _build_titlebar(self) -> None:
        bar, self.status_tag = theme.titlebar(
            self.root, "  DataRouterPy — 文件转存", end_text="未运行")
        logo = Path(__file__).resolve().parents[2] / "assets" / "router.png"
        if logo.exists():
            img = tk.PhotoImage(file=str(logo)).subsample(3, 3)
            self._title_img = img
            tk.Label(bar, image=img, bg=theme.TITLE).pack(side="left", padx=(4, 0))

    def _build_statusbar(self) -> None:
        bar, self.sb_left, self.sb_mid, self.sb_right = theme.statusbar(self.root)
        self.sb_right.config(text=f"v{__version__}")

    # —— 主区（设置页） ————————————————————————————————————————

    def _build_body(self) -> None:
        main = theme.group(self.root)
        main.pack(fill="both", expand=True, padx=10, pady=(10, 6))

        # 第一行：名称 + 添加/删除 + 启用转存 …… 保存应用
        row1 = tk.Frame(main, bg=theme.CHROME)
        row1.pack(fill="x", padx=8, pady=(10, 4))
        tk.Label(row1, text="名称", bg=theme.CHROME).pack(side="left")
        self.cmb_name = ttk.Combobox(row1, style="Win.TCombobox", state="readonly", width=18)
        self.cmb_name.pack(side="left", padx=(8, 12))
        self.cmb_name.bind("<<ComboboxSelected>>", lambda _e: self._on_select_config())
        self.btn_add_name = theme.win95_button(row1, "添加", width=8, command=self._on_add_config)
        self.btn_add_name.pack(side="left", padx=(0, 8))
        self.btn_del_name = theme.win95_button(row1, "删除", width=8, command=self._on_del_config)
        self.btn_del_name.pack(side="left", padx=(0, 12))
        self.chk_enabled_var = tk.BooleanVar(value=True)
        chk = tk.Checkbutton(
            row1, text="启用转存", variable=self.chk_enabled_var,
            bg=theme.CHROME, activebackground=theme.CHROME, font=theme.FONT)
        chk.pack(side="left")
        self.btn_apply = theme.win95_button(row1, "保存应用", width=16, command=self._on_save_apply)
        self.btn_apply.pack(side="right")

        # 第二行：路径 + 浏览 + 打开
        row2 = tk.Frame(main, bg=theme.CHROME)
        row2.pack(fill="x", padx=8, pady=4)
        tk.Label(row2, text="路径", bg=theme.CHROME).pack(side="left")
        self.ent_watch = theme.field(row2)
        self.ent_watch.pack(side="left", fill="x", expand=True, padx=(8, 12), ipady=2)
        theme.win95_button(row2, "浏览", command=self._on_browse).pack(side="right", padx=(8, 0))
        theme.win95_button(row2, "打开", command=self._on_open_folder).pack(side="right")

        # 第三行：内容分隔符 / 文件夹内容序号 / 文件名内容序号
        row3 = tk.Frame(main, bg=theme.CHROME)
        row3.pack(fill="x", padx=8, pady=4)
        tk.Label(row3, text="内容分隔符", bg=theme.CHROME).pack(side="left")
        self.ent_src_split = theme.field(row3, width=10)
        self.ent_src_split.pack(side="left", padx=(4, 20), ipady=2)
        tk.Label(row3, text="文件夹内容序号", bg=theme.CHROME).pack(side="left")
        self.spn_folder_idx = ttk.Spinbox(row3, style="Win.TSpinbox", from_=0, to=99, width=6)
        self.spn_folder_idx.pack(side="left", padx=(4, 20))
        tk.Label(row3, text="文件名内容序号", bg=theme.CHROME).pack(side="left")
        self.spn_file_idx = ttk.Spinbox(row3, style="Win.TSpinbox", from_=0, to=99, width=6)
        self.spn_file_idx.pack(side="left", padx=(4, 0))

        # 左右两个分组框
        body = tk.Frame(main, bg=theme.CHROME)
        body.pack(fill="both", expand=True, padx=8, pady=(10, 8))
        self._build_folder_group(body)
        self._build_filename_group(body)

        log_frame = theme.group(self.root, "转存日志")
        log_frame.pack(fill="x", padx=10, pady=(0, 6))
        self.txt_log = tk.Text(
            log_frame, height=9, bg=theme.CHROME_DARKER, fg=theme.CHROME_LIGHT,
            insertbackground=theme.CHROME_LIGHT, font=("Consolas", 9),
            state="disabled", relief="sunken", bd=2)
        self.txt_log.pack(side="left", fill="both", expand=True, padx=6, pady=6)
        sb = tk.Scrollbar(log_frame, command=self.txt_log.yview)
        sb.pack(side="right", fill="y", padx=(0, 6), pady=6)
        self.txt_log.config(yscrollcommand=sb.set)

    # —— 部件编辑分组框（文件夹 / 文件名称 共用构建） ————————————

    def _make_part_panel(self, parent: tk.Misc, title: str,
                         picker_label: str) -> dict:
        """构建一个部件编辑分组框，句柄 dict 用于读写部件列表。"""
        gr = theme.group(parent, title)
        handle = {"frame": gr, "parts": []}

        top = tk.Frame(gr, bg=theme.CHROME)
        top.pack(fill="x", padx=8, pady=(6, 2))
        tk.Label(top, text=f"{picker_label}", bg=theme.CHROME).pack(side="left")
        ent = theme.field(top, width=10)
        ent.pack(side="left", padx=(8, 16), ipady=2)
        handle["split_entry"] = ent

        btns = tk.Frame(gr, bg=theme.CHROME)
        btns.pack(fill="x", padx=8, pady=2)
        theme.win95_button(btns, "添加", command=lambda: self._part_add(handle)).pack(side="left", padx=(0, 8))
        theme.win95_button(btns, "删除", command=lambda: self._part_del(handle)).pack(side="left", padx=(0, 8))
        theme.win95_button(btns, "清空", command=lambda: self._part_clear(handle)).pack(side="left")

        cols = ("序号", "类型")
        tree = ttk.Treeview(gr, columns=cols, show="headings", style="Win.Treeview", height=9)
        tree.heading("序号", text="序号", anchor="center")
        tree.heading("类型", text="类型", anchor="w")
        tree.column("序号", width=60, anchor="center", stretch=False)
        tree.column("类型", width=120, anchor="w", stretch=True)
        tree.pack(fill="both", expand=True, padx=8, pady=(4, 2))
        tree.bind("<<TreeviewSelect>>", lambda _e: self._part_selected(handle))
        tree.bind("<Double-1>", lambda _e: self._part_toggle_type(handle))
        handle["tree"] = tree

        # 底部：原路径序号（类型=名称） / 格式化（类型=时间）
        bottom = tk.Frame(gr, bg=theme.CHROME)
        bottom.pack(fill="x", padx=8, pady=(2, 8))
        tk.Label(bottom, text="原路径序号", bg=theme.CHROME).pack(side="left")
        spn = ttk.Spinbox(bottom, style="Win.TSpinbox", from_=0, to=99, width=6)
        spn.pack(side="left", padx=(4, 16))
        spn.bind("<ButtonRelease-1>", lambda _e: self._part_apply_edit(handle, "index"))
        spn.bind("<KeyRelease>", lambda _e: self._part_apply_edit(handle, "index"))
        handle["index_spin"] = spn

        tk.Label(bottom, text="格式化", bg=theme.CHROME).pack(side="right")
        fm = theme.field(bottom, width=14)
        fm.pack(side="right", padx=(4, 0), ipady=2)
        fm.bind("<KeyRelease>", lambda _e: self._part_apply_edit(handle, "fmt"))
        handle["fmt_entry"] = fm
        handle["fmt_label"] = tk.Label(bottom, text="", bg=theme.CHROME)
        handle["fmt_label"].pack(side="right", padx=(16, 0))
        return handle

    def _build_folder_group(self, parent: tk.Frame) -> None:
        self.folder_handle = self._make_part_panel(parent, "文件夹", "路径分隔符")
        self.folder_handle["frame"].pack(side="left", fill="both", expand=True, padx=(0, 10))
        # 提示占位（第二标签列），与截图一致的两个分组框互相撑开
        self.folder_handle["fmt_label"].config(text="")

    def _build_filename_group(self, parent: tk.Frame) -> None:
        self.file_handle = self._make_part_panel(parent, "文件名称", "文件名分隔符")
        self.file_handle["frame"].pack(side="left", fill="both", expand=True)

    # —— 配置 ↔ 界面 ————————————————————————————————————————

    def _collect_ui(self, cfg: TransformConfig) -> TransformConfig:
        """把界面当前值写入 cfg（不落盘）。"""
        cfg.name = self.cmb_name.get() or cfg.name
        cfg.enabled = bool(self.chk_enabled_var.get())
        cfg.watch_path = self.ent_watch.get().strip()
        cfg.source_splitter = self.ent_src_split.get() or ","
        cfg.folder_idx = self._spin_int(self.spn_folder_idx)
        cfg.filename_idx = self._spin_int(self.spn_file_idx)
        cfg.folder_splitter = self.folder_handle["split_entry"].get() or "+"
        cfg.filename_splitter = self.file_handle["split_entry"].get() or "_"
        cfg.folder_parts = [dict_to_part(p) for p in self.folder_handle["parts"]]
        cfg.filename_parts = [dict_to_part(p) for p in self.file_handle["parts"]]
        return cfg

    def _load_config_to_ui(self, cfg: TransformConfig) -> None:
        names = [c.name for c in self.configs]
        self.cmb_name["values"] = names
        self.cmb_name.set(cfg.name)
        self.chk_enabled_var.set(cfg.enabled)
        self.ent_watch.delete(0, tk.END)
        self.ent_watch.insert(0, cfg.watch_path)
        self.ent_src_split.delete(0, tk.END)
        self.ent_src_split.insert(0, cfg.source_splitter)
        self.spn_folder_idx.set(str(cfg.folder_idx))
        self.spn_file_idx.set(str(cfg.filename_idx))
        self.folder_handle["split_entry"].delete(0, tk.END)
        self.folder_handle["split_entry"].insert(0, cfg.folder_splitter)
        self.file_handle["split_entry"].delete(0, tk.END)
        self.file_handle["split_entry"].insert(0, cfg.filename_splitter)
        self._refresh_part_tree(self.folder_handle, cfg.folder_parts)
        self._refresh_part_tree(self.file_handle, cfg.filename_parts)

    @staticmethod
    def _spin_int(spn: ttk.Spinbox) -> int:
        try:
            return int(str(spn.get()))
        except (ValueError, tk.TclError):
            return 0

    # —— 配置列表操作 ————————————————————————————————————————

    def _current_index(self) -> int:
        name = self.cmb_name.get()
        for i, cfg in enumerate(self.configs):
            if cfg.name == name:
                return i
        return 0

    def _on_select_config(self) -> None:
        # 先把旧配置的界面值存回内存（名称取旧配置自身的，避免被新下拉值覆盖）
        old_idx = getattr(self, "_loading_idx", 0)
        if 0 <= old_idx < len(self.configs):
            old_name = self.configs[old_idx].name
            self._collect_ui(self.configs[old_idx])
            self.configs[old_idx].name = old_name
        idx = self._current_index()
        self._loading_idx = idx
        self._load_config_to_ui(self.configs[idx])

    def _on_add_config(self) -> None:
        name = simpledialog.askstring(
            "添加转存配置", "请输入转存配置名称：", parent=self.root)
        if not name or not name.strip():
            return
        name = name.strip()
        if any(c.name == name for c in self.configs):
            messagebox.showinfo("提示", "该名称已存在", parent=self.root)
            return
        self._collect_ui(self.configs[self._current_index()])
        new_cfg = TransformConfig(name=name)
        new_cfg.watch_path = self.configs[self._current_index()].watch_path
        self.configs.append(new_cfg)
        self._loading_idx = len(self.configs) - 1
        self._load_config_to_ui(new_cfg)

    def _on_del_config(self) -> None:
        if len(self.configs) <= 1:
            messagebox.showinfo("提示", "至少保留一套转存配置", parent=self.root)
            return
        idx = self._current_index()
        name = self.configs[idx].name
        if not messagebox.askyesno(
                "删除", f"确定删除当前转存配置「{name}」？", parent=self.root):
            return
        self.configs.pop(idx)
        self._loading_idx = min(idx, len(self.configs) - 1)
        self._load_config_to_ui(self.configs[self._loading_idx])

    def _on_save_apply(self) -> None:
        self._collect_ui(self.configs[self._current_index()])
        save_config(self.data_dir, self.configs)
        self.apply_all()
        self.sb_left.config(text=f"配置已保存并应用（{self.data_dir / 'TransformConfig.json'}）")

    # —— 部件列表操作 ————————————————————————————————————————

    def _refresh_part_tree(self, handle: dict, parts: list[PartItem]) -> None:
        handle["parts"] = [dict(source_index=p.source_index, type=p.type, fmt=p.fmt) for p in parts]
        self._render_part_tree(handle, select_index=-1)

    def _render_part_tree(self, handle: dict, select_index: int = -1) -> None:
        tree: ttk.Treeview = handle["tree"]
        parts = handle["parts"]
        tree.delete(*tree.get_children())
        for i, part in enumerate(parts):
            type_text = PART_TYPES[0] if part["type"] == "name" else PART_TYPES[1]
            tree.insert("", tk.END, values=(i, type_text))
        if 0 <= select_index < len(parts):
            iid = tree.get_children()[select_index]
            tree.selection_set(iid)
            tree.see(iid)
        else:
            self._part_apply_edit(handle, "refresh")

    def _selected_part_row(self, handle: dict) -> int:
        tree: ttk.Treeview = handle["tree"]
        sel = tree.selection()
        if not sel:
            return -1
        try:
            row = int(tree.index(sel[0]))
            if 0 <= row < len(handle["parts"]):
                return row
        except (ValueError, IndexError):
            pass
        return -1

    def _part_add(self, handle: dict) -> None:
        handle["parts"].append(dict(source_index=len(handle["parts"]), type="name", fmt=DEFAULT_FMT))
        self._render_part_tree(handle, select_index=len(handle["parts"]) - 1)

    def _part_del(self, handle: dict) -> None:
        row = self._selected_part_row(handle)
        if row < 0:
            return
        handle["parts"].pop(row)
        self._render_part_tree(handle, select_index=min(row, len(handle["parts"]) - 1))

    def _part_clear(self, handle: dict) -> None:
        handle["parts"].clear()
        self._render_part_tree(handle)

    def _part_toggle_type(self, handle: dict) -> None:
        row = self._selected_part_row(handle)
        if row < 0:
            return
        part = handle["parts"][row]
        if part["type"] == "name":
            part["type"] = "datetime"
            if not part["fmt"]:
                part["fmt"] = DEFAULT_FMT
        else:
            part["type"] = "name"
        self._render_part_tree(handle, select_index=row)

    def _part_selected(self, handle: dict) -> None:
        self._show_edit_fields(handle)
        self._fill_edit_fields(handle, self._selected_part_row(handle))

    def _show_edit_fields(self, handle: dict) -> None:
        """根据选中行类型显示/禁用 原路径序号 与 格式化。"""
        row = self._selected_part_row(handle)
        is_name = row >= 0 and handle["parts"][row]["type"] == "name"
        is_dt = row >= 0 and handle["parts"][row]["type"] == "datetime"
        if is_name or is_dt:
            handle["index_spin"].config(state="normal" if is_name else "disabled")
            handle["fmt_entry"].config(state="normal" if is_dt else "disabled")
        else:
            handle["index_spin"].config(state="disabled")
            handle["fmt_entry"].config(state="disabled")

    def _fill_edit_fields(self, handle: dict, row: int) -> None:
        spn: ttk.Spinbox = handle["index_spin"]
        fm: tk.Entry = handle["fmt_entry"]
        handle["current_row"] = row
        if row < 0:
            spn.set("")
            fm.delete(0, tk.END)
            self._show_edit_fields(handle)
            return
        part = handle["parts"][row]
        spn.set(str(part["source_index"]))
        fm.delete(0, tk.END)
        if part["type"] == "datetime" and part["fmt"]:
            fm.insert(0, part["fmt"])
        self._show_edit_fields(handle)

    def _part_apply_edit(self, handle: dict, field_name: str = "") -> None:
        row = handle.get("current_row", -1)
        if row < 0 or row >= len(handle["parts"]):
            return
        part = handle["parts"][row]
        if field_name == "index":
            try:
                part["source_index"] = max(0, int(str(handle["index_spin"].get())))
            except ValueError:
                pass
        elif field_name == "fmt":
            part["fmt"] = handle["fmt_entry"].get().strip()

    # —— 路径操作 ————————————————————————————————————————————

    def _watch_path(self) -> str:
        return self.ent_watch.get().strip()

    def _on_browse(self) -> None:
        folder = filedialog.askdirectory(parent=self.root)
        if folder:
            self.ent_watch.delete(0, tk.END)
            self.ent_watch.insert(0, Path(folder).as_posix())

    def _on_open_folder(self) -> None:
        watch = self._watch_path()
        if not watch:
            messagebox.showinfo("提示", "请先填写路径", parent=self.root)
            return
        p = Path(watch)
        if not p.exists():
            try:
                p.mkdir(parents=True, exist_ok=True)
            except OSError as exc:
                messagebox.showerror("错误", f"无法创建目录：\n{exc}", parent=self.root)
                return
        os.startfile(str(p))  # noqa: S606 - Windows 打开资源管理器

    # —— 日志与状态 ——————————————————————————————————————————

    def _log(self, line: str) -> None:
        self.log_queue.put(line)

    def _poll_log(self) -> None:
        while True:
            try:
                line = self.log_queue.get_nowait()
            except queue.Empty:
                break
            self.txt_log.config(state="normal")
            self.txt_log.insert(tk.END, line + "\n")
            self.txt_log.see(tk.END)
            self.txt_log.config(state="disabled")
        self._update_status()
        self.root.after(200, self._poll_log)

    def _update_status(self) -> None:
        running = [n for n, r in self.routers.items() if r.running]
        if running:
            self.status_tag.config(text=f"● 运行中 {len(running)} 套")
            self.sb_mid.config(text="正常")
        else:
            self.status_tag.config(text="未运行")
            self.sb_mid.config(text="空闲")
        current = self._current_index()
        cfg = self.configs[current] if self.configs else None
        if cfg:
            self.sb_left.config(
                text=f"配置 {cfg.name} | 路径 {cfg.watch_path or '(未设置)'}")

    # —— 启动 / 停止全部转存 ——————————————————————————————————

    def apply_all(self) -> None:
        """按当前配置列表重建并启动所有启用的转存线程。"""
        for router in self.routers.values():
            router.stop()
        self.routers.clear()
        for cfg in self.configs:
            if not cfg.enabled:
                continue
            router = Router(cfg, self._log)
            router.start()
            self.routers[cfg.name] = router
        self._log(f"已应用配置：启用 {len(self.routers)}/{len(self.configs)} 套")
        self._update_status()

    # —— 帮助 / 关于 —————————————————————————————————————————

    def _show_help(self) -> None:
        messagebox.showinfo(
            "使用说明",
            "1. 填写路径：要监控转存的来源目录\n"
            "2. 文件名用「内容分隔符」拆段；文件夹内容序号 / 文件名内容序号\n"
            "   指定用第几段作为目标文件夹与目标文件名的来源\n"
            "3. 在「文件夹」「文件名称」分组框添加组成段：\n"
            "   · 类型=名称：按「原路径序号」取拆分后的第几段\n"
            "   · 类型=时间：按「格式化」插入当前日期时间（双击类型可切换）\n"
            "4. 保存应用后即开始监控并自动转存，底部可查看日志",
            parent=self.root)

    def _show_about(self) -> None:
        messagebox.showinfo(
            "关于",
            f"DataRouterPy v{__version__}\n"
            "监控目录文件转存 / 文件名拆分重组\n"
            "对标 CameraViewerDotnet 文件转存功能",
            parent=self.root)

    # —— 运行 —————————————————————————————————————————————————

    def run(self) -> None:
        self.root.protocol("WM_DELETE_WINDOW", self._on_close)
        self.root.mainloop()

    def _on_close(self) -> None:
        for router in self.routers.values():
            router.stop()
        self.root.destroy()


def dict_to_part(node: dict) -> PartItem:
    return PartItem(source_index=int(node.get("source_index", 0) or 0),
                    type=str(node.get("type", "name")),
                    fmt=str(node.get("fmt", "")))
