using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AntdUI;
using CameraViewer;

namespace CameraViewerDotnet
{
    public class SettingsWindow : AntdUI.Window
    {
        private readonly AntdUI.Tabs tabs;
        private readonly AntdUI.PageHeader titleBar;
        private readonly List<Action> langUpdaters = new List<Action>();

        private TableLayoutPanel jobxHost;
        private AntdUI.Table jobxGrid;
        private readonly List<JobxRow> jobxRows = new List<JobxRow>();
        private TextBox jobxLog;
        private AntdUI.Button jobxAddBtn, jobxBackupBtn, jobxBackupAllBtn, jobxOpenDirBtn;
        private System.Windows.Forms.Timer jobxLogTimer;

        public SettingsWindow()
        {
            Text = I18n.T("systemSettings");
            Size = new Size(720, 620);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Resizable = false;
            MaximizeBox = false;
            MinimizeBox = false;
            Mode = ThemeManager.TAMode;

            tabs = new AntdUI.Tabs { Dock = DockStyle.Fill, Type = TabType.Line };
            tabs.Pages.Add(MakePage(I18n.T("cameraDisplay"), BuildDisplayPage()));
            tabs.Pages.Add(MakePage(I18n.T("cameraSettings"), BuildCameraPage()));
            tabs.Pages.Add(MakePage(I18n.T("displaySettings"), BuildThemePage()));
            tabs.Pages.Add(MakePage(I18n.T("appSettings"), BuildAppPage()));
            tabs.Pages.Add(MakePage(I18n.T("fileTransit"), BuildTransitPage()));
            tabs.Pages.Add(MakePage(I18n.T("cleanup"), BuildCleanPage()));
            tabs.Pages.Add(MakePage(I18n.T("jobxBackup"), BuildJobxPage()));

            // 标题栏：AntdUI.Window 不自绘标题栏，需用 PageHeader 提供标题/关闭按钮
            titleBar = new AntdUI.PageHeader
            {
                Dock = DockStyle.Top,
                Height = 36,
                Text = I18n.T("systemSettings"),
                ShowButton = true,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = ThemeManager.Bg2,
            };

            // Dock 顺序：后加入的先布局（titleBar 占顶部，tabs 填充剩余）
            Controls.Add(tabs);
            Controls.Add(titleBar);

            I18n.LanguageChanged += OnLanguageChanged;
            ThemeManager.ThemeChanged += ApplyTheme;
            FormClosed += (s, e) =>
            {
                I18n.LanguageChanged -= OnLanguageChanged;
                ThemeManager.ThemeChanged -= ApplyTheme;
                jobxLogTimer?.Stop();
                jobxLogTimer?.Dispose();
            };
        }

        private void OnLanguageChanged()
        {
            Text = I18n.T("systemSettings");
            titleBar.Text = I18n.T("systemSettings");
            var names = new[] { I18n.T("cameraDisplay"), I18n.T("cameraSettings"), I18n.T("displaySettings"), I18n.T("appSettings"), I18n.T("fileTransit"), I18n.T("cleanup"), I18n.T("jobxBackup") };
            for (int i = 0; i < tabs.Pages.Count && i < names.Length; i++)
                tabs.Pages[i].Text = names[i];
            foreach (var u in langUpdaters) u();
            RebuildJobxTable();
        }

        private void ApplyTheme()
        {
            Mode = ThemeManager.TAMode;
            titleBar.BackColor = ThemeManager.Bg2;
            if (jobxLog != null)
            {
                jobxLog.BackColor = ThemeManager.Bg3;
                jobxLog.ForeColor = ThemeManager.Fg;
            }
        }

        private static AntdUI.TabPage MakePage(string header, Control content)
        {
            var page = new AntdUI.TabPage { Text = header, Padding = new Padding(12) };
            page.Controls.Add(content);
            return page;
        }

        private static AntdUI.Label MakeLabel(string text, bool bold = false) => new AntdUI.Label
        {
            Text = text,
            AutoSize = true,
            Font = bold ? new Font("Microsoft YaHei UI", 9f, FontStyle.Bold) : null,
            Margin = new Padding(0, 8, 8, 8),
        };

        // ---------- 相机显示 ----------
        private Control BuildDisplayPage()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var countRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
            var countLabel = MakeLabel(I18n.T("count"));
            countRow.Controls.Add(countLabel);
            var countSelect = new AntdUI.Select { Width = 120, Height = 30 };
            var counts = new[] { 1, 2, 4, 6, 9, 12, 16, 20, 24, 25, 30, 36 };
            foreach (var n in counts) countSelect.Items.Add(n);
            countSelect.SelectedIndex = Array.IndexOf(counts, ConfigService.Camera.count);
            countSelect.SelectedIndexChanged += (s, e) =>
            {
                if (e.Value >= 0 && e.Value < counts.Length)
                {
                    ConfigService.Camera.count = counts[e.Value];
                    ConfigService.SaveCamera();
                    var main = Application.OpenForms.OfType<MainWindow>().FirstOrDefault();
                    main?.RebuildGrid();
                }
            };
            countRow.Controls.Add(countSelect);
            panel.Controls.Add(countRow, 0, 0);

            var delayRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
            var delayLabel = MakeLabel(I18n.T("delay"));
            delayRow.Controls.Add(delayLabel);
            var delayNum = new AntdUI.InputNumber
            {
                Width = 120,
                Height = 30,
                Minimum = 0,
                Maximum = 600,
                Increment = 1,
                Value = ConfigService.Camera.delay,
            };
            delayNum.ValueChanged += (s, e) =>
            {
                ConfigService.Camera.delay = (int)e.Value;
                ConfigService.SaveCamera();
            };
            delayRow.Controls.Add(delayNum);
            panel.Controls.Add(delayRow, 0, 1);

            langUpdaters.Add(() =>
            {
                countLabel.Text = I18n.T("count");
                delayLabel.Text = I18n.T("delay");
            });
            return panel;
        }

