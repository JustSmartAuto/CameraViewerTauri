using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using AntdUI;
using CameraViewer;
using Label = System.Windows.Forms.Label;
using Panel = System.Windows.Forms.Panel;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;

namespace CameraViewerDotnet
{
    public class MainWindow : AntdUI.Window
    {
        private readonly TableLayoutPanel grid;
        private readonly Panel center;
        private readonly Panel maxHost;
        private readonly Panel toolbar;
        private readonly Panel status;
        private readonly AntdUI.PageHeader header;
        private readonly Label timeText;
        private readonly Label versionText;
        private readonly AntdUI.Button settingsBtn;
        private readonly AntdUI.Button themeBtn;
        private readonly AntdUI.Button langBtn;
        private readonly AntdUI.Button aboutBtn;
        private readonly NotifyIcon notifyIcon;
        private readonly System.Windows.Forms.Timer timer;
        private readonly List<CameraCell> cells = new List<CameraCell>();
        private CameraCell maximizedCell;
        private bool allowClose;

        private static readonly Dictionary<int, (int cols, int rows)> Layouts = new Dictionary<int, (int, int)>
        {
            [1] = (1, 1),
            [2] = (2, 1),
            [4] = (2, 2),
            [6] = (3, 2),
            [9] = (3, 3),
            [12] = (4, 3),
            [16] = (4, 4),
        };

        public static Icon LoadAppIcon()
        {
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
            catch { return SystemIcons.Application; }
        }

        public MainWindow()
        {
            Text = I18n.T("appTitle");
            Icon = LoadAppIcon();
            Size = new Size(1200, 800);
            WindowState = FormWindowState.Maximized;
            MinimumSize = new Size(600, 400);
            Mode = ThemeManager.TAMode;

            // 标题栏：AntdUI.Window 不自绘标题栏，需用 PageHeader 提供标题/拖动/最小化/最大化/关闭
            header = new AntdUI.PageHeader
            {
                Dock = DockStyle.Top,
                Height = 40,
                Text = I18n.T("appTitle"),
                ShowIcon = true,
                ShowButton = true,
                BackColor = ThemeManager.Bg2,
            };

            // 工具栏
            toolbar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = ThemeManager.Bg2 };
            toolbar.Paint += (s, e) =>
            {
                using var pen = new Pen(ThemeManager.Border);
                e.Graphics.DrawLine(pen, 0, toolbar.Height - 1, toolbar.Width - 1, toolbar.Height - 1);
            };
            var btns = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
            };

            settingsBtn = MakeButton(92);
            settingsBtn.Click += (s, e) => new SettingsWindow().ShowDialog(this);
            btns.Controls.Add(settingsBtn);

            themeBtn = MakeButton(92);
            themeBtn.Click += (s, e) =>
            {
                var next = ThemeManager.Theme == "dark" ? "light" : "dark";
                ThemeManager.Apply(next);
                ConfigService.App.theme = next;
                ConfigService.SaveApp();
            };
            btns.Controls.Add(themeBtn);

            langBtn = MakeButton(92);
            langBtn.Click += (s, e) =>
            {
                I18n.SetLanguage(I18n.Language == "en" ? "zh" : "en");
                ConfigService.App.language = I18n.Language;
                ConfigService.SaveApp();
            };
            btns.Controls.Add(langBtn);

            aboutBtn = MakeButton(92);
            aboutBtn.Click += (s, e) => new AboutWindow().ShowDialog(this);
            btns.Controls.Add(aboutBtn);

            toolbar.Controls.Add(btns);

