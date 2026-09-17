using System;
using System.Drawing;
using System.Windows.Forms;
using AntdUI;
using CameraViewer;
using Panel = System.Windows.Forms.Panel;

namespace CameraViewerDotnet
{
    public class CameraCell : Panel
    {
        private readonly int id;
        private readonly AntdUI.Input urlBox;
        private readonly AntdUI.Input remarkBox;
        private readonly AntdUI.Button lockBtn;
        private readonly AntdUI.Button refreshBtn;
        private readonly AntdUI.Button maxBtn;
        private readonly Panel top;
        private readonly Microsoft.Web.WebView2.WinForms.WebView2 webView;
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

            urlBox = new AntdUI.Input
            {
                Dock = DockStyle.Fill,
                Text = item.ip,
                ReadOnly = item.locked,
                PlaceholderText = I18n.T("urlPlaceholder"),
            };
            urlBox.LostFocus += (s, e) => CommitUrl();
            urlBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { CommitUrl(); e.SuppressKeyPress = true; }
            };
            topGrid.Controls.Add(urlBox, 0, 0);

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

            lockBtn = new AntdUI.Button { Text = "", IconSvg = "", Width = 78, Height = 26, Margin = new Padding(4, 0, 0, 0) };
            lockBtn.Click += (s, e) => ToggleLock();
            topGrid.Controls.Add(lockBtn, 2, 0);

            refreshBtn = new AntdUI.Button { Text = I18n.T("refresh"), IconSvg = AntIcon.Svg(AntIcon.Reload), Width = 78, Height = 26, Margin = new Padding(4, 0, 0, 0) };
            refreshBtn.Click += async (s, e) => await ReloadAsync();
            topGrid.Controls.Add(refreshBtn, 3, 0);

            maxBtn = new AntdUI.Button { Text = "", IconSvg = "", Width = 78, Height = 26, Margin = new Padding(4, 0, 0, 0) };
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

            // Dock 顺序：Fill 先加入，顶部栏后加入
            root.Controls.Add(webView, 0, 1);
            root.Controls.Add(top, 0, 0);
            Controls.Add(root);

            I18n.LanguageChanged += OnLanguageChanged;
            ThemeManager.ThemeChanged += ApplyTheme;
            Disposed += (s, e) =>
            {
                I18n.LanguageChanged -= OnLanguageChanged;
                ThemeManager.ThemeChanged -= ApplyTheme;
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
            urlBox.PlaceholderText = I18n.T("urlPlaceholder");
            remarkBox.PlaceholderText = I18n.T("remarkPlaceholder");
            PostHmiLang();
        }

        public void ApplyTheme()
        {
            BackColor = ThemeManager.Bg2;
            top.BackColor = ThemeManager.Bg2;
        }

        private void CommitUrl()
        {
            var item = ConfigService.EnsureItem(id);
            item.ip = urlBox.Text.Trim();
            ConfigService.SaveCamera();
            if (webViewReady && item.locked)
                Navigate();
        }

        private void ToggleLock()
        {
            var item = ConfigService.EnsureItem(id);
            item.locked = !item.locked;
            urlBox.ReadOnly = item.locked;
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
    }
}