        // ---------- 相机设置 ----------
        private Control BuildCameraPage()
        {
            // 外层可滚动容器
            var scroll = new System.Windows.Forms.Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = ThemeManager.Bg2,
                Padding = new Padding(0, 4, 0, 4),
            };

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(0, 0, 8, 4),
                BackColor = ThemeManager.Bg2,
            };

            for (int i = 0; i < ConfigService.Camera.count; i++)
            {
                var item = ConfigService.EnsureItem(i);
                item.conn = ConfigService.NormalizeConn(item.conn);
                var conn = item.conn;

                var card = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    ColumnCount = 1,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    BackColor = ThemeManager.Bg3,
                    Padding = new Padding(8),
                    Margin = new Padding(0, 0, 0, 8),
                };
                int rowCount = conn == "cogsocket" ? 8 : 7;
                for (int r = 0; r < rowCount; r++) card.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                var header = MakeLabel(I18n.T("camera") + " " + (i + 1), true);
                card.Controls.Add(header, 0, 0);

                // 第 1 行：连接方式下拉
                var connRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
                var connLabel = MakeLabel(I18n.T("conn"));
                connRow.Controls.Add(connLabel);
                var connSelect = new AntdUI.Select { Width = 140, Height = 30 };
                connSelect.Items.Add(I18n.T("connHttp"));
                connSelect.Items.Add(I18n.T("connCogsocket"));
                connSelect.Items.Add(I18n.T("connGige"));
                connSelect.SelectedIndex = conn == "cogsocket" ? 1 : conn == "gige" ? 2 : 0;
                int idx = i;
                connSelect.SelectedIndexChanged += (s, e) =>
                {
                    var it = ConfigService.EnsureItem(idx);
                    var newConn = e.Value == 1 ? "cogsocket" : e.Value == 2 ? "gige" : "http";
                    if (it.conn == newConn) return;
                    it.conn = newConn;
                    ConfigService.SaveCamera();
                    // 重建整个设置页以切换字段可见性
                    RebuildCameraPage();
                };
                connRow.Controls.Add(connSelect);
                card.Controls.Add(connRow, 0, 1);

                // 第 2 行：URL / host:port（Http + CogSocket 显示）
                AntdUI.Input urlBox = null;
                AntdUI.Input serialBox = null;
                AntdUI.InputNumber exposureNum = null;

                if (conn != "gige")
                {
                    var urlRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
                    var urlLabel = MakeLabel(conn == "cogsocket" ? I18n.T("ip") : I18n.T("url"));
                    urlRow.Controls.Add(urlLabel);
                    urlBox = new AntdUI.Input
                    {
                        Width = 380,
                        Height = 30,
                        Text = item.ip,
                        ReadOnly = item.locked,
                        PlaceholderText = conn == "cogsocket" ? I18n.T("connPlaceholder") : I18n.T("urlPlaceholder"),
                    };
                    urlBox.LostFocus += (s, e) =>
                    {
                        var it = ConfigService.EnsureItem(idx);
                        it.ip = urlBox.Text;
                        ConfigService.SaveCamera();
                    };
                    urlRow.Controls.Add(urlBox);
                    card.Controls.Add(urlRow, 0, 2);
                }

                // 第 3 行：序列号（GigE 显示）
                if (conn == "gige")
                {
                    var serialRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
                    var serialLabel = MakeLabel(I18n.T("serial"));
                    serialRow.Controls.Add(serialLabel);
                    serialBox = new AntdUI.Input
                    {
                        Width = 380,
                        Height = 30,
                        Text = item.serial,
                        ReadOnly = item.locked,
                        PlaceholderText = I18n.T("serialPlaceholder"),
                    };
                    serialBox.LostFocus += (s, e) =>
                    {
                        var it = ConfigService.EnsureItem(idx);
                        it.serial = serialBox.Text;
                        ConfigService.SaveCamera();
                    };
                    serialRow.Controls.Add(serialBox);
                    card.Controls.Add(serialRow, 0, 2);

                    // 第 4 行：曝光时间
                    var expRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
                    var expLabel = MakeLabel(I18n.T("exposure"));
                    expRow.Controls.Add(expLabel);
                    exposureNum = new AntdUI.InputNumber
                    {
                        Width = 120,
                        Height = 30,
                        Minimum = 0,
                        Maximum = 10000,
                        Increment = 1,
                        Value = item.exposure,
                    };
                    exposureNum.ValueChanged += (s, e) =>
                    {
                        var it = ConfigService.EnsureItem(idx);
                        it.exposure = (int)e.Value;
                        ConfigService.SaveCamera();
                    };
                    expRow.Controls.Add(exposureNum);
                    card.Controls.Add(expRow, 0, 3);
                }

                // CogSocket 专用：cellSubscribe + subGridCount
                AntdUI.Input cellSubscribeBox = null;
                AntdUI.Select subGridSelect = null;
                if (conn == "cogsocket")
                {
                    var cellRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
                    var cellLabel = MakeLabel(I18n.T("cellSubscribe"));
                    cellRow.Controls.Add(cellLabel);
                    cellSubscribeBox = new AntdUI.Input
                    {
                        Width = 380, Height = 30,
                        Text = item.cellSubscribe,
                        PlaceholderText = I18n.T("cellSubscribePlaceholder"),
                    };
                    cellSubscribeBox.LostFocus += (s, e) =>
                    {
                        var it = ConfigService.EnsureItem(idx);
                        it.cellSubscribe = cellSubscribeBox.Text;
                        ConfigService.SaveCamera();
                    };
                    cellRow.Controls.Add(cellSubscribeBox);
                    card.Controls.Add(cellRow, 0, 3);

                    var subRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
                    var subLabel = MakeLabel(I18n.T("subGridCount"));
                    subRow.Controls.Add(subLabel);
                    subGridSelect = new AntdUI.Select { Width = 120, Height = 30 };
                    subGridSelect.Items.AddRange(new object[] { "1", "2", "4", "6", "8" });
                    subGridSelect.SelectedIndex = new[] { 1, 2, 4, 6, 8 }.ToList().IndexOf(Math.Clamp(item.subGridCount, 1, 8));
                    subGridSelect.SelectedIndexChanged += (s, e) =>
                    {
                        var it = ConfigService.EnsureItem(idx);
                        it.subGridCount = new[] { 1, 2, 4, 6, 8 }[e.Value];
                        ConfigService.SaveCamera();
                    };
                    subRow.Controls.Add(subGridSelect);
                    card.Controls.Add(subRow, 0, 4);
                }

                // 备注行
                var remarkRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
                var remarkLabel = MakeLabel(I18n.T("remark"));
                remarkRow.Controls.Add(remarkLabel);
                var remarkBox = new AntdUI.Input { Width = 380, Height = 30, Text = item.remark, ReadOnly = item.locked };
                remarkBox.LostFocus += (s, e) =>
                {
                    var it = ConfigService.EnsureItem(idx);
                    it.remark = remarkBox.Text;
                    ConfigService.SaveCamera();
                };
                remarkRow.Controls.Add(remarkBox);
                card.Controls.Add(remarkRow, 0, conn == "cogsocket" ? 5 : conn == "gige" ? 4 : 3);

                // 锁定行
                var lockCheck = new AntdUI.Checkbox { Text = I18n.T("locked"), Checked = item.locked, Margin = new Padding(0, 4, 0, 2) };
                lockCheck.CheckedChanged += (s, e) =>
                {
                    var it = ConfigService.EnsureItem(idx);
                    it.locked = e.Value;
                    if (urlBox != null) urlBox.ReadOnly = e.Value;
                    if (serialBox != null) serialBox.ReadOnly = e.Value;
                    remarkBox.ReadOnly = e.Value;
                    ConfigService.SaveCamera();
                };
                card.Controls.Add(lockCheck, 0, conn == "cogsocket" ? 6 : conn == "gige" ? 5 : 4);

                panel.Controls.Add(card, 0, i);

                // 语言更新（每次重建卡片都会重绑，简化处理：只更新静态文本）
                langUpdaters.Add(() =>
                {
                    header.Text = I18n.T("camera") + " " + (idx + 1);
                    connLabel.Text = I18n.T("conn");
                    connSelect.Items[0] = I18n.T("connHttp");
                    connSelect.Items[1] = I18n.T("connCogsocket");
                    connSelect.Items[2] = I18n.T("connGige");
                    if (urlBox != null)
                    {
                        urlBox.PlaceholderText = conn == "cogsocket" ? I18n.T("connPlaceholder") : I18n.T("urlPlaceholder");
                    }
                    if (serialBox != null) serialBox.PlaceholderText = I18n.T("serialPlaceholder");
                    remarkLabel.Text = I18n.T("remark");
                    lockCheck.Text = I18n.T("locked");
                });
            }
            scroll.Controls.Add(panel);
            return scroll;
        }

        /// <summary>相机数量变化后重建设置页（因为每个卡片字段依赖 conn 类型）。</summary>
        private void RebuildCameraPage()
        {
            var tabPage = tabs.Pages.FirstOrDefault(p => p.Text == I18n.T("cameraSettings"));
            if (tabPage == null) return;
            tabPage.Controls.Clear();
            tabPage.Controls.Add(BuildCameraPage());
        }

        // ---------- 显示设置 ----------
        private Control BuildThemePage()
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
            var themeLabel = MakeLabel(I18n.T("themeSetting"), true);
            panel.Controls.Add(themeLabel);

            var light = new AntdUI.Radio { Text = I18n.T("themeLight"), Checked = ThemeManager.Theme == "light", AutoSizeMode = TAutoSize.Auto, Margin = new Padding(0, 8, 0, 4) };
            light.CheckedChanged += (s, e) => { if (e.Value) ApplyThemeChoice("light"); };
            var dark = new AntdUI.Radio { Text = I18n.T("themeDark"), Checked = ThemeManager.Theme == "dark", AutoSizeMode = TAutoSize.Auto, Margin = new Padding(0, 0, 0, 4) };
            dark.CheckedChanged += (s, e) => { if (e.Value) ApplyThemeChoice("dark"); };
            var beos = new AntdUI.Radio { Text = I18n.T("themeBeos"), Checked = ThemeManager.Theme == "beos", AutoSizeMode = TAutoSize.Auto, Margin = new Padding(0, 0, 0, 4) };
            beos.CheckedChanged += (s, e) => { if (e.Value) ApplyThemeChoice("beos"); };

            panel.Controls.Add(light);
            panel.Controls.Add(dark);
            panel.Controls.Add(beos);
            langUpdaters.Add(() =>
            {
                themeLabel.Text = I18n.T("themeSetting");
                light.Text = I18n.T("themeLight");
                dark.Text = I18n.T("themeDark");
                beos.Text = I18n.T("themeBeos");
            });
            return panel;
        }

        private void ApplyThemeChoice(string theme)
        {
            ThemeManager.Apply(theme);
            ConfigService.App.theme = theme;
            ConfigService.SaveApp();
        }

        // ---------- 软件设置（配置目录 + 语言） ----------
        private Control BuildAppPage()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // 语言
            panel.Controls.Add(MakeLabel(I18n.T("languageSetting"), true), 0, 0);
            var langRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
            var zh = new AntdUI.Radio { Text = I18n.T("languageZh"), Checked = I18n.Language == "zh", AutoSizeMode = TAutoSize.Auto, Margin = new Padding(0, 0, 16, 4) };
            zh.CheckedChanged += (s, e) => { if (e.Value) { I18n.SetLanguage("zh"); ConfigService.App.language = "zh"; ConfigService.SaveApp(); } };
            var en = new AntdUI.Radio { Text = I18n.T("languageEn"), Checked = I18n.Language == "en", AutoSizeMode = TAutoSize.Auto, Margin = new Padding(0, 0, 0, 4) };
            en.CheckedChanged += (s, e) => { if (e.Value) { I18n.SetLanguage("en"); ConfigService.App.language = "en"; ConfigService.SaveApp(); } };
            langRow.Controls.Add(zh);
            langRow.Controls.Add(en);
            panel.Controls.Add(langRow, 0, 1);

            // 配置目录
            var dirHeader = MakeLabel(I18n.T("configDir"), true);
            dirHeader.Margin = new Padding(0, 16, 0, 4);
            panel.Controls.Add(dirHeader, 0, 2);

            var dirRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
            var dirBox = new AntdUI.Input
            {
                Width = 420,
                Height = 30,
                Text = ConfigService.ConfigDir,
                PlaceholderText = I18n.T("configDirPlaceholder"),
            };
            dirRow.Controls.Add(dirBox);

            var browseBtn = new AntdUI.Button { Width = 78, Height = 30, Text = I18n.T("select"), Margin = new Padding(8, 0, 0, 0) };
            browseBtn.Click += (s, e) =>
            {
                using var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = I18n.T("configDir") };
                if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                    dirBox.Text = dlg.SelectedPath;
            };
            dirRow.Controls.Add(browseBtn);

            var openDirBtn = new AntdUI.Button { Width = 100, Height = 30, Text = I18n.T("openDir"), Margin = new Padding(8, 0, 0, 0) };
            openDirBtn.Click += (s, e) =>
            {
                try
                {
                    if (!Directory.Exists(ConfigService.ConfigDir)) Directory.CreateDirectory(ConfigService.ConfigDir);
                    System.Diagnostics.Process.Start("explorer.exe", ConfigService.ConfigDir);
                }
                catch { MessageBox.Show(I18n.T("openDirFailed")); }
            };
            dirRow.Controls.Add(openDirBtn);
            panel.Controls.Add(dirRow, 0, 3);

            var tip = new AntdUI.Label { Text = I18n.T("configDirWarn"), Margin = new Padding(0, 4, 0, 0), AutoSize = true };
            langUpdaters.Add(() =>
            {
                dirHeader.Text = I18n.T("configDir");
                browseBtn.Text = I18n.T("select");
                openDirBtn.Text = I18n.T("openDir");
                dirBox.PlaceholderText = I18n.T("configDirPlaceholder");
                tip.Text = I18n.T("configDirWarn");
            });
            panel.Controls.Add(tip, 0, 4);

            var applyRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
            var applyBtn = new AntdUI.Button { Width = 110, Height = 30, Text = I18n.T("copyAndRestart") };
            applyBtn.Click += (s, e) =>
            {
                var newDir = (dirBox.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(newDir)) return;
                if (newDir.Equals(ConfigService.ConfigDir, StringComparison.OrdinalIgnoreCase)) return;

                try
                {
                    Directory.CreateDirectory(newDir);
                    foreach (var f in Directory.GetFiles(ConfigService.ConfigDir))
                    {
                        var dest = Path.Combine(newDir, Path.GetFileName(f));
                        if (!File.Exists(dest)) File.Copy(f, dest);
                    }
                    ConfigService.SetConfigDir(newDir);
                    // 提示重启
                    MessageBox.Show(I18n.T("copyAndRestart"), I18n.T("appTitle"), MessageBoxButtons.OK);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, I18n.T("error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            applyRow.Controls.Add(applyBtn);

            var resetBtn = new AntdUI.Button { Width = 110, Height = 30, Text = I18n.T("useDefault"), Margin = new Padding(8, 0, 0, 0), Type = AntdUI.TTypeMini.Default };
            resetBtn.Click += (s, e) =>
            {
                ConfigService.ResetConfigDir();
                dirBox.Text = ConfigService.ConfigDir;
                MessageBox.Show(I18n.T("copyAndRestart"), I18n.T("appTitle"), MessageBoxButtons.OK);
            };
            applyRow.Controls.Add(resetBtn);
            panel.Controls.Add(applyRow, 0, 5);

            langUpdaters.Add(() =>
            {
                applyBtn.Text = I18n.T("copyAndRestart");
                resetBtn.Text = I18n.T("useDefault");
            });

            langUpdaters.Add(() =>
            {
                dirHeader.Text = I18n.T("configDir");
                browseBtn.Text = I18n.T("select");
                openDirBtn.Text = I18n.T("openDir");
                dirBox.PlaceholderText = I18n.T("configDirPlaceholder");
                tip.Text = I18n.T("configDirWarn");
            });
            return panel;
        }

        [Obsolete("Replaced by BuildAppPage")]
        private Control BuildLanguagePage() { return BuildAppPage(); }

        // ---------- 文件中转（复刻 CameraHelperLens 完整 UI） ----------
        private Control BuildTransitPage()
        {
            // 外层可滚动
            var scroll = new System.Windows.Forms.Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = ThemeManager.Bg2, Padding = new Padding(0, 4, 0, 4) };

            var panel = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, Padding = new Padding(4) };
            for (int r = 0; r < 8; r++) panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var t = ConfigService.Transform;

            // Row 0: 启用 + 保存应用 + 立即处理
            var topRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
            var enableCheck = new AntdUI.Checkbox { Text = I18n.T("transitEnable"), Checked = t.IsEnable, AutoSizeMode = TAutoSize.Auto };
            enableCheck.CheckedChanged += (s, e) => { t.IsEnable = e.Value; ConfigService.SaveTransform(); };
            topRow.Controls.Add(enableCheck);

            var saveBtn = new AntdUI.Button { Width = 110, Height = 30, Text = I18n.T("apply"), Margin = new Padding(16, 0, 0, 0) };
            saveBtn.Click += (s, e) =>
            {
                ConfigService.SaveTransform();
                App.Transit?.UpdateConfig(t);
            };
            topRow.Controls.Add(saveBtn);

            var runBtn = new AntdUI.Button { Width = 130, Height = 30, Text = I18n.T("transitRun"), Margin = new Padding(8, 0, 0, 0) };
            runBtn.Click += (s, e) =>
                _ = System.Threading.Tasks.Task.Run(() => { try { new TransformAction { Config = t }.RunAction(); } catch { } });
            topRow.Controls.Add(runBtn);
            panel.Controls.Add(topRow, 0, 0);

            // Row 1: WatchPath + 浏览
            var watchRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
            watchRow.Controls.Add(MakeLabel(I18n.T("transitWatch")));
            var watchBox = new AntdUI.Input { Width = 560, Height = 30, Text = t.WatchPath, PlaceholderText = "D:\\CameraOutput\\待处理" };
            watchBox.LostFocus += (s, e) => { t.WatchPath = watchBox.Text.Trim(); };
            watchRow.Controls.Add(watchBox);
            var watchBrowseBtn = new AntdUI.Button { Width = 78, Height = 30, Text = I18n.T("select"), Margin = new Padding(8, 0, 0, 0) };
            watchBrowseBtn.Click += (s, e) =>
            {
                using var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = I18n.T("transitWatch") };
                if (dlg.ShowDialog(this) == DialogResult.OK) watchBox.Text = dlg.SelectedPath;
            };
            watchRow.Controls.Add(watchBrowseBtn);
            panel.Controls.Add(watchRow, 0, 1);

            // Row 2: 分隔符 + 索引
            var splitRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
            splitRow.Controls.Add(MakeLabel(I18n.T("transitSourceSplit")));
            var splitBox = new AntdUI.Input { Width = 60, Height = 30, Text = t.SourceSpliter };
            splitBox.LostFocus += (s, e) => { if (!string.IsNullOrEmpty(splitBox.Text)) t.SourceSpliter = splitBox.Text[0].ToString(); };
            splitRow.Controls.Add(splitBox);
            splitRow.Controls.Add(MakeLabel(I18n.T("transitFolderIdx")));
            var folderIdxBox = new AntdUI.InputNumber { Width = 80, Height = 30, Minimum = 0, Maximum = 99, Increment = 1, Value = t.FolderIndex };
            folderIdxBox.ValueChanged += (s, e) => { t.FolderIndex = (int)e.Value; };
            splitRow.Controls.Add(folderIdxBox);
            splitRow.Controls.Add(MakeLabel(I18n.T("transitFileIdx")));
            var fileIdxBox = new AntdUI.InputNumber { Width = 80, Height = 30, Minimum = 0, Maximum = 99, Increment = 1, Value = t.FileNameIndex };
            fileIdxBox.ValueChanged += (s, e) => { t.FileNameIndex = (int)e.Value; };
            splitRow.Controls.Add(fileIdxBox);
            splitRow.Controls.Add(MakeLabel(I18n.T("transitFolderSplit")));
            var folderSpliterBox = new AntdUI.Input { Width = 60, Height = 30, Text = t.FolderSpliter };
            folderSpliterBox.LostFocus += (s, e) => { if (!string.IsNullOrEmpty(folderSpliterBox.Text)) t.FolderSpliter = folderSpliterBox.Text[0].ToString(); };
            splitRow.Controls.Add(folderSpliterBox);
            splitRow.Controls.Add(MakeLabel(I18n.T("transitFileSplit")));
            var fileSpliterBox = new AntdUI.Input { Width = 60, Height = 30, Text = t.FileNameSpliter };
            fileSpliterBox.LostFocus += (s, e) => { if (!string.IsNullOrEmpty(fileSpliterBox.Text)) t.FileNameSpliter = fileSpliterBox.Text[0].ToString(); };
            splitRow.Controls.Add(fileSpliterBox);
            panel.Controls.Add(splitRow, 0, 2);

            // Row 3-6: 双 GroupBox（文件夹部件 + 文件名部件）— Lens 原版 DataGridView 编辑器
            var partsSplit = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
            partsSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            partsSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            // ===== 文件夹部件 GroupBox =====
            var dirGroup = new AntdUI.Collapse { Text = I18n.T("transitFolderParts"), Margin = new Padding(4) };
            var dirInner = new System.Windows.Forms.Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            DataGridView dirGrid = null;

            var dirAddBtn = new AntdUI.Button { Width = 64, Height = 28, Text = "+" };
            dirAddBtn.Click += (s, e) => { t.FolderPartList.Add(new TransformPart { PartType = TransformPartType.Name }); RebuildDirGrid(dirGrid); };
            var dirDelBtn = new AntdUI.Button { Width = 64, Height = 28, Text = "-", Margin = new Padding(4, 0, 0, 0) };
            dirDelBtn.Click += (s, e) => { if (dirGrid.SelectedRows.Count > 0) { t.FolderPartList.RemoveAt(dirGrid.SelectedRows[0].Index); RebuildDirGrid(dirGrid); } };
            var dirClearBtn = new AntdUI.Button { Width = 64, Height = 28, Text = "×", Margin = new Padding(4, 0, 0, 0) };
            dirClearBtn.Click += (s, e) => { t.FolderPartList.Clear(); RebuildDirGrid(dirGrid); };

            var dirBtnRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            dirBtnRow.Controls.Add(dirAddBtn); dirBtnRow.Controls.Add(dirDelBtn); dirBtnRow.Controls.Add(dirClearBtn);
            dirInner.Controls.Add(dirBtnRow);

            dirGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                Top = 36,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                BackgroundColor = ThemeManager.Bg3,
                ForeColor = ThemeManager.Fg,
                BorderStyle = BorderStyle.None,
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = ThemeManager.Bg3, ForeColor = ThemeManager.Fg },
            };
            dirGrid.Columns.Add("colIdx", "#");
            dirGrid.Columns.Add("colType", I18n.T("transitPartType"));
            dirGrid.Columns.Add("colSourceIdx", I18n.T("transitSourceIdx"));
            dirGrid.Columns.Add("colFormat", I18n.T("transitFormat"));
            dirGrid.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex < 0 || e.RowIndex >= t.FolderPartList.Count) return;
                var part = t.FolderPartList[e.RowIndex];
                if (e.ColumnIndex == 1)
                {
                    var typeName = dirGrid[e.ColumnIndex, e.RowIndex].Value?.ToString();
                    part.PartType = typeName == "PC时间" ? TransformPartType.DateTime : TransformPartType.Name;
                }
                else if (e.ColumnIndex == 2 && int.TryParse(dirGrid[e.ColumnIndex, e.RowIndex].Value?.ToString(), out var si))
                    part.SourceIndex = si;
                else if (e.ColumnIndex == 3)
                    part.Format = dirGrid[e.ColumnIndex, e.RowIndex].Value?.ToString() ?? "";
            };
            dirInner.Controls.Add(dirGrid);
            RebuildDirGrid(dirGrid);

            dirGroup.Controls.Add(dirInner);
            partsSplit.Controls.Add(dirGroup, 0, 0);

            // ===== 文件名部件 GroupBox =====
            var fileGroup = new AntdUI.Collapse { Text = I18n.T("transitFileParts"), Margin = new Padding(4) };
            var fileInner = new System.Windows.Forms.Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            DataGridView fileGrid = null;

            var fileAddBtn = new AntdUI.Button { Width = 64, Height = 28, Text = "+" };
            fileAddBtn.Click += (s, e) => { t.FileNamePartList.Add(new TransformPart { PartType = TransformPartType.Name }); RebuildFileGrid(fileGrid); };
            var fileDelBtn = new AntdUI.Button { Width = 64, Height = 28, Text = "-", Margin = new Padding(4, 0, 0, 0) };
            fileDelBtn.Click += (s, e) => { if (fileGrid.SelectedRows.Count > 0) { t.FileNamePartList.RemoveAt(fileGrid.SelectedRows[0].Index); RebuildFileGrid(fileGrid); } };
            var fileClearBtn = new AntdUI.Button { Width = 64, Height = 28, Text = "×", Margin = new Padding(4, 0, 0, 0) };
            fileClearBtn.Click += (s, e) => { t.FileNamePartList.Clear(); RebuildFileGrid(fileGrid); };

            var fileBtnRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            fileBtnRow.Controls.Add(fileAddBtn); fileBtnRow.Controls.Add(fileDelBtn); fileBtnRow.Controls.Add(fileClearBtn);
            fileInner.Controls.Add(fileBtnRow);

            fileGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                Top = 36,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                BackgroundColor = ThemeManager.Bg3,
                ForeColor = ThemeManager.Fg,
                BorderStyle = BorderStyle.None,
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = ThemeManager.Bg3, ForeColor = ThemeManager.Fg },
            };
            fileGrid.Columns.Add("colIdx", "#");
            fileGrid.Columns.Add("colType", I18n.T("transitPartType"));
            fileGrid.Columns.Add("colSourceIdx", I18n.T("transitSourceIdx"));
            fileGrid.Columns.Add("colFormat", I18n.T("transitFormat"));
            fileGrid.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex < 0 || e.RowIndex >= t.FileNamePartList.Count) return;
                var part = t.FileNamePartList[e.RowIndex];
                if (e.ColumnIndex == 1)
                {
                    var typeName = fileGrid[e.ColumnIndex, e.RowIndex].Value?.ToString();
                    part.PartType = typeName == "PC时间" ? TransformPartType.DateTime : TransformPartType.Name;
                }
                else if (e.ColumnIndex == 2 && int.TryParse(fileGrid[e.ColumnIndex, e.RowIndex].Value?.ToString(), out var si))
                    part.SourceIndex = si;
                else if (e.ColumnIndex == 3)
                    part.Format = fileGrid[e.ColumnIndex, e.RowIndex].Value?.ToString() ?? "";
            };
            fileInner.Controls.Add(fileGrid);
            RebuildFileGrid(fileGrid);

            fileGroup.Controls.Add(fileInner);
            partsSplit.Controls.Add(fileGroup, 1, 0);
            panel.Controls.Add(partsSplit, 0, 3);

            scroll.Controls.Add(panel);
            return scroll;
        }

        private static void RebuildDirGrid(DataGridView grid)
        {
            grid.Rows.Clear();
            var parts = ConfigService.Transform.FolderPartList;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                grid.Rows.Add(i, p.PartType == TransformPartType.DateTime ? "PC时间" : "名称", p.SourceIndex, p.Format);
            }
        }

        private static void RebuildFileGrid(DataGridView grid)
        {
            grid.Rows.Clear();
            var parts = ConfigService.Transform.FileNamePartList;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                grid.Rows.Add(i, p.PartType == TransformPartType.DateTime ? "PC时间" : "名称", p.SourceIndex, p.Format);
            }
        }

        // ---------- 存图清理（复刻 CameraHelperLens：所有字段瞬时改内存，Apply 按钮一次性保存+重启 Cleaner） ----------
        private Control BuildCleanPage()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
            for (int r = 0; r < 8; r++) panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var c = ConfigService.Clean;

            // Row 0: 启用 + Apply + CleanOnce
            var topRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
            var enableCheck = new AntdUI.Checkbox { Text = I18n.T("cleanupEnable"), Checked = c.IsEnable, AutoSizeMode = TAutoSize.Auto };
            enableCheck.CheckedChanged += (s, e) => c.IsEnable = e.Value;
            topRow.Controls.Add(enableCheck);

            var applyBtn = new AntdUI.Button { Width = 110, Height = 30, Text = I18n.T("apply"), Margin = new Padding(16, 0, 0, 0) };
            applyBtn.Click += (s, e) =>
            {
                ConfigService.SaveClean();
                App.Cleaner?.UpdateConfig(c);
            };
            topRow.Controls.Add(applyBtn);

            var nowBtn = new AntdUI.Button { Width = 130, Height = 30, Text = I18n.T("cleanupNow"), Margin = new Padding(8, 0, 0, 0) };
            nowBtn.Click += (s, e) => App.Cleaner?.CleanOnce();
            topRow.Controls.Add(nowBtn);
            panel.Controls.Add(topRow, 0, 0);

            // Row 1-7: 各设置字段（瞬时改内存，不 Save — Lens 原版模式）
            var folderRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
            folderRow.Controls.Add(MakeLabel(I18n.T("cleanupFolder")));
            var folderBox = new AntdUI.Input { Width = 480, Height = 30, Text = c.FolderPath };
            folderBox.LostFocus += (s, e) => c.FolderPath = folderBox.Text.Trim();
            folderRow.Controls.Add(folderBox);
            var folderBrowseBtn = new AntdUI.Button { Width = 78, Height = 30, Text = I18n.T("select"), Margin = new Padding(8, 0, 0, 0) };
            folderBrowseBtn.Click += (s, e) =>
            {
                using var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = I18n.T("cleanupFolder") };
                if (dlg.ShowDialog(this) == DialogResult.OK) folderBox.Text = dlg.SelectedPath;
            };
            folderRow.Controls.Add(folderBrowseBtn);
            panel.Controls.Add(folderRow, 0, 1);

            var intervalRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
            intervalRow.Controls.Add(MakeLabel(I18n.T("cleanupInterval")));
            var intervalBox = new AntdUI.InputNumber { Width = 100, Height = 30, Minimum = (decimal)0.1, Maximum = 1000, Increment = (decimal)0.5, DecimalPlaces = 1, Value = (decimal)c.ScanInterval };
            intervalBox.ValueChanged += (s, e) => c.ScanInterval = (double)e.Value;
            intervalRow.Controls.Add(intervalBox);
            panel.Controls.Add(intervalRow, 0, 2);

            var emptyRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
            var emptyCheck = new AntdUI.Checkbox { Text = I18n.T("cleanupEmpty"), Checked = c.IsCleanEmptyFolders, AutoSizeMode = TAutoSize.Auto };
            emptyCheck.CheckedChanged += (s, e) => c.IsCleanEmptyFolders = e.Value;
            emptyRow.Controls.Add(emptyCheck);
            panel.Controls.Add(emptyRow, 0, 3);

            var daysRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
            var daysCheck = new AntdUI.Checkbox { Text = I18n.T("cleanupHoldDaysEn"), Checked = c.IsEnableHoldDays, Margin = new Padding(0, 4, 0, 0), AutoSizeMode = TAutoSize.Auto };
            daysCheck.CheckedChanged += (s, e) => c.IsEnableHoldDays = e.Value;
            daysRow.Controls.Add(daysCheck);
            daysRow.Controls.Add(MakeLabel(I18n.T("cleanupHoldDays")));
            var daysBox = new AntdUI.InputNumber { Width = 100, Height = 30, Minimum = 0, Maximum = 99999, Increment = 1, Value = c.HoldDays };
            daysBox.ValueChanged += (s, e) => c.HoldDays = (int)e.Value;
            daysRow.Controls.Add(daysBox);
            panel.Controls.Add(daysRow, 0, 4);

            var freeRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
            var freeCheck = new AntdUI.Checkbox { Text = I18n.T("cleanupFreeEn"), Checked = c.IsEnableRemainingSpace, Margin = new Padding(0, 4, 0, 0), AutoSizeMode = TAutoSize.Auto };
            freeCheck.CheckedChanged += (s, e) => c.IsEnableRemainingSpace = e.Value;
            freeRow.Controls.Add(freeCheck);
            freeRow.Controls.Add(MakeLabel(I18n.T("cleanupFreeGB")));
            var freeBox = new AntdUI.InputNumber { Width = 100, Height = 30, Minimum = (decimal)0.1, Maximum = 10000, Increment = (decimal)0.5, DecimalPlaces = 1, Value = (decimal)c.RemainingSpace };
            freeBox.ValueChanged += (s, e) => c.RemainingSpace = (double)e.Value;
            freeRow.Controls.Add(freeBox);
            freeRow.Controls.Add(MakeLabel(I18n.T("cleanupDeleteMs")));
            var delBox = new AntdUI.InputNumber { Width = 100, Height = 30, Minimum = 0, Maximum = 10000, Increment = 1, Value = c.DeleteInterval };
            delBox.ValueChanged += (s, e) => c.DeleteInterval = (int)e.Value;
            freeRow.Controls.Add(delBox);
            panel.Controls.Add(freeRow, 0, 5);

            langUpdaters.Add(() =>
            {
                enableCheck.Text = I18n.T("cleanupEnable");
                ((AntdUI.Label)folderRow.Controls[0]).Text = I18n.T("cleanupFolder");
                folderBrowseBtn.Text = I18n.T("select");
                ((AntdUI.Label)intervalRow.Controls[0]).Text = I18n.T("cleanupInterval");
                daysCheck.Text = I18n.T("cleanupHoldDaysEn");
                ((AntdUI.Label)daysRow.Controls[1]).Text = I18n.T("cleanupHoldDays");
                freeCheck.Text = I18n.T("cleanupFreeEn");
                ((AntdUI.Label)freeRow.Controls[1]).Text = I18n.T("cleanupFreeGB");
                ((AntdUI.Label)freeRow.Controls[3]).Text = I18n.T("cleanupDeleteMs");
                emptyCheck.Text = I18n.T("cleanupEmpty");
                nowBtn.Text = I18n.T("cleanupNow");
            });
            return panel;
        }

        // ---------- JOBX 备份 ----------
        private Control BuildJobxPage()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 235));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };

            jobxAddBtn = new AntdUI.Button { Width = 92, Height = 30, Text = I18n.T("addCamera"), DefaultBorderColor = ThemeManager.BtnBorder, BorderWidth = 1 };
            jobxAddBtn.Click += (s, e) =>
            {
                ConfigService.JobxBackup.cameras.Add(new JobxCameraConfig());
                ConfigService.SaveJobxBackup();
                RebuildJobxTable();
            };
            btns.Controls.Add(jobxAddBtn);

            jobxBackupBtn = new AntdUI.Button { Width = 78, Height = 30, Text = I18n.T("backup"), Margin = new Padding(8, 0, 0, 0), DefaultBorderColor = ThemeManager.BtnBorder, BorderWidth = 1 };
            jobxBackupBtn.Click += (s, e) =>
            {
                var row = (jobxGrid?.SelectedIndex ?? -1) - 1; // AntdUI SelectedIndex 为 1-based
                var r = row >= 0 && row < jobxRows.Count ? jobxRows[row] : null;
                if (r == null)
                {
                    JobxBackupService.AddLog("WARN", I18n.T("jobxSelectCamera"));
                    return;
                }
                var cam = r.src;
                System.Threading.Tasks.Task.Run(() =>
                {
                    try { JobxBackupService.BackupCamera(cam); }
                    catch (Exception ex) { JobxBackupService.AddLog("ERROR", ex.Message); }
                });
            };
            btns.Controls.Add(jobxBackupBtn);

            jobxBackupAllBtn = new AntdUI.Button { Width = 92, Height = 30, Text = I18n.T("backupAll"), Margin = new Padding(8, 0, 0, 0), DefaultBorderColor = ThemeManager.BtnBorder, BorderWidth = 1 };
            jobxBackupAllBtn.Click += (s, e) => JobxBackupService.BackupAll();
            btns.Controls.Add(jobxBackupAllBtn);

            jobxOpenDirBtn = new AntdUI.Button { Width = 92, Height = 30, Text = I18n.T("openDir"), Margin = new Padding(8, 0, 0, 0), DefaultBorderColor = ThemeManager.BtnBorder, BorderWidth = 1 };
            jobxOpenDirBtn.Click += (s, e) =>
            {
                string dir = null;
                var row = (jobxGrid?.SelectedIndex ?? -1) - 1; // AntdUI SelectedIndex 为 1-based
                if (row >= 0 && row < jobxRows.Count && !string.IsNullOrWhiteSpace(jobxRows[row].src.backup_directory))
                    dir = jobxRows[row].src.backup_directory;
                JobxBackupService.OpenBackupDirectory(dir);
            };
            btns.Controls.Add(jobxOpenDirBtn);
            panel.Controls.Add(btns, 0, 0);

            jobxHost = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
            panel.Controls.Add(jobxHost, 0, 1);

            var logLabel = MakeLabel(I18n.T("log"), true);
            logLabel.Margin = new Padding(0, 0, 0, 4);
            panel.Controls.Add(logLabel, 0, 2);

            jobxLog = new TextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9f),
                BackColor = ThemeManager.Bg3,
                ForeColor = ThemeManager.Fg,
                BorderStyle = BorderStyle.FixedSingle,
            };
            panel.Controls.Add(jobxLog, 0, 3);

            jobxLogTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            jobxLogTimer.Tick += (s, e) => RefreshJobxLog();
            jobxLogTimer.Start();
            RefreshJobxLog();

            langUpdaters.Add(UpdateJobxTexts);
            UpdateJobxTexts();
            RebuildJobxTable();
            return panel;
        }

        private void UpdateJobxTexts()
        {
            jobxAddBtn.Text = I18n.T("addCamera");
            jobxBackupBtn.Text = I18n.T("backup");
            jobxBackupAllBtn.Text = I18n.T("backupAll");
            jobxOpenDirBtn.Text = I18n.T("openDir");
        }

        // AntdUI.Table 数据绑定要求 public 类 + public 属性（字段/private 类读不到）
        public class JobxRow
        {
            public JobxCameraConfig src { get; set; }
            public string name { get; set; }
            public string ip { get; set; }
            public string port { get; set; }
            public string username { get; set; }
            public string password { get; set; }
            public string backupDir { get; set; }
            public bool ftps { get; set; }
            public bool trust { get; set; }
            public CellButton select { get; set; }
            public CellButton delete { get; set; }

            public void SyncFromSrc()
            {
                name = src.name;
                ip = src.ip;
                port = src.ftp_port.ToString();
                username = src.ftp_username;
                password = src.ftp_password;
                backupDir = src.backup_directory;
                ftps = src.ftps_enabled;
                trust = src.trust_all_certs;
            }
        }

        private void RebuildJobxTable()
        {
            if (jobxHost == null) return;
            jobxRows.Clear();
            foreach (var cam in ConfigService.JobxBackup.cameras)
            {
                var row = new JobxRow { src = cam };
                row.SyncFromSrc();
                row.select = new CellButton("select", I18n.T("select"));
                row.delete = new CellButton("delete", I18n.T("delete"));
                jobxRows.Add(row);
            }

            var columns = new ColumnCollection
            {
                new Column("name", I18n.T("name")) { Editable = true, Width = "110" },
                new Column("ip", I18n.T("ip")) { Editable = true, Width = "100" },
                new Column("port", I18n.T("port")) { Editable = true, Width = "55" },
                new Column("username", I18n.T("username")) { Editable = true, Width = "80" },
                new Column("password", I18n.T("password")) { Editable = true, Width = "80" },
                new Column("backupDir", I18n.T("backupDir")) { Editable = true, Width = "130" },
                new Column("select", I18n.T("select")) { Width = "64" },
                new ColumnCheck("ftps", I18n.T("ftps")) { Width = "55" },
                new ColumnCheck("trust", I18n.T("trustCerts")) { Width = "70" },
                new Column("delete", "") { Width = "60" },
            };

            var table = new AntdUI.Table
            {
                Dock = DockStyle.Fill,
                Columns = columns,
                DataSource = jobxRows,
                Bordered = true,
                EmptyHeader = true,
                EditMode = TEditMode.DoubleClick,
                RowHeight = 32,
            };
            table.CellEndEdit += JobxCellEndEdit;
            table.CheckedChanged += JobxCheckedChanged;
            table.CellButtonClick += JobxCellButtonClick;

            jobxGrid = table;
            jobxHost.Controls.Clear();
            jobxHost.Controls.Add(table);
        }

        private bool JobxCellEndEdit(object s, TableEndEditEventArgs e)
        {
            var row = e.RowIndex - 1; // AntdUI 行索引为 1-based（0 是表头）
            if (row < 0 || row >= jobxRows.Count) return false;
            var r = jobxRows[row];
            var key = e.Column?.Key;
            switch (key)
            {
                case "name": r.src.name = e.Value; break;
                case "ip": r.src.ip = e.Value; break;
                case "port":
                    if (int.TryParse(e.Value, out var p) && p > 0 && p < 65536) r.src.ftp_port = p;
                    else r.SyncFromSrc();
                    break;
                case "username": r.src.ftp_username = e.Value; break;
                case "password": r.src.ftp_password = e.Value; break;
                case "backupDir": r.src.backup_directory = e.Value; break;
                default: return false;
            }
            r.SyncFromSrc();
            ConfigService.SaveJobxBackup();
            return false;
        }

        private void JobxCheckedChanged(object s, TableCheckEventArgs e)
        {
            var row = e.RowIndex - 1; // AntdUI 行索引为 1-based（0 是表头）
            if (row < 0 || row >= jobxRows.Count) return;
            var r = jobxRows[row];
            var key = e.Column?.Key;
            if (key == "ftps") { r.src.ftps_enabled = e.Value; r.ftps = e.Value; }
            else if (key == "trust") { r.src.trust_all_certs = e.Value; r.trust = e.Value; }
            else return;
            ConfigService.SaveJobxBackup();
        }

        private void JobxCellButtonClick(object s, TableButtonEventArgs e)
        {
            // AntdUI 行索引为 1-based（0 是表头）；优先用 Record 匹配
            var r = e.Record as JobxRow ?? (e.RowIndex >= 1 && e.RowIndex <= jobxRows.Count ? jobxRows[e.RowIndex - 1] : null);
            if (r == null) return;
            var id = e.Btn?.Id;
            if (id == "select")
            {
                try
                {
                    using var dlg = new System.Windows.Forms.FolderBrowserDialog
                    {
                        Description = I18n.T("selectBackupDir"),
                        ShowNewFolderButton = true,
                    };
                    if (!string.IsNullOrWhiteSpace(r.src.backup_directory) && System.IO.Directory.Exists(r.src.backup_directory))
                        dlg.SelectedPath = r.src.backup_directory;
                    if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                    {
                        r.src.backup_directory = dlg.SelectedPath;
                        r.backupDir = dlg.SelectedPath;
                        ConfigService.SaveJobxBackup();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, I18n.T("error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else if (id == "delete")
            {
                ConfigService.JobxBackup.cameras.Remove(r.src);
                ConfigService.SaveJobxBackup();
                RebuildJobxTable();
            }
        }

        private void RefreshJobxLog()
        {
            if (jobxLog == null || jobxLog.IsDisposed) return;
            var logs = JobxBackupService.GetLogs();
            var sb = new System.Text.StringBuilder();
            foreach (var entry in logs)
                sb.Append('[').Append(entry.timestamp).Append("] [").Append(entry.level).Append("] ").AppendLine(entry.message);
            jobxLog.Text = sb.ToString();
            jobxLog.SelectionStart = jobxLog.TextLength;
            jobxLog.ScrollToCaret();
        }
    }
}
