using System;
using System.Drawing;
using System.Windows.Forms;
using AntdUI;
using CameraViewer;
using Panel = System.Windows.Forms.Panel;

namespace CameraViewerGigE
{
    public class CameraCell : Panel
    {
        private readonly int id;
        private readonly bool isGige;
        /// <summary>http：网页地址；gige：相机序列号。</summary>
        private readonly AntdUI.Input mainBox;
        private readonly AntdUI.Input remarkBox;
        private readonly AntdUI.Button lockBtn;
        private readonly AntdUI.Button refreshBtn;
        private readonly AntdUI.Button maxBtn;
        private readonly Panel top;
        private readonly Microsoft.Web.WebView2.WinForms.WebView2 webView;
        private readonly System.Windows.Forms.PictureBox pictureBox;
        private readonly System.Windows.Forms.Label gigeStatus;
        private readonly System.Windows.Forms.Timer frameTimer;
        private GigECamera gigeCamera;
        private string statusKey;
        private bool webViewReady;
        private bool isMaximized;
        private static string hmiI18nScript;

        public event Action<CameraCell> ToggleMaximize;

        private static string LoadHmiI18nScript()
        {
            if (hmiI18nScript != null) return hmiI18nScript;
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (name.EndsWith("hmi-i18n.js"))
                    {
                        using (var s = asm.GetManifestResourceStream(name))
                        using (var r = new System.IO.StreamReader(s, System.Text.Encoding.UTF8))
                            hmiI18nScript = r.ReadToEnd();
                        return hmiI18nScript;
                    }
                }
            }
            catch { }
            hmiI18nScript = "";
            return hmiI18nScript;
        }

        private void PostHmiLang()
        {
            if (!webViewReady || webView.CoreWebView2 == null) return;
            try
            {
                webView.CoreWebView2.PostWebMessageAsJson(
                    "{\"__hmiI18n\":\"lang\",\"lang\":\"" + I18n.Language + "\"}");
            }
            catch { }
        }

        private void OnWebMessageReceived(object sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var json = e.WebMessageAsJson;
                if (json != null && json.Contains("__hmiI18n") && json.Contains("ready"))
                    PostHmiLang();
            }
            catch { }
        }

        public CameraCell(int id)
        {
            this.id = id;
            var item = ConfigService.EnsureItem(id);
            item.conn = ConfigService.NormalizeConn(item.conn);
            isGige = item.conn == "gige";

            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            BackColor = ThemeManager.Bg2;
            Padding = new Padding(1);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = ThemeManager.Bg2 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            top = new Panel { Dock = DockStyle.Fill, Height = 30, BackColor = ThemeManager.Bg2 };
            var topGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, BackColor = ThemeManager.Bg2, Margin = new Padding(3, 3, 3, 1) };
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            mainBox = new AntdUI.Input
            {
                Dock = DockStyle.Fill,
                Text = isGige ? item.serial : item.ip,
                ReadOnly = item.locked,
                PlaceholderText = isGige ? I18n.T("serialPlaceholder") : I18n.T("urlPlaceholder"),
            };
            mainBox.LostFocus += (s, e) => CommitMain();
            mainBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { CommitMain(); e.SuppressKeyPress = true; }
            };
            topGrid.Controls.Add(mainBox, 0, 0);

            remarkBox = new AntdUI.Input
            {
                Dock = DockStyle.Fill,
                Text = item.remark,
                ReadOnly = item.locked,
                PlaceholderText = I18n.T("remarkPlaceholder"),
                Margin = new Padding(4, 0, 0, 0),
            };
            remarkBox.LostFocus += (s, e) =>
            {
                var it = ConfigService.EnsureItem(id);
                it.remark = remarkBox.Text;
                ConfigService.SaveCamera();
            };
            topGrid.Controls.Add(remarkBox, 1, 0);

            lockBtn = new AntdUI.Button { Text = "", IconSvg = "", Width = 78, Height = 26, Margin = new Padding(4, 0, 0, 0), DefaultBorderColor = ThemeManager.BtnBorder, BorderWidth = 1 };
            lockBtn.Click += (s, e) => ToggleLock();
            topGrid.Controls.Add(lockBtn, 2, 0);

            refreshBtn = new AntdUI.Button { Text = I18n.T("refresh"), IconSvg = AntIcon.Svg(AntIcon.Reload), Width = 78, Height = 26, Margin = new Padding(4, 0, 0, 0), DefaultBorderColor = ThemeManager.BtnBorder, BorderWidth = 1 };
            refreshBtn.Click += async (s, e) => await ReloadAsync();
            topGrid.Controls.Add(refreshBtn, 3, 0);

            maxBtn = new AntdUI.Button { Text = "", IconSvg = "", Width = 78, Height = 26, Margin = new Padding(4, 0, 0, 0), DefaultBorderColor = ThemeManager.BtnBorder, BorderWidth = 1 };
            maxBtn.Click += (s, e) =>
            {
                isMaximized = !isMaximized;
                UpdateMaxBtn();
                ToggleMaximize?.Invoke(this);
            };
            topGrid.Controls.Add(maxBtn, 4, 0);
            UpdateLockBtn();
            UpdateMaxBtn();

            top.Controls.Add(topGrid);

            if (isGige)
            {
                // GigE：PictureBox 显示实时画面，状态 Label 覆盖在其上
                pictureBox = new System.Windows.Forms.PictureBox
                {
                    Dock = DockStyle.Fill,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = ThemeManager.Bg,
                };
                gigeStatus = new System.Windows.Forms.Label
                {
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = ThemeManager.FgDim,
                    BackColor = ThemeManager.Bg,
                    Text = I18n.T("gigeConnecting"),
                };
                statusKey = "gigeConnecting";
                root.Controls.Add(pictureBox, 0, 1);
                root.Controls.Add(gigeStatus, 0, 1);

                frameTimer = new System.Windows.Forms.Timer { Interval = 100 };
                frameTimer.Tick += (s, e) => PumpFrame();
                frameTimer.Start();
            }
            else
            {
                webView = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill };
                webView.CoreWebView2InitializationCompleted += async (s, e) =>
                {
                    if (e.IsSuccess)
                    {
                        webViewReady = true;
                        webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                        var script = LoadHmiI18nScript();
                        if (!string.IsNullOrEmpty(script))
                        {
                            try { await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script); } catch { }
                        }
                    }
                    Navigate();
                };
                root.Controls.Add(webView, 0, 1);
            }

            // Dock 顺序：Fill 先加入，顶部栏后加入
            root.Controls.Add(top, 0, 0);
            Controls.Add(root);

            I18n.LanguageChanged += OnLanguageChanged;
            ThemeManager.ThemeChanged += ApplyTheme;
            Disposed += (s, e) =>
            {
                I18n.LanguageChanged -= OnLanguageChanged;
                ThemeManager.ThemeChanged -= ApplyTheme;
                frameTimer?.Stop();
                frameTimer?.Dispose();
                StopGige();
            };
        }

        private void UpdateLockBtn()
        {
            var locked = ConfigService.EnsureItem(id).locked;
            lockBtn.Text = locked ? I18n.T("unlock") : I18n.T("lock");
            lockBtn.IconSvg = AntIcon.Svg(locked ? AntIcon.Lock : AntIcon.Unlock);
        }

        private void UpdateMaxBtn()
        {
            maxBtn.Text = isMaximized ? I18n.T("restore") : I18n.T("maximize");
            maxBtn.IconSvg = AntIcon.Svg(isMaximized ? AntIcon.Shrink : AntIcon.Expand);
        }

        private void OnLanguageChanged()
        {
            UpdateLockBtn();
            UpdateMaxBtn();
            refreshBtn.Text = I18n.T("refresh");
            refreshBtn.IconSvg = AntIcon.Svg(AntIcon.Reload);
            mainBox.PlaceholderText = isGige ? I18n.T("serialPlaceholder") : I18n.T("urlPlaceholder");
            remarkBox.PlaceholderText = I18n.T("remarkPlaceholder");
            if (gigeStatus != null && statusKey != null) gigeStatus.Text = I18n.T(statusKey);
            PostHmiLang();
        }

        public void ApplyTheme()
        {
            BackColor = ThemeManager.Bg2;
            top.BackColor = ThemeManager.Bg2;
            if (pictureBox != null) pictureBox.BackColor = ThemeManager.Bg;
            if (gigeStatus != null)
            {
                gigeStatus.BackColor = ThemeManager.Bg;
                gigeStatus.ForeColor = ThemeManager.FgDim;
            }
        }

        private void CommitMain()
        {
            var item = ConfigService.EnsureItem(id);
            if (isGige) item.serial = mainBox.Text.Trim();
            else item.ip = mainBox.Text.Trim();
            ConfigService.SaveCamera();
            if (!item.locked) return;
            if (isGige) ReloadGige();
            else if (webViewReady) Navigate();
        }

        private void ToggleLock()
        {
            var item = ConfigService.EnsureItem(id);
            item.locked = !item.locked;
            mainBox.ReadOnly = item.locked;
            remarkBox.ReadOnly = item.locked;
            UpdateLockBtn();
            ConfigService.SaveCamera();
        }

        private void Navigate()
        {
            var item = ConfigService.GetItem(id);
            if (item == null) return;
            var url = (item.ip ?? "").Trim();
            if (url.Length == 0) return;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = "http://" + url;
            try { webView.CoreWebView2?.Navigate(url); } catch { }
        }

        public async System.Threading.Tasks.Task ReloadAsync()
        {
            if (IsDisposed || Disposing) return;
            if (isGige)
            {
                ReloadGige();
                return;
            }
            if (!webViewReady)
            {
                try { await webView.EnsureCoreWebView2Async(null); }
                catch { return; }
            }
            else
            {
                try { webView.Reload(); return; } catch { }
            }
            Navigate();
        }

        // ---------- GigE 实时画面 ----------

        private void ReloadGige()
        {
            StopGige();
            var item = ConfigService.GetItem(id);
            if (item == null) return;
            if (string.IsNullOrWhiteSpace(item.serial))
            {
                ShowStatus(I18n.T("gigeNoSerial"), "gigeNoSerial");
                return;
            }
            ShowStatus(I18n.T("gigeConnecting"), "gigeConnecting");
            var cam = new GigECamera(item.serial, item.exposureMs);
            cam.Failed += OnGigeFailed;
            gigeCamera = cam;
            cam.Start();
        }

        private void StopGige()
        {
            var cam = gigeCamera;
            gigeCamera = null;
            if (cam != null)
            {
                cam.Failed -= OnGigeFailed;
                cam.Dispose();
            }
            SwapImage(null);
        }

        private void OnGigeFailed(string message)
        {
            try
            {
                if (IsDisposed || Disposing || !IsHandleCreated) return;
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed || Disposing || gigeStatus == null) return;
                    ShowStatus(message, null);
                }));
            }
            catch { }
        }

        private void PumpFrame()
        {
            if (gigeCamera == null || IsDisposed || Disposing) return;
            var bmp = gigeCamera.TakeLatest();
            if (bmp == null) return;
            SwapImage(bmp);
            if (gigeStatus.Visible)
            {
                gigeStatus.Visible = false;
                statusKey = null;
            }
        }

        private void SwapImage(Bitmap bmp)
        {
            if (pictureBox == null) return;
            var old = pictureBox.Image;
            pictureBox.Image = bmp;
            old?.Dispose();
        }

        private void ShowStatus(string text, string key)
        {
            if (gigeStatus == null) return;
            statusKey = key;
            gigeStatus.Text = text;
            gigeStatus.Visible = true;
            gigeStatus.BringToFront();
        }
    }
}
