using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
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
        private readonly AntdUI.Button _productBtn;   // 产品图：仅显示采集图像
        private readonly AntdUI.Button _overlayBtn;   // 叠加图：图像 + 相机 SVGLayer
        private readonly AntdUI.Button _fitBtn;       // 适合：叠加视图复位为槽位适配尺寸
        private readonly AntdUI.Button _webBtn;       // 网页：隐藏子网格，整格显示 HTTP HMI
        private readonly Panel _top;

        // WebView2 path: http / gige / cogsocket-subGrid==1；CogSocket 子网格的“网页”模式按需创建
        private Microsoft.Web.WebView2.WinForms.WebView2 _webView;
        private readonly TableLayoutPanel _root;
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

        // 叠加图：每个子网格槽位一个 WebView2，渲染图像 + SVG（与 HTTP 模式同源渲染行为）
        private readonly Dictionary<int, Microsoft.Web.WebView2.WinForms.WebView2> _slotWebViews = new Dictionary<int, Microsoft.Web.WebView2.WinForms.WebView2>();
        private readonly HashSet<int> _slotWebViewReady = new HashSet<int>();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> _slotDataUrl = new System.Collections.Concurrent.ConcurrentDictionary<int, string>();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> _slotSvg = new System.Collections.Concurrent.ConcurrentDictionary<int, string>();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, (int W, int H)> _slotSrcSize = new System.Collections.Concurrent.ConcurrentDictionary<int, (int W, int H)>();
        private volatile bool _overlayMode;
        private volatile bool _webMode;   // 网页模式：整格显示 HTTP HMI 页面，隐藏槽位

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
            _root = root;

            // ---------- Top bar ----------
            _top = new Panel { Dock = DockStyle.Fill, Height = 30, BackColor = ThemeManager.Bg2 };
            int btnCols = _useSubGrid ? 7 : 3;   // 产品图/叠加图/适合/网页 + 锁/刷新/最大化
            var topGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 + btnCols, RowCount = 1, BackColor = ThemeManager.Bg2, Margin = new Padding(3, 3, 3, 1) };
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            for (int i = 0; i < btnCols; i++) topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

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

            int col = 2;
            if (_useSubGrid)
            {
                _productBtn = new AntdUI.Button { Text = I18n.T("productImage"), Width = 64, Height = 26, Margin = new Padding(4, 0, 0, 0), DefaultBorderColor = ThemeManager.BtnBorder, BorderWidth = 1 };
                _productBtn.Click += (s, e) => SetOverlayMode(false);
                topGrid.Controls.Add(_productBtn, col++, 0);

                _overlayBtn = new AntdUI.Button { Text = I18n.T("overlayImage"), Width = 64, Height = 26, Margin = new Padding(4, 0, 0, 0), DefaultBorderColor = ThemeManager.BtnBorder, BorderWidth = 1 };
                _overlayBtn.Click += (s, e) => SetOverlayMode(true);
                topGrid.Controls.Add(_overlayBtn, col++, 0);

                _fitBtn = new AntdUI.Button { Text = I18n.T("fitView"), Width = 52, Height = 26, Margin = new Padding(4, 0, 0, 0), DefaultBorderColor = ThemeManager.BtnBorder, BorderWidth = 1 };
                _fitBtn.Click += (s, e) => FitOverlayViews();
                topGrid.Controls.Add(_fitBtn, col++, 0);

                _webBtn = new AntdUI.Button { Text = I18n.T("webView"), Width = 52, Height = 26, Margin = new Padding(4, 0, 0, 0), DefaultBorderColor = ThemeManager.BtnBorder, BorderWidth = 1 };
                _webBtn.Click += (s, e) => SetViewMode("web");
                topGrid.Controls.Add(_webBtn, col++, 0);
            }

            _lockBtn = new AntdUI.Button { Width = 78, Height = 26, Margin = new Padding(4, 0, 0, 0), DefaultBorderColor = ThemeManager.BtnBorder, BorderWidth = 1 };
            _lockBtn.Click += (s, e) => ToggleLock();
            topGrid.Controls.Add(_lockBtn, col++, 0);

            _refreshBtn = new AntdUI.Button { Text = I18n.T("refresh"), IconSvg = AntIcon.Svg(AntIcon.Reload), Width = 78, Height = 26, Margin = new Padding(4, 0, 0, 0), DefaultBorderColor = ThemeManager.BtnBorder, BorderWidth = 1 };
            _refreshBtn.Click += async (s, e) => await ReloadAsync();
            topGrid.Controls.Add(_refreshBtn, col++, 0);

            _maxBtn = new AntdUI.Button { Width = 78, Height = 26, Margin = new Padding(4, 0, 0, 0), DefaultBorderColor = ThemeManager.BtnBorder, BorderWidth = 1 };
            _maxBtn.Click += (s, e) =>
            {
                _isMaximized = !_isMaximized;
                UpdateMaxBtn();
                ToggleMaximize?.Invoke(this);
            };
            topGrid.Controls.Add(_maxBtn, col, 0);
            UpdateLockBtn();
            UpdateMaxBtn();
            UpdateViewButtons();

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
                // 恢复上次保存的 产品图/叠加图/网页 选择（句柄创建后再初始化 WebView2）
                var savedMode = ConfigService.EnsureItem(_id).viewMode;
                if (!string.Equals(savedMode, "product", StringComparison.OrdinalIgnoreCase))
                {
                    HandleCreated += (s, e) =>
                    {
                        if (!_overlayMode && !_webMode) SetViewMode(savedMode, persist: false);
                    };
                }
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
                EnsureHttpWebView();
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
                var v = NormalizeShotValue(b.value);
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

        /// <summary>拍照值归一化：相机 float 单元格返回 "41.0"，统一为 "41" 以便匹配绑定值。</summary>
        private static string NormalizeShotValue(string value)
        {
            var v = (value ?? "").Trim();
            if (v.Length == 0) return v;
            if (double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) &&
                d == Math.Floor(d) && Math.Abs(d) < 1e15)
            {
                return ((long)d).ToString(CultureInfo.InvariantCulture);
            }
            return v;
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

        /// <summary>拍照完成回调（后台线程）：标识取值 → 绑定槽位 → 显示图像。先缩略图，原图到达后替换。</summary>
        private void OnSubGridResult(string value, Bitmap bmp, bool isOriginal, string svg, int srcWidth, int srcHeight)
        {
            int slot;
            if (!_useSubGrid || IsDisposed || bmp == null ||
                !_valueToSlot.TryGetValue(NormalizeShotValue(value), out slot) || !_slotBoxes.ContainsKey(slot))
            {
                bmp?.Dispose();
                return;
            }

            // 保存本次拍照的叠加图形与原始像素尺寸；叠加模式开启时同时把图像编码为 data URL 供 WebView2 渲染
            _slotSvg[slot] = svg ?? "";
            if (srcWidth > 0 && srcHeight > 0) _slotSrcSize[slot] = (srcWidth, srcHeight);
            string dataUrl = null;
            if (_overlayMode) dataUrl = ToDataUrl(bmp);

            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => ApplySubGridImage(slot, bmp, isOriginal, dataUrl))); }
                catch { bmp.Dispose(); }
            }
            else
            {
                ApplySubGridImage(slot, bmp, isOriginal, dataUrl);
            }
        }

        private void ApplySubGridImage(int slot, Bitmap bmp, bool isOriginal, string dataUrl)
        {
            var pb = _slotBoxes[slot];
            if (IsDisposed || pb.IsDisposed) { bmp?.Dispose(); return; }
            // 最小槽位（第一次拍照）缩略图到达说明是新一轮产品：先清空上一轮，避免新旧混显；
            // 原图补发只替换本槽位，不能再次清空。
            if (!isOriginal && slot == _firstSlot) ClearAllSubGrids();
            var old = pb.Image;
            pb.Image = bmp;
            old?.Dispose();

            if (dataUrl != null)
            {
                _slotDataUrl[slot] = dataUrl;
                PostOverlay(slot);
            }
        }

        /// <summary>产品图 / 叠加图 / 网页 三选一；网页模式隐藏槽位，整格显示 HTTP HMI，并持久化。</summary>
        private void SetViewMode(string mode, bool persist = true)
        {
            if (!_useSubGrid) return;
            _webMode = string.Equals(mode, "web", StringComparison.OrdinalIgnoreCase);
            _overlayMode = !_webMode && string.Equals(mode, "overlay", StringComparison.OrdinalIgnoreCase);
            if (persist) SaveViewMode(_webMode ? "web" : (_overlayMode ? "overlay" : "product"));
            UpdateViewButtons();

            if (_webMode)
            {
                EnsureHttpWebView();
                if (_subGridPanel != null) _subGridPanel.Visible = false;
                foreach (var wv in _slotWebViews.Values) wv.Visible = false;
                _webView.Visible = true;
                _root.Controls.SetChildIndex(_webView, 0);
                return;
            }

            if (_webView != null) _webView.Visible = false;
            if (_subGridPanel != null) _subGridPanel.Visible = true;

            if (!_overlayMode)
            {
                foreach (var wv in _slotWebViews.Values) wv.Visible = false;
                return;
            }

            EnsureOverlayWebViews();
            foreach (var wv in _slotWebViews.Values) wv.Visible = true;
            foreach (var slot in _slotBoxes.Keys)
            {
                if (!_slotDataUrl.ContainsKey(slot))
                {
                    var img = _slotBoxes[slot].Image;
                    if (img != null) _slotDataUrl[slot] = ToDataUrl(img);
                }
                PostOverlay(slot);
            }
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
            ClearOverlays();
        }

        // ==================== Overlay (SVG) ====================

        /// <summary>切换产品图 / 叠加图显示模式（叠加视图支持滚轮缩放、拖动平移、适合复位），并持久化选择。</summary>
        private void SetOverlayMode(bool overlay, bool persist = true)
        {
            SetViewMode(overlay ? "overlay" : "product", persist);
        }

        private void UpdateViewButtons()
        {
            if (_productBtn == null || _overlayBtn == null) return;
            _productBtn.Type = (!_overlayMode && !_webMode) ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
            _overlayBtn.Type = _overlayMode ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
            if (_fitBtn != null) _fitBtn.Enabled = _overlayMode;
            if (_webBtn != null) _webBtn.Type = _webMode ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
        }

        /// <summary>把当前的 产品图/叠加图 选择写回 CameraConfig.json。</summary>
        private void SaveViewMode(string mode)
        {
            try
            {
                var item = ConfigService.EnsureItem(_id);
                if (item.viewMode == mode) return;
                item.viewMode = mode;
                ConfigService.SaveCamera();
            }
            catch { }
        }

        /// <summary>适合：把所有叠加视图的缩放/平移复位为槽位适配。</summary>
        private void FitOverlayViews()
        {
            foreach (var slot in _slotWebViews.Keys)
            {
                if (!_slotWebViewReady.Contains(slot)) continue;
                try { _slotWebViews[slot].CoreWebView2?.PostWebMessageAsJson("{\"fit\":true}"); } catch { }
            }
        }

        private void EnsureOverlayWebViews()
        {
            foreach (var kv in _slotBoxes)
            {
                if (_slotWebViews.ContainsKey(kv.Key)) continue;
                var wv = new Microsoft.Web.WebView2.WinForms.WebView2
                {
                    Dock = DockStyle.Fill,
                    DefaultBackgroundColor = Color.Transparent,
                    Visible = _overlayMode,
                };
                _slotWebViews[kv.Key] = wv;
                kv.Value.Controls.Add(wv);
                wv.BringToFront();
                _ = InitializeOverlayWebViewAsync(kv.Key, wv);
            }
        }

        private async System.Threading.Tasks.Task InitializeOverlayWebViewAsync(int slot, Microsoft.Web.WebView2.WinForms.WebView2 wv)
        {
            try
            {
                await wv.EnsureCoreWebView2Async(null);
                wv.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                wv.CoreWebView2.Settings.AreDevToolsEnabled = false;
                wv.CoreWebView2.Settings.IsStatusBarEnabled = false;
                wv.CoreWebView2.NavigationCompleted += (s, e) =>
                {
                    _slotWebViewReady.Add(slot);
                    PostOverlay(slot);
                };
                wv.CoreWebView2.NavigateToString(OverlayHtml);
            }
            catch { }
        }

        /// <summary>把当前槽位的图像与 SVG 推给该槽位的 WebView2（像素坐标用图像原始尺寸）。</summary>
        private void PostOverlay(int slot)
        {
            if (IsDisposed) return;
            var wv = _slotWebViews.TryGetValue(slot, out var v) ? v : null;
            if (wv == null || wv.CoreWebView2 == null || !_slotWebViewReady.Contains(slot)) return;
            var pb = _slotBoxes.TryGetValue(slot, out var box) ? box : null;
            // SVG 坐标对应原始采集图像；缩略图仅用于显示，不能拿缩略图尺寸当 viewBox
            int w = 0, h = 0;
            if (_slotSrcSize.TryGetValue(slot, out var src)) { w = src.W; h = src.H; }
            if (w <= 0 || h <= 0) { w = box?.Image?.Width ?? 0; h = box?.Image?.Height ?? 0; }
            if (w <= 0 || h <= 0) return;

            string img = _slotDataUrl.TryGetValue(slot, out var d) ? d : "";
            string svg = _slotSvg.TryGetValue(slot, out var s) ? s : "";
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new { w, h, img, svg });
                wv.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch { }
        }

        private void ClearOverlays()
        {
            foreach (var slot in _slotBoxes.Keys)
            {
                _slotDataUrl.TryRemove(slot, out _);
                _slotSvg.TryRemove(slot, out _);
                _slotSrcSize.TryRemove(slot, out _);
            }
            foreach (var slot in _slotWebViews.Keys)
            {
                if (!_slotWebViewReady.Contains(slot)) continue;
                try { _slotWebViews[slot].CoreWebView2?.PostWebMessageAsJson("{\"clear\":true}"); } catch { }
            }
        }

        /// <summary>叠加图 HTML：图像与 SVG 按原始像素坐标等比缩放居中，与产品图 Zoom 行为一致。</summary>
        private const string OverlayHtml = @"<!doctype html><html><head><meta charset=""utf-8""><style>
