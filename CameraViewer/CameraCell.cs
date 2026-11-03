using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AntdUI;
using CameraViewer;
using Panel = System.Windows.Forms.Panel;

namespace CameraViewerDotnet
{
    public class CameraCell : Panel
    {
        private readonly int _id;
        private readonly AntdUI.Input _urlBox;
        private readonly AntdUI.Input _remarkBox;
        private readonly AntdUI.Button _lockBtn;
        private readonly AntdUI.Button _refreshBtn;
        private readonly AntdUI.Button _maxBtn;
        private readonly Panel _top;

        // WebView2 path: http / gige / cogsocket-subGrid==1
        private readonly Microsoft.Web.WebView2.WinForms.WebView2 _webView;
        private bool _webViewReady;

        // Sub-grid path: cogsocket + subGridCount > 1
        private readonly bool _isCogSocket;
        private readonly bool _useSubGrid;
        private readonly int _subGridCount;
        private readonly string _identifierCell;                      // 拍照标识单元格（如 A21）
        private readonly List<CellBinding> _bindings;                 // 标识取值 → 槽位
        private readonly Dictionary<string, int> _valueToSlot = new Dictionary<string, int>();
        private readonly Dictionary<int, PictureBox> _slotBoxes = new Dictionary<int, PictureBox>();
        private int _firstSlot = -1;   // 最小绑定槽位：该槽位拍照到达时表示新一轮开始，先清空所有子网格
        private readonly TableLayoutPanel _subGridPanel;
        private Panel _placeholderHost;
        private System.Windows.Forms.Label _placeholderLabel;
        private CogSocketSubscription _cogSub;   // 共享连接订阅（同一相机多 Cell 复用一条 WebSocket）

        private bool _isMaximized;
        private static string _hmiI18nScript;

        public event Action<CameraCell> ToggleMaximize;

        public CameraCell(int id)
        {
            _id = id;
            var item = ConfigService.EnsureItem(id);
            var conn = ConfigService.NormalizeConn(item.conn);

            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            BackColor = ThemeManager.Bg2;
            Padding = new Padding(1);

            // Decide rendering path
            _isCogSocket = conn == "cogsocket";
            _identifierCell = (item.identifierCell ?? "").Trim();
            _bindings = (item.cellBindings ?? new List<CellBinding>())
                .Where(b => b != null && !string.IsNullOrWhiteSpace(b.value))
                .OrderBy(b => b.slotIndex)
                .ToList();
            _useSubGrid = conn == "cogsocket" && item.subGridCount > 1 && _bindings.Count > 0 && _identifierCell.Length > 0;
            _subGridCount = _useSubGrid ? Math.Clamp(item.subGridCount, 1, 8) : 1;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = ThemeManager.Bg2 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            // ---------- Top bar ----------
            _top = new Panel { Dock = DockStyle.Fill, Height = 30, BackColor = ThemeManager.Bg2 };
            var topGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, BackColor = ThemeManager.Bg2, Margin = new Padding(3, 3, 3, 1) };
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _urlBox = new AntdUI.Input
            {
                Dock = DockStyle.Fill,
                Text = item.ip,
                ReadOnly = item.locked,
                PlaceholderText = conn == "cogsocket" ? I18n.T("connPlaceholder") : I18n.T("urlPlaceholder"),
            };
            _urlBox.LostFocus += (s, e) => CommitUrl();
            _urlBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { CommitUrl(); e.SuppressKeyPress = true; } };
            topGrid.Controls.Add(_urlBox, 0, 0);

            _remarkBox = new AntdUI.Input
            {
                Dock = DockStyle.Fill,
                Text = item.remark,
                ReadOnly = item.locked,
                PlaceholderText = I18n.T("remarkPlaceholder"),
                Margin = new Padding(4, 0, 0, 0),
            };
            _remarkBox.LostFocus += (s, e) =>
            {
                var it = ConfigService.EnsureItem(_id);
                it.remark = _remarkBox.Text;
                ConfigService.SaveCamera();
            };
            topGrid.Controls.Add(_remarkBox, 1, 0);

