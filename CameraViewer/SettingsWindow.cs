using System;
using System.Collections.Generic;
using System.Drawing;
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
            tabs.Pages.Add(MakePage(I18n.T("appSettings"), BuildLanguagePage()));
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
            var names = new[] { I18n.T("cameraDisplay"), I18n.T("cameraSettings"), I18n.T("displaySettings"), I18n.T("appSettings"), I18n.T("jobxBackup") };
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
            var counts = new[] { 1, 2, 4, 6, 9, 12, 16 };
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
            var panel = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, 4, 8, 4) };

            for (int i = 0; i < ConfigService.Camera.count; i++)
            {
                var item = ConfigService.EnsureItem(i);
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
                for (int r = 0; r < 4; r++) card.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                var header = MakeLabel(I18n.T("camera") + " " + (i + 1), true);
                card.Controls.Add(header, 0, 0);

                var urlRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
                var urlLabel = MakeLabel(I18n.T("url"));
                urlRow.Controls.Add(urlLabel);
                var urlBox = new AntdUI.Input { Width = 380, Height = 30, Text = item.ip, ReadOnly = item.locked };
                urlBox.LostFocus += (s, e) =>
                {
                    item.ip = urlBox.Text;
                    ConfigService.SaveCamera();
                };
                urlRow.Controls.Add(urlBox);
                card.Controls.Add(urlRow, 0, 1);

                var remarkRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
                var remarkLabel = MakeLabel(I18n.T("remark"));
                remarkRow.Controls.Add(remarkLabel);
                var remarkBox = new AntdUI.Input { Width = 380, Height = 30, Text = item.remark, ReadOnly = item.locked };
                remarkBox.LostFocus += (s, e) =>
                {
                    item.remark = remarkBox.Text;
                    ConfigService.SaveCamera();
                };
                remarkRow.Controls.Add(remarkBox);
                card.Controls.Add(remarkRow, 0, 2);

                var lockCheck = new AntdUI.Checkbox { Text = I18n.T("locked"), Checked = item.locked, Margin = new Padding(0, 4, 0, 2) };
                lockCheck.CheckedChanged += (s, e) =>
                {
                    item.locked = e.Value;
                    urlBox.ReadOnly = e.Value;
                    remarkBox.ReadOnly = e.Value;
                    ConfigService.SaveCamera();
                };
                card.Controls.Add(lockCheck, 0, 3);

                panel.Controls.Add(card, 0, i);

                int idx = i;
                langUpdaters.Add(() =>
                {
                    header.Text = I18n.T("camera") + " " + (idx + 1);
                    urlLabel.Text = I18n.T("url");
                    remarkLabel.Text = I18n.T("remark");
                    lockCheck.Text = I18n.T("locked");
                });
            }
            return panel;
        }

        // ---------- 显示设置 ----------
        private Control BuildThemePage()
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
            var themeLabel = MakeLabel(I18n.T("themeSetting"), true);
            panel.Controls.Add(themeLabel);

            var dark = new AntdUI.Radio { Text = I18n.T("themeDark"), Checked = ThemeManager.Theme == "dark", AutoSizeMode = TAutoSize.Auto, Margin = new Padding(0, 8, 0, 4) };
            dark.CheckedChanged += (s, e) => { if (e.Value) ApplyThemeChoice("dark"); };
            var light = new AntdUI.Radio { Text = I18n.T("themeLight"), Checked = ThemeManager.Theme != "dark", AutoSizeMode = TAutoSize.Auto, Margin = new Padding(0, 0, 0, 4) };
            light.CheckedChanged += (s, e) => { if (e.Value) ApplyThemeChoice("light"); };

            panel.Controls.Add(dark);
            panel.Controls.Add(light);
            langUpdaters.Add(() =>
            {
                themeLabel.Text = I18n.T("themeSetting");
                dark.Text = I18n.T("themeDark");
                light.Text = I18n.T("themeLight");
            });
            return panel;
        }

        private void ApplyThemeChoice(string theme)
        {
            ThemeManager.Apply(theme);
            ConfigService.App.theme = theme;
            ConfigService.SaveApp();
        }

        // ---------- 软件设置 ----------
        private Control BuildLanguagePage()
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
            var langLabel = MakeLabel(I18n.T("languageSetting"), true);
            panel.Controls.Add(langLabel);

            var zh = new AntdUI.Radio { Text = I18n.T("languageZh"), Checked = I18n.Language == "zh", AutoSizeMode = TAutoSize.Auto, Margin = new Padding(0, 8, 0, 4) };
            zh.CheckedChanged += (s, e) => { if (e.Value) ApplyLanguageChoice("zh"); };
            var en = new AntdUI.Radio { Text = I18n.T("languageEn"), Checked = I18n.Language == "en", AutoSizeMode = TAutoSize.Auto, Margin = new Padding(0, 0, 0, 4) };
            en.CheckedChanged += (s, e) => { if (e.Value) ApplyLanguageChoice("en"); };

            panel.Controls.Add(zh);
            panel.Controls.Add(en);
            langUpdaters.Add(() =>
            {
                langLabel.Text = I18n.T("languageSetting");
                zh.Text = I18n.T("languageZh");
                en.Text = I18n.T("languageEn");
            });
            return panel;
        }

        private void ApplyLanguageChoice(string lang)
        {
            I18n.SetLanguage(lang);
            ConfigService.App.language = lang;
            ConfigService.SaveApp();
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