html,body{margin:0;height:100%;background:transparent;overflow:hidden;user-select:none;touch-action:none;cursor:grab}
#fit{position:absolute;left:0;top:0}
#im,#sv{position:absolute;left:0;top:0;width:100%;height:100%}
#im{display:block;-webkit-user-drag:none}
</style></head><body>
<div id=""fit""><img id=""im""><div id=""sv""></div></div>
<script>
(function(){
 var fit=document.getElementById('fit'), im=document.getElementById('im'), sv=document.getElementById('sv');
 var W=0,H=0, zoom=1, panX=0, panY=0;
 function baseScale(){ return Math.min(window.innerWidth/W, window.innerHeight/H); }
 function layout(){
   if(!W||!H) return;
   var s=baseScale()*zoom;
   fit.style.width=(W*s)+'px'; fit.style.height=(H*s)+'px';
   fit.style.left=((window.innerWidth-W*s)/2+panX)+'px';
   fit.style.top=((window.innerHeight-H*s)/2+panY)+'px';
 }
 function reset(){ zoom=1; panX=0; panY=0; layout(); }
 window.addEventListener('resize',layout);
 // 滚轮以鼠标位置为中心缩放（zoom=1 即适合槽位）
 document.addEventListener('wheel',function(e){
   if(!W||!H) return;
   e.preventDefault();
   var nz=Math.max(0.1, Math.min(20, zoom*Math.exp(-e.deltaY*0.0015)));
   if(nz===zoom) return;
   var s0=baseScale()*zoom, ns=baseScale()*nz;
   var cx=e.clientX-window.innerWidth/2-panX, cy=e.clientY-window.innerHeight/2-panY;
   panX=e.clientX-window.innerWidth/2-cx*(ns/s0);
   panY=e.clientY-window.innerHeight/2-cy*(ns/s0);
   zoom=nz; layout();
 },{passive:false});
 // 鼠标拖动平移
 var drag=false, lx=0, ly=0;
 document.addEventListener('pointerdown',function(e){
   if(e.button!==0) return;
   drag=true; lx=e.clientX; ly=e.clientY;
   document.body.style.cursor='grabbing'; e.preventDefault();
 });
 document.addEventListener('pointermove',function(e){
   if(!drag) return;
   panX+=e.clientX-lx; panY+=e.clientY-ly;
   lx=e.clientX; ly=e.clientY; layout();
 });
 function endDrag(){ if(!drag) return; drag=false; document.body.style.cursor='grab'; }
 document.addEventListener('pointerup',endDrag);
 document.addEventListener('pointercancel',endDrag);
 document.addEventListener('dragstart',function(e){ e.preventDefault(); });
 window.chrome.webview.addEventListener('message',function(e){
   var d=e.data||{};
   if(d.clear){ im.removeAttribute('src'); sv.replaceChildren(); return; }
   if(d.fit){ reset(); return; }
   if(d.w>0&&d.h>0){ W=d.w; H=d.h; }
   if(d.img){ im.src=d.img; }
   if(d.svg){
     try{
       var doc=new DOMParser().parseFromString(d.svg,'image/svg+xml');
       var root=doc.documentElement;
       if(root&&root.nodeName.toLowerCase()==='svg'){
         root.setAttribute('viewBox','0 0 '+W+' '+H);
         root.setAttribute('preserveAspectRatio','none');
         root.setAttribute('width','100%');
         root.setAttribute('height','100%');
         sv.replaceChildren(document.importNode(root,true));
       }
     }catch(_){}
   }
   layout();
 });
})();
</script></body></html>";

        /// <summary>位图转 JPEG data URL（叠加图用全分辨率图像，保证与产品图细节一致）。</summary>
        private static string ToDataUrl(Image img)
        {
            if (img == null) return "";
            try
            {
                using var ms = new System.IO.MemoryStream();
                var codec = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
                    .First(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
                using var ep = new System.Drawing.Imaging.EncoderParameters(1);
                ep.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
                img.Save(ms, codec, ep);
                return "data:image/jpeg;base64," + System.Convert.ToBase64String(ms.ToArray());
            }
            catch { return ""; }
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

        /// <summary>按需创建 HTTP HMI 的 WebView2（CogSocket 子网格的“网页”模式复用）。</summary>
        private void EnsureHttpWebView()
        {
            if (_webView != null) return;
            var wv = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill, Visible = _webMode || !_useSubGrid };
            wv.CoreWebView2InitializationCompleted += async (s, e) =>
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
            _webView = wv;
            _root.Controls.Add(wv, 0, 1);
            if (_webMode) wv.BringToFront();
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
            if (_productBtn != null) _productBtn.Text = I18n.T("productImage");
            if (_overlayBtn != null) _overlayBtn.Text = I18n.T("overlayImage");
            if (_fitBtn != null) _fitBtn.Text = I18n.T("fitView");
            if (_webBtn != null) _webBtn.Text = I18n.T("webView");
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
            if ((!_useSubGrid || _webMode) && _webViewReady && item.locked)
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
                // 网页模式下同时刷新 HMI 页面
                if (_webMode && _webView != null)
                {
                    try { if (_webViewReady) _webView.Reload(); else Navigate(); } catch { }
                }
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
                _slotWebViews.Clear();
                _slotWebViewReady.Clear();
                _slotDataUrl.Clear();
                _slotSvg.Clear();
                _slotSrcSize.Clear();
            }
        }
    }
}