            _lockBtn = new AntdUI.Button { Width = 78, Height = 26, Margin = new Padding(4, 0, 0, 0), DefaultBorderColor = ThemeManager.BtnBorder, BorderWidth = 1 };
            _lockBtn.Click += (s, e) => ToggleLock();
            topGrid.Controls.Add(_lockBtn, 2, 0);

            _refreshBtn = new AntdUI.Button { Text = I18n.T("refresh"), IconSvg = AntIcon.Svg(AntIcon.Reload), Width = 78, Height = 26, Margin = new Padding(4, 0, 0, 0), DefaultBorderColor = ThemeManager.BtnBorder, BorderWidth = 1 };
            _refreshBtn.Click += async (s, e) => await ReloadAsync();
            topGrid.Controls.Add(_refreshBtn, 3, 0);

            _maxBtn = new AntdUI.Button { Width = 78, Height = 26, Margin = new Padding(4, 0, 0, 0), DefaultBorderColor = ThemeManager.BtnBorder, BorderWidth = 1 };
            _maxBtn.Click += (s, e) =>
            {
                _isMaximized = !_isMaximized;
                UpdateMaxBtn();
                ToggleMaximize?.Invoke(this);
            };
            topGrid.Controls.Add(_maxBtn, 4, 0);
            UpdateLockBtn();
            UpdateMaxBtn();

            _top.Controls.Add(topGrid);