            // 中部：相机网格 + 最大化宿主
            center = new Panel { Dock = DockStyle.Fill, BackColor = ThemeManager.Bg };
            grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = ThemeManager.Bg,
            };
            maxHost = new Panel { Dock = DockStyle.Fill, BackColor = ThemeManager.Bg, Visible = false, Padding = new Padding(4) };
            center.Controls.Add(grid);
            center.Controls.Add(maxHost);

            // 状态栏
            status = new Panel { Dock = DockStyle.Bottom, Height = 24, BackColor = ThemeManager.Bg2 };
            var st = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent };
            st.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            st.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            timeText = new Label
            {
                Text = "",
                ForeColor = ThemeManager.FgDim,
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 8.25f),
                Anchor = AnchorStyles.Left,
                Margin = new Padding(8, 4, 0, 0),
            };
            versionText = new Label
            {
                Text = I18n.T("version") + " " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? ""),
                ForeColor = ThemeManager.FgDim,
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 8.25f),
                Anchor = AnchorStyles.Left,
                Margin = new Padding(16, 4, 0, 0),
            };
            st.Controls.Add(timeText, 0, 0);
            st.Controls.Add(versionText, 1, 0);
            status.Controls.Add(st);

            // Dock 顺序：后加入的先布局（header 最顶，toolbar 其下，status 底部，center 填充剩余）
            Controls.Add(center);
            Controls.Add(toolbar);
            Controls.Add(status);
            Controls.Add(header);

            timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += (s, e) => UpdateTime();
            timer.Start();
            UpdateTime();

            notifyIcon = new NotifyIcon
            {
                Text = I18n.T("appTitle"),
                Icon = LoadAppIcon(),
                Visible = true,
            };
            var menu = new ContextMenuStrip();
            var showItem = new ToolStripMenuItem(I18n.T("showWindow"));
            showItem.Click += (s, e) => ShowFromTray();
            var exitItem = new ToolStripMenuItem(I18n.T("exit"));
            exitItem.Click += (s, e) =>
            {
                allowClose = true;
                Close();
            };
            menu.Items.Add(showItem);
            menu.Items.Add(exitItem);
            notifyIcon.ContextMenuStrip = menu;
            notifyIcon.DoubleClick += (s, e) => ShowFromTray();

            FormClosing += (s, e) =>
            {
                if (!allowClose)
                {
                    e.Cancel = true;
                    Hide();
                }
            };
            FormClosed += (s, e) =>
            {
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
                timer.Stop();
                I18n.LanguageChanged -= OnLanguageChanged;
                ThemeManager.ThemeChanged -= ApplyTheme;
            };

            I18n.LanguageChanged += OnLanguageChanged;
            ThemeManager.ThemeChanged += ApplyTheme;

            UpdateToolbarButtons();
            ApplyTheme();
            RebuildGrid();
        }

        private static AntdUI.Button MakeButton(int width) => new AntdUI.Button
        {
            Width = width,
            Height = 30,
            Margin = new Padding(4, 9, 0, 0),
            Type = TTypeMini.Default,
            DefaultBorderColor = ThemeManager.BtnBorder,
            BorderWidth = 1,
        };

        private void UpdateToolbarButtons()
        {
            settingsBtn.Text = I18n.T("setting");
            settingsBtn.IconSvg = AntIcon.Svg(AntIcon.Setting);
            themeBtn.Text = ThemeManager.Theme == "dark" ? I18n.T("themeLight") : I18n.T("themeDark");
            themeBtn.IconSvg = AntIcon.Svg(ThemeManager.Theme == "dark" ? AntIcon.Sun : AntIcon.Moon);
            langBtn.Text = I18n.Language == "en" ? I18n.T("languageZh") : I18n.T("languageEn");
            langBtn.IconSvg = AntIcon.Svg(AntIcon.Global);
            aboutBtn.Text = I18n.T("about");
            aboutBtn.IconSvg = AntIcon.Svg(AntIcon.InfoCircle);
        }

        private void UpdateTime() =>
            timeText.Text = I18n.T("systemTime") + " " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        private void OnLanguageChanged()
        {
            Text = I18n.T("appTitle");
            header.Text = I18n.T("appTitle");
            notifyIcon.Text = I18n.T("appTitle");
            UpdateToolbarButtons();
            UpdateTime();
        }

        private void ApplyTheme()
        {
            Mode = ThemeManager.TAMode;
            header.BackColor = ThemeManager.Bg2;
            toolbar.BackColor = ThemeManager.Bg2;
            status.BackColor = ThemeManager.Bg2;
            center.BackColor = ThemeManager.Bg;
            grid.BackColor = ThemeManager.Bg;
            maxHost.BackColor = ThemeManager.Bg;
            timeText.ForeColor = ThemeManager.FgDim;
            versionText.ForeColor = ThemeManager.FgDim;
            foreach (var c in cells) c.ApplyTheme();
            toolbar.Invalidate();
            status.Invalidate();
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Maximized;
            Activate();
        }

        public void RebuildGrid()
        {
            foreach (var c in cells)
            {
                c.ToggleMaximize -= OnToggleMaximize;
                c.Dispose();
            }
            cells.Clear();
            grid.Controls.Clear();
            maxHost.Controls.Clear();
            maximizedCell = null;
            maxHost.Visible = false;
            grid.Visible = true;

            var count = ConfigService.Camera.count;
            var layout = Layouts.TryGetValue(count, out var l)
                ? l
                : (cols: (int)Math.Ceiling(Math.Sqrt(count)), rows: (int)Math.Ceiling(Math.Sqrt(count)));

            grid.SuspendLayout();
            grid.ColumnCount = layout.cols;
            grid.RowCount = layout.rows;
            grid.ColumnStyles.Clear();
            grid.RowStyles.Clear();
            for (int c = 0; c < layout.cols; c++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / layout.cols));
            for (int r = 0; r < layout.rows; r++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / layout.rows));

            var delay = ConfigService.Camera.delay;
            for (int i = 0; i < count; i++)
            {
                var cell = new CameraCell(i);
                cell.ToggleMaximize += OnToggleMaximize;
                cells.Add(cell);
                cell.Dock = DockStyle.Fill;
                cell.Margin = new Padding(2);
                grid.Controls.Add(cell, i % layout.cols, i / layout.cols);

                var idx = i;
                var ms = (long)idx * delay * 1000;
                if (ms <= 0)
                {
                    _ = cell.ReloadAsync();
                }
                else
                {
                    var t = new System.Windows.Forms.Timer { Interval = (int)Math.Min(ms, int.MaxValue) };
                    t.Tick += (s2, e2) =>
                    {
                        t.Stop();
                        t.Dispose();
                        _ = cell.ReloadAsync();
                    };
                    t.Start();
                }
            }
            grid.ResumeLayout(true);
        }

        private void OnToggleMaximize(CameraCell cell)
        {
            if (maximizedCell == null)
            {
                maximizedCell = cell;
                grid.Controls.Remove(cell);
                grid.Visible = false;
                cell.Dock = DockStyle.Fill;
                maxHost.Controls.Add(cell);
                maxHost.Visible = true;
            }
            else
            {
                maxHost.Controls.Remove(cell);
                maxHost.Visible = false;
                grid.Visible = true;
                grid.Controls.Add(cell);
                // 显式恢复每个单元格位置，避免布局引擎重排
                var cols = Math.Max(1, grid.ColumnCount);
                for (int i = 0; i < cells.Count; i++)
                {
                    if (!grid.Controls.Contains(cells[i])) continue;
                    grid.SetCellPosition(cells[i], new TableLayoutPanelCellPosition(i % cols, i / cols));
                }
                cell.Dock = DockStyle.Fill;
                maximizedCell = null;
            }
        }
    }
}