            // ---------- Rendering area ----------
            if (_useSubGrid)
            {
                // Sub-grid mode: PictureBoxes
                _subGridPanel = BuildSubGridPanel();
                root.Controls.Add(_subGridPanel, 0, 1);
                root.Controls.Add(_top, 0, 0);
                Controls.Add(root);
                // Subscribe shared CogSocket connection
                InitCogSocket();
            }
            else if (_isCogSocket)
            {
                // CogSocket 未完成配置：不回退 HTTP，仅显示配置提示
                root.Controls.Add(BuildCogSocketPlaceholder(), 0, 1);
                root.Controls.Add(_top, 0, 0);
                Controls.Add(root);
            }
            else
            {
                // WebView2 mode
                _webView = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill };
                _webView.CoreWebView2InitializationCompleted += async (s, e) =>
                {
                    if (e.IsSuccess)
                    {
                        _webViewReady = true;
                        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                        var script = LoadHmiI18nScript();
                        if (!string.IsNullOrEmpty(script))
                        {
                            try { await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script); } catch { }
                        }
                    }
                    Navigate();
                };
                root.Controls.Add(_webView, 0, 1);
                root.Controls.Add(_top, 0, 0);
                Controls.Add(root);
            }

            I18n.LanguageChanged += OnLanguageChanged;
            ThemeManager.ThemeChanged += ApplyTheme;
            Disposed += OnDisposed;
        }

        // ==================== Sub-grid construction ====================

        private TableLayoutPanel BuildSubGridPanel()
        {
            var (cols, rows) = SubGridLayout(_subGridCount);
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = cols,
                RowCount = rows,
                BackColor = ThemeManager.Bg2,
                Padding = new Padding(2),
                Margin = new Padding(0),
            };
            for (int i = 0; i < cols; i++) panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cols));
            for (int i = 0; i < rows; i++) panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));

            // 建立 槽位 → 拍照标识取值 映射（槽位号即子网格位置）
            var slotValue = new Dictionary<int, string>();
            foreach (var b in _bindings)
            {
                if (b.slotIndex < 0 || b.slotIndex >= _subGridCount || slotValue.ContainsKey(b.slotIndex)) continue;
                var v = b.value.Trim();
                slotValue[b.slotIndex] = v;
                _valueToSlot[v] = b.slotIndex;
            }
            if (_valueToSlot.Count > 0) _firstSlot = _valueToSlot.Values.Min();

            for (int i = 0; i < cols * rows; i++)
            {
                var pb = new PictureBox
                {
                    Dock = DockStyle.Fill,
                    BackColor = ThemeManager.Bg,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BorderStyle = BorderStyle.None,
                    Margin = new Padding(1),
                };
                // Label overlay: 显示绑定的拍照标识取值（如 11/21/31/41），未绑定时显示槽位号
                string label = slotValue.TryGetValue(i, out var sv) ? sv : $"#{i + 1}";
                pb.Controls.Add(new System.Windows.Forms.Label
                {
                    Text = label,
                    AutoSize = true,
                    ForeColor = ThemeManager.FgDim,
                    Font = new Font("Microsoft YaHei UI", 7f),
                    Location = new Point(2, 2),
                    BackColor = Color.Transparent,
                });
                panel.Controls.Add(pb, i % cols, i / cols);

                if (slotValue.ContainsKey(i))
                    _slotBoxes[i] = pb;
            }
            return panel;
        }

        // ==================== CogSocket placeholder ====================

        /// <summary>CogSocket 配置不完整时显示提示，不加载 HTTP HMI 页面。</summary>
        private Panel BuildCogSocketPlaceholder()
        {
            _placeholderHost = new Panel { Dock = DockStyle.Fill, BackColor = ThemeManager.Bg };
            _placeholderLabel = new System.Windows.Forms.Label
            {
                Text = I18n.T("cogSocketNotConfigured"),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = ThemeManager.FgDim,
                Font = new Font("Microsoft YaHei UI", 9f),
                Padding = new Padding(12),
            };
            _placeholderHost.Controls.Add(_placeholderLabel);
            return _placeholderHost;
        }

        private static (int cols, int rows) SubGridLayout(int count) => count switch
        {
            1 => (1, 1),
            2 => (2, 1),
            4 => (2, 2),
            6 => (3, 2),
            8 => (4, 2),
            _ => (2, 2),
        };

        // ==================== CogSocket lifecycle ====================

        private void InitCogSocket()
        {
            var item = ConfigService.EnsureItem(_id);
            if (string.IsNullOrWhiteSpace(item.ip) || _cogSub != null) return;
            _cogSub = CogSocketManager.Subscribe(item.ip, _identifierCell, OnSubGridResult);
        }

        /// <summary>拍照完成回调（后台线程）：标识取值 → 绑定槽位 → 显示原图。</summary>
        private void OnSubGridResult(string value, Bitmap bmp)
        {
            int slot;
            if (!_useSubGrid || IsDisposed || bmp == null ||
                !_valueToSlot.TryGetValue((value ?? "").Trim(), out slot) || !_slotBoxes.ContainsKey(slot))
            {
                bmp?.Dispose();
                return;
            }

            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => ApplySubGridImage(slot, bmp))); }
                catch { bmp.Dispose(); }
            }
            else
            {
                ApplySubGridImage(slot, bmp);
            }
        }

        private void ApplySubGridImage(int slot, Bitmap bmp)
        {
            var pb = _slotBoxes[slot];
            if (IsDisposed || pb.IsDisposed) { bmp?.Dispose(); return; }
            // 最小槽位（第一次拍照）到达说明是新一轮产品：先清空上一轮所有子网格，避免新旧混显
            if (slot == _firstSlot) ClearAllSubGrids();
            var old = pb.Image;
            pb.Image = bmp;
            old?.Dispose();
        }

        private void ClearAllSubGrids()
        {
            if (IsDisposed || _subGridPanel == null) return;
            foreach (Control c in _subGridPanel.Controls)
            {
                if (c is PictureBox pb)
                {
                    var old = pb.Image;
                    pb.Image = null;
                    old?.Dispose();
                }
            }
        }

        // ==================== WebView2 path ====================

        private static string LoadHmiI18nScript()
        {
            if (_hmiI18nScript != null) return _hmiI18nScript;
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (name.EndsWith("hmi-i18n.js"))
                    {
                        using var s = asm.GetManifestResourceStream(name);
                        using var r = new System.IO.StreamReader(s, System.Text.Encoding.UTF8);
                        _hmiI18nScript = r.ReadToEnd();
                        return _hmiI18nScript;
                    }
                }
            }
            catch { }
            _hmiI18nScript = "";
            return _hmiI18nScript;
        }

        private void PostHmiLang()
        {
            if (!_webViewReady || _webView?.CoreWebView2 == null) return;
            try
            {
                _webView.CoreWebView2.PostWebMessageAsJson(
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

        private void Navigate()
        {
            if (_webView == null) return;
            var item = ConfigService.GetItem(_id);
            if (item == null) return;
            var url = (item.ip ?? "").Trim();
            if (url.Length == 0) return;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = "http://" + url;
            try { _webView.CoreWebView2?.Navigate(url); } catch { }
        }

        // ==================== Common UI ====================

        private void UpdateLockBtn()
        {
            var locked = ConfigService.EnsureItem(_id).locked;
            _lockBtn.Text = locked ? I18n.T("unlock") : I18n.T("lock");
            _lockBtn.IconSvg = AntIcon.Svg(locked ? AntIcon.Lock : AntIcon.Unlock);
        }

        private void UpdateMaxBtn()
        {
            _maxBtn.Text = _isMaximized ? I18n.T("restore") : I18n.T("maximize");
            _maxBtn.IconSvg = AntIcon.Svg(_isMaximized ? AntIcon.Shrink : AntIcon.Expand);
        }

        private void OnLanguageChanged()
        {
            UpdateLockBtn();
            UpdateMaxBtn();
            _refreshBtn.Text = I18n.T("refresh");
            _refreshBtn.IconSvg = AntIcon.Svg(AntIcon.Reload);
            var conn = ConfigService.NormalizeConn(ConfigService.EnsureItem(_id).conn);
            _urlBox.PlaceholderText = conn == "cogsocket" ? I18n.T("connPlaceholder") : I18n.T("urlPlaceholder");
            _remarkBox.PlaceholderText = I18n.T("remarkPlaceholder");
            if (_placeholderLabel != null) _placeholderLabel.Text = I18n.T("cogSocketNotConfigured");
            PostHmiLang();
        }

        public void ApplyTheme()
        {
            BackColor = ThemeManager.Bg2;
            _top.BackColor = ThemeManager.Bg2;
            if (_placeholderHost != null)
            {
                _placeholderHost.BackColor = ThemeManager.Bg;
                _placeholderLabel.ForeColor = ThemeManager.FgDim;
            }
            if (_useSubGrid && _subGridPanel != null)
            {
                _subGridPanel.BackColor = ThemeManager.Bg2;
                foreach (Control c in _subGridPanel.Controls)
                {
                    if (c is PictureBox pb) pb.BackColor = ThemeManager.Bg;
                }
            }
        }

        private void CommitUrl()
        {
            var item = ConfigService.EnsureItem(_id);
            item.ip = _urlBox.Text.Trim();
            ConfigService.SaveCamera();
            if (!_useSubGrid && _webViewReady && item.locked)
                Navigate();
        }

        private void ToggleLock()
        {
            var item = ConfigService.EnsureItem(_id);
            item.locked = !item.locked;
            _urlBox.ReadOnly = item.locked;
            _remarkBox.ReadOnly = item.locked;
            UpdateLockBtn();
            ConfigService.SaveCamera();
        }

        public async System.Threading.Tasks.Task ReloadAsync()
        {
            if (IsDisposed || Disposing) return;
            if (_useSubGrid)
            {
                ClearAllSubGrids();
                InitCogSocket();
                // 刷新：重建该相机的共享连接（同相机的其他 Cell 会自动恢复接收）
                CogSocketManager.Reconnect(ConfigService.EnsureItem(_id).ip);
                return;
            }
            // CogSocket 未完成配置：不回退 HTTP WebView2
            if (_isCogSocket) return;
            if (!_webViewReady)
            {
                try { await _webView.EnsureCoreWebView2Async(null); }
                catch { return; }
            }
            else
            {
                try { _webView.Reload(); return; } catch { }
            }
            Navigate();
        }

        // ==================== Cleanup ====================

        private void OnDisposed(object sender, EventArgs e)
        {
            I18n.LanguageChanged -= OnLanguageChanged;
            ThemeManager.ThemeChanged -= ApplyTheme;

            if (_useSubGrid)
            {
                if (_cogSub != null)
                {
                    _cogSub.Dispose();
                    _cogSub = null;
                }
                // Dispose any PictureBox images
                if (_subGridPanel != null)
                {
                    foreach (Control c in _subGridPanel.Controls)
                    {
                        if (c is PictureBox pb)
                        {
                            var old = pb.Image;
                            pb.Image = null;
                            old?.Dispose();
                        }
                    }
                }
            }
        }
    }
}
