using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NomacsNet;

public class ViewerSettings
{
    public string BackgroundHex { get; set; } = "#FF303030";
    public double SlideshowSeconds { get; set; } = 3.0;
    public bool AntiAlias { get; set; } = true;
    public bool RecursiveScan { get; set; } = false;
    public bool ShowInfoPanel { get; set; } = true;
    public bool ShowThumbs { get; set; } = true;
    public bool ShowMetadata { get; set; } = false;
    public bool ShowStatusBar { get; set; } = false;
    public bool Frameless { get; set; } = false;
    public List<string> RecentFiles { get; set; } = new();
}

public static class AppSettings
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NomacsNet");
    private static readonly string Path_ = Path.Combine(Dir, "settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static ViewerSettings Load()
    {
        try
        {
            if (File.Exists(Path_))
                return JsonSerializer.Deserialize<ViewerSettings>(File.ReadAllText(Path_)) ?? new ViewerSettings();
        }
        catch { }
        return new ViewerSettings();
    }

    public static void Save(ViewerSettings s)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path_, JsonSerializer.Serialize(s, Options));
        }
        catch { }
    }
}

public class MainWindow : Window
{
    private static readonly string[] Extensions =
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".ico", ".wdp", ".jxr", ".avif", ".heic", ".heif", ".heifs", ".hif", ".dng", ".cr2", ".nef", ".arw" };

    private readonly ViewerSettings _settings = AppSettings.Load();
    private readonly DispatcherTimer _slideshow;
    private CancellationTokenSource _thumbCts = new();

    // core view
    private readonly Border _viewport;
    private readonly Image _image;
    private readonly Menu _menuBar;
    private readonly StatusBar _statusBar;
    private readonly StatusBarItem _statusFile, _statusZoom, _statusSize;

    // hud panels
    private readonly Border _infoPanel;
    private readonly TextBlock _infoName, _infoMeta, _infoDims;
    private readonly Border _metaPanel;
    private readonly ListView _metaList;
    private readonly ScrollViewer _thumbsScroll;
    private readonly StackPanel _thumbsHost;
    private readonly StackPanel _navBox;
    private readonly TextBlock _zoomLabel;
    private readonly List<string> _files = new();
    private readonly List<Border> _thumbItems = new();
    private int _index = -1;
    private BitmapSource? _bitmap;
    private double _zoom = 1.0;
    private double _angle;
    private bool _flipX;
    private double _panX, _panY;
    private bool _flipY;
    private bool _fitToWindow = true;
    private Point _dragStart;
    private bool _dragging;
    private MenuItem? _recentMenu;
    private MenuItem? _sortDesc;

    private static readonly Brush DarkPanel = new SolidColorBrush(Color.FromArgb(0xA6, 0x10, 0x10, 0x10));
    private static readonly Brush HoverAccent = new SolidColorBrush(Color.FromRgb(0x3E, 0x5C, 0x79));
    private static readonly Brush SubmenuBg = new SolidColorBrush(Color.FromRgb(0x23, 0x23, 0x23));
    private static readonly Brush BarBg = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D));
    private static readonly Brush MenuText = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
    private static readonly Brush GestureText = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
    private static readonly Brush ThumbHighlight = new SolidColorBrush(Color.FromRgb(0x4E, 0x9E, 0xEA));
    private static readonly Brush ThumbBorder = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));

    public MainWindow()
    {
        Title = "nomacs";
        Width = 1100; Height = 750;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = LoadIcon();
        FontSize = 13;

        _slideshow = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(0.5, _settings.SlideshowSeconds)) };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _menuBar = new Menu
        {
            Background = BarBg,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        BuildMenus();
        root.Children.Add(_menuBar);

        _image = new Image { Stretch = Stretch.None, SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(_image, AntiAliasMode());

        _viewport = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_settings.BackgroundHex)),
            ClipToBounds = true,
            Focusable = true,
            Child = _image
        };
        Grid.SetRow(_viewport, 1);
        root.Children.Add(_viewport);

        _statusBar = new StatusBar { Background = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)) };
        _statusFile = new StatusBarItem { Padding = new Thickness(10, 2, 10, 2) };
        _statusSize = new StatusBarItem { Padding = new Thickness(10, 2, 10, 2) };
        _statusZoom = new StatusBarItem { Padding = new Thickness(10, 2, 10, 2) };
        _statusFile.Content = "无图片 — 拖拽图片到窗口，或按 Ctrl+O 打开";
        _statusBar.Items.Add(_statusFile);
        _statusBar.Items.Add(new Separator());
        _statusBar.Items.Add(_statusSize);
        _statusBar.Items.Add(_statusZoom);
        Grid.SetRow(_statusBar, 2);
        root.Children.Add(_statusBar);

        // ---------- HUD: info box bottom-left ----------
        _infoPanel = new Border
        {
            Background = DarkPanel,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 14, 8),
            Margin = new Thickness(10, 0, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false
        };
        {
            var sp = new StackPanel();
            _infoName = new TextBlock { Foreground = MenuText, FontWeight = FontWeights.SemiBold, FontSize = 14 };
            _infoMeta = new TextBlock { Foreground = GestureText, FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
            _infoDims = new TextBlock { Foreground = GestureText, FontSize = 12, Margin = new Thickness(0, 1, 0, 0) };
            sp.Children.Add(_infoName);
            sp.Children.Add(_infoMeta);
            sp.Children.Add(_infoDims);
            _infoPanel.Child = sp;
        }
        Grid.SetRow(_infoPanel, 1);
        root.Children.Add(_infoPanel);

        // ---------- HUD: zoom label bottom-right ----------
        _zoomLabel = new TextBlock
        {
            Foreground = MenuText,
            FontSize = 13,
            Margin = new Thickness(4)
        };
        var zoomBorder = new Border
        {
            Background = DarkPanel,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 10, 10),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
            Child = _zoomLabel
        };
        Grid.SetRow(zoomBorder, 1);
        root.Children.Add(zoomBorder);

        // ---------- HUD: navigation arrows bottom-center ----------
        _navBox = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 110),
            Opacity = 0.75
        };
        _navBox.Children.Add(NavButton("◀", -1));
        _navBox.Children.Add(NavButton("▶", 1));
        Grid.SetRow(_navBox, 1);
        root.Children.Add(_navBox);

        // ---------- HUD: thumbnails bottom-center ----------
        _thumbsHost = new StackPanel { Orientation = Orientation.Horizontal };
        _thumbsScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _thumbsHost,
            Background = DarkPanel,
            Padding = new Thickness(4, 2, 4, 4),
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            MaxWidth = 720
        };
        Grid.SetRow(_thumbsScroll, 1);
        root.Children.Add(_thumbsScroll);

        // ---------- HUD: metadata panel right ----------
        _metaPanel = new Border
        {
            Background = DarkPanel,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 4, 6, 6),
            Margin = new Thickness(0, 10, 10, 10),
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Right,
            Width = 320
        };
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = "元数据信息",
                Foreground = MenuText,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Margin = new Thickness(6, 4, 0, 6)
            });
            _metaList = new ListView
            {
                Background = Brushes.Transparent,
                Foreground = MenuText,
                BorderThickness = new Thickness(0),
                FontSize = 12
            };
            var gv = new GridView();
            gv.Columns.Add(MakeColumn("键", 110));
            gv.Columns.Add(MakeColumn("值", 160));
            _metaList.View = gv;
            sp.Children.Add(_metaList);
            _metaPanel.Child = sp;
        }
        Grid.SetRow(_metaPanel, 1);
        root.Children.Add(_metaPanel);

        _viewport.ContextMenu = BuildContextMenu();
        _slideshow.Tick += (_, _) => Next(1);
        _viewport.MouseWheel += OnWheel;
        _viewport.MouseLeftButtonDown += OnPanStart;
        _viewport.MouseLeftButtonUp += OnPanEnd;
        _viewport.MouseMove += OnPanMove;
        KeyDown += OnKey;
        SizeChanged += (_, _) => { if (_fitToWindow) ApplyFit(); };
        Closing += (_, _) => { _slideshow.IsEnabled = false; _thumbCts.Cancel(); };
        AllowDrop = true;
        Drop += (_, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
                e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
                OpenPath(files[0]);
        };

        Content = root;
        ApplySettings(_settings);
        UpdateStatus();
    }

    private static GridViewColumn MakeColumn(string header, double width) =>
        new()
        {
            Header = new TextBlock { Text = header, Foreground = MenuText },
            Width = width,
            DisplayMemberBinding = CreateColumnBinding(header == "键" ? 0 : 1)
        };

    private static BindingBase CreateColumnBinding(int i) => new Binding($"[{i}]");

    private Border NavButton(string glyph, int dir)
    {
        var tb = new TextBlock
        {
            Text = glyph,
            Foreground = MenuText,
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var b = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0x10, 0x10, 0x10)),
            CornerRadius = new CornerRadius(13),
            Width = 26, Height = 26,
            Margin = new Thickness(5, 0, 5, 0),
            Child = tb,
            Cursor = Cursors.Hand
        };
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Next(dir);
        };
        return b;
    }

    // ---------- theming helpers ----------

    private BitmapScalingMode AntiAliasMode() =>
        _settings.AntiAlias ? BitmapScalingMode.HighQuality : BitmapScalingMode.LowQuality;

    private static Separator DarkSeparator() => new()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
        Height = 1,
        Margin = new Thickness(6, 5, 6, 5)
    };

    private MenuItem MI(string header, string? gesture = null, RoutedEventHandler? onClick = null, bool? checkable = null)
    {
        var mi = new MenuItem { Header = header, Foreground = MenuText };
        if (gesture != null) mi.Tag = gesture;
        if (checkable == true)
        {
            mi.IsChecked = true;
            mi.Template = SubmenuCheckedTemplate();
        }
        else
        {
            mi.Template = SubmenuItemTemplate();
        }
        if (onClick != null) mi.Click += onClick;
        return mi;
    }

    private MenuItem TopMenu(string header, string? gesture = null, RoutedEventHandler? onClick = null)
    {
        var mi = new MenuItem { Header = header, Foreground = MenuText };
        if (gesture != null) mi.Tag = gesture;
        mi.Template = TopLevelTemplate();
        if (onClick != null) mi.Click += onClick;
        return mi;
    }

    private MenuItem Sub(string header, string? gesture = null, RoutedEventHandler? onClick = null)
    {
        var mi = new MenuItem { Header = header, Foreground = MenuText };
        if (gesture != null) mi.Tag = gesture;
        mi.Template = SubmenuHeaderTemplate();
        if (onClick != null) mi.Click += onClick;
        return mi;
    }

    private ControlTemplate TopLevelTemplate()
    {
        var t = new ControlTemplate(typeof(MenuItem));
        var root = new FrameworkElementFactory(typeof(Grid), "root");
        var bd = new FrameworkElementFactory(typeof(Border), "Bd");
        bd.SetBinding(Border.BackgroundProperty,
            new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        bd.SetValue(Border.PaddingProperty, new Thickness(10, 5, 10, 5));
        bd.SetValue(Border.SnapsToDevicePixelsProperty, true);
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        cp.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        bd.AppendChild(cp);
        var popup = new FrameworkElementFactory(typeof(Popup), "PART_Popup");
        popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
        popup.SetValue(Popup.AllowsTransparencyProperty, true);
        popup.SetValue(Popup.StaysOpenProperty, false);
        var pb = new FrameworkElementFactory(typeof(Border));
        pb.SetValue(Border.BackgroundProperty, SubmenuBg);
        pb.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)));
        pb.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        pb.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        var sv = new FrameworkElementFactory(typeof(ScrollViewer));
        sv.SetValue(ScrollViewer.MaxHeightProperty, 520.0);
        sv.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        sv.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        sv.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        pb.AppendChild(sv);
        popup.AppendChild(pb);
        root.AppendChild(bd);
        root.AppendChild(popup);
        t.VisualTree = root;
        AddHoverTrigger(t);
        return t;
    }

    private ControlTemplate SubmenuHeaderTemplate()
    {
        var t = new ControlTemplate(typeof(MenuItem));
        var bd = new FrameworkElementFactory(typeof(Grid), "root");
        bd.SetValue(Grid.MarginProperty, new Thickness(2));
        var tb = new FrameworkElementFactory(typeof(Border), "Bd");
        tb.SetBinding(Border.BackgroundProperty,
            new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        tb.SetValue(Border.PaddingProperty, new Thickness(10, 5, 10, 5));
        var inner = new FrameworkElementFactory(typeof(Grid));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        cp.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        cp.SetValue(Grid.ColumnProperty, 0);
        var arr = new FrameworkElementFactory(typeof(TextBlock), "arrow");
        arr.SetValue(TextBlock.TextProperty, "▶");
        arr.SetValue(TextBlock.ForegroundProperty, GestureText);
        arr.SetValue(TextBlock.FontSizeProperty, 9.0);
        arr.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        arr.SetValue(Grid.ColumnProperty, 2);
        inner.AppendChild(cp);
        inner.AppendChild(arr);
        tb.AppendChild(inner);
        var popup = new FrameworkElementFactory(typeof(Popup), "PART_Popup");
        popup.SetValue(Popup.PlacementProperty, PlacementMode.Right);
        popup.SetValue(Popup.AllowsTransparencyProperty, true);
        popup.SetValue(Popup.StaysOpenProperty, false);
        var pb = new FrameworkElementFactory(typeof(Border));
        pb.SetValue(Border.BackgroundProperty, SubmenuBg);
        pb.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)));
        pb.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        pb.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        var sv = new FrameworkElementFactory(typeof(ScrollViewer));
        sv.SetValue(ScrollViewer.MaxHeightProperty, 520.0);
        sv.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        sv.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        sv.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        pb.AppendChild(sv);
        popup.AppendChild(pb);
        bd.AppendChild(popup);
        t.VisualTree = bd;
        AddHoverTrigger(t);
        return t;
    }

    private ControlTemplate SubmenuItemTemplate()
    {
        var t = new ControlTemplate(typeof(MenuItem));
        var bd = new FrameworkElementFactory(typeof(Border), "Bd");
        bd.SetBinding(Border.BackgroundProperty,
            new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        bd.SetValue(Border.PaddingProperty, new Thickness(10, 5, 10, 5));
        bd.SetValue(Border.MarginProperty, new Thickness(2, 0, 2, 0));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        cp.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        cp.SetValue(Grid.ColumnProperty, 0);
        var gest = new FrameworkElementFactory(typeof(TextBlock));
        gest.SetBinding(TextBlock.TextProperty, new Binding("Tag") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        gest.SetValue(TextBlock.ForegroundProperty, GestureText);
        gest.SetValue(TextBlock.FontSizeProperty, 12.0);
        gest.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        gest.SetValue(Grid.ColumnProperty, 2);
        var inner = new FrameworkElementFactory(typeof(Grid));
        inner.AppendChild(cp);
        inner.AppendChild(gest);
        bd.AppendChild(inner);
        t.VisualTree = bd;
        AddHoverTrigger(t);
        return t;
    }

    private ControlTemplate SubmenuCheckedTemplate()
    {
        var t = new ControlTemplate(typeof(MenuItem));
        var bd = new FrameworkElementFactory(typeof(Border), "Bd");
        bd.SetBinding(Border.BackgroundProperty,
            new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        bd.SetValue(Border.PaddingProperty, new Thickness(10, 5, 10, 5));
        bd.SetValue(Border.MarginProperty, new Thickness(2, 0, 2, 0));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        cp.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        cp.SetValue(Grid.ColumnProperty, 0);
        var gest = new FrameworkElementFactory(typeof(TextBlock));
        gest.SetBinding(TextBlock.TextProperty, new Binding("Tag") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        gest.SetValue(TextBlock.ForegroundProperty, GestureText);
        gest.SetValue(TextBlock.FontSizeProperty, 12.0);
        gest.SetValue(Grid.ColumnProperty, 2);
        var tick = new FrameworkElementFactory(typeof(TextBlock), "tick");
        tick.SetValue(TextBlock.TextProperty, "✓");
        tick.SetValue(TextBlock.ForegroundProperty, ThumbHighlight);
        tick.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        tick.SetValue(Grid.ColumnProperty, 1);
        tick.SetBinding(UIElement.VisibilityProperty,
            new Binding("IsChecked")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                Converter = new System.Windows.Controls.BooleanToVisibilityConverter()
            });
        var inner = new FrameworkElementFactory(typeof(Grid));
        inner.AppendChild(cp);
        inner.AppendChild(tick);
        inner.AppendChild(gest);
        bd.AppendChild(inner);
        t.VisualTree = bd;
        AddHoverTrigger(t);
        return t;
    }

    private static void AddHoverTrigger(ControlTemplate t)
    {
        var hl = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        hl.Setters.Add(new Setter(Control.BackgroundProperty, HoverAccent));
        t.Triggers.Add(hl);
    }

    private static ImageSource? LoadIcon()
    {
        try
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("nomacs.ico");
            if (stream == null) return null;
            using (stream)
            {
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                frame.Freeze();
                return frame;
            }
        }
        catch { return null; }
    }

    // ---------- menus ----------

    private void BuildMenus()
    {
        // ── 文件(&F) ──
        var file = TopMenu("文件(_F)");
        file.Items.Add(MI("打开(_O)", "Ctrl+O", (_, _) => OpenDialog()));
        file.Items.Add(MI("打开目录(_D)", "Ctrl+D", (_, _) => OpenDirDialog()));
        var with = Sub("打开方式(_O)");
        with.Items.Add(MI("资源管理器(_E)", null, (_, _) => OpenInExplorer()));
        file.Items.Add(with);
        _recentMenu = Sub("近期文件(_R)");
        file.Items.Add(_recentMenu);
        UpdateRecentMenu();
        file.Items.Add(DarkSeparator());
        file.Items.Add(MI("重命名(_N)", "F2", (_, _) => RenameDialog()));
        file.Items.Add(MI("转到(_G)", "Ctrl+G", (_, _) => GotoDialog()));
        file.Items.Add(DarkSeparator());
        file.Items.Add(MI("保存(_S)", "Ctrl+S", (_, _) => SaveFile()));
        file.Items.Add(MI("另存为(_A)", "Ctrl+Shift+S", (_, _) => SaveAsDialog()));
        file.Items.Add(MI("重新加载(_L)", "F6", (_, _) => Reload()));
        file.Items.Add(DarkSeparator());
        var sort = Sub("排序(_O)");
        sort.Items.Add(MI("按文件名(_F)", null, (_, _) => SortFiles(0)));
        sort.Items.Add(MI("按修改日期(_M)", null, (_, _) => SortFiles(1)));
        sort.Items.Add(MI("按大小(_S)", null, (_, _) => SortFiles(2)));
        sort.Items.Add(DarkSeparator());
        var descItem = MI("逆序(_R)", null, (_, _) =>
        {
            if (_sortDesc == null) return;
            _sortDesc.IsChecked = !_sortDesc.IsChecked;
            if (_files.Count > 0) Reload();
        }, checkable: true);
        descItem.IsChecked = false;
        _sortDesc = descItem;
        sort.Items.Add(descItem);
        file.Items.Add(sort);
        file.Items.Add(DarkSeparator());
        var recItem = MI("递归扫描文件夹(_A)", null, (_, _) =>
        {
            _settings.RecursiveScan = !_settings.RecursiveScan;
            AppSettings.Save(_settings);
            if (_files.Count > 0) Reload();
            MessageBox.Show(this,
                _settings.RecursiveScan ? "已开启递归扫描文件夹选项" : "已关闭递归扫描文件夹选项",
                "nomacs", MessageBoxButton.OK, MessageBoxImage.Information);
        }, checkable: true);
        recItem.IsChecked = _settings.RecursiveScan;
        file.Items.Add(recItem);
        file.Items.Add(DarkSeparator());
        file.Items.Add(MI("退出(_E)", "Esc", (_, _) => Close()));

        // ── 编辑(_E) ──
        var edit = TopMenu("编辑(_E)");
        edit.Items.Add(MI("复制(_C)", "Ctrl+C", (_, _) => CopyImage()));
        edit.Items.Add(MI("粘贴(_P)", "Ctrl+V", (_, _) => PasteImage()));
        edit.Items.Add(DarkSeparator());
        edit.Items.Add(MI("顺时针90°(_0)", "R", (_, _) => Rotate(90)));
        edit.Items.Add(MI("逆时针90°(_9)", "Shift+R", (_, _) => Rotate(-90)));
        edit.Items.Add(MI("180°", null, (_, _) => Rotate(180)));
        edit.Items.Add(DarkSeparator());
        edit.Items.Add(MI("水平翻转(_H)", "T", (_, _) => FlipX()));
        edit.Items.Add(MI("垂直翻转(_V)", "Shift+T", (_, _) => FlipY()));
        edit.Items.Add(DarkSeparator());
        edit.Items.Add(MI("背景颜色(_B)...", "B", (_, _) => PickBackground()));

        // ── 视图(_V) ──
        var view = TopMenu("视图(_V)");
        view.Items.Add(MI("放大(_I)", "Ctrl++", (_, _) => SetZoom(_zoom * 1.25)));
        view.Items.Add(MI("缩小(_Z)", "Ctrl+-", (_, _) => SetZoom(_zoom * 0.8)));
        view.Items.Add(DarkSeparator());
        view.Items.Add(MI("窗口尺寸适应图像(_F)", "0", (_, _) => ToggleFit()));
        view.Items.Add(MI("100%显示(_1)", "1", (_, _) => SetZoom(1.0)));
        var zoom = Sub("缩放(_Z)");
        foreach (var pct in new[] { 25, 50, 100, 200, 400, 800, 1600 })
        {
            double z = pct / 100.0;
            zoom.Items.Add(MI($"{pct}%", null, (_, _) => SetZoom(z)));
        }
        view.Items.Add(zoom);
        var antiItem = MI("抗锯齿(_A)", null, (_, _) =>
        {
            _settings.AntiAlias = !_settings.AntiAlias;
            AppSettings.Save(_settings);
            RenderOptions.SetBitmapScalingMode(_image, AntiAliasMode());
        }, checkable: true);
        antiItem.IsChecked = _settings.AntiAlias;
        view.Items.Add(antiItem);
        view.Items.Add(DarkSeparator());
        view.Items.Add(MI("全屏(_L)", "Ctrl+Shift+F", (_, _) => ToggleFullscreen()));
        var framelessItem = MI("无边框(_F)", null, (_, _) =>
        {
            _settings.Frameless = !_settings.Frameless;
            AppSettings.Save(_settings);
            ApplyWindowChrome();
        }, checkable: true);
        framelessItem.IsChecked = _settings.Frameless;
        view.Items.Add(framelessItem);

        // ── 面板(_P) ──
        var panels = TopMenu("面板(_P)");
        var thumbsItem = MI("缩略图(_T)", "Ctrl+T", (_, _) =>
        {
            _settings.ShowThumbs = !_settings.ShowThumbs;
            AppSettings.Save(_settings);
            ApplySettings(_settings);
        }, checkable: true);
        thumbsItem.IsChecked = _settings.ShowThumbs;
        panels.Items.Add(thumbsItem);
        var infoItem = MI("文件信息(_I)", "I", (_, _) =>
        {
            _settings.ShowInfoPanel = !_settings.ShowInfoPanel;
            AppSettings.Save(_settings);
            ApplySettings(_settings);
        }, checkable: true);
        infoItem.IsChecked = _settings.ShowInfoPanel;
        panels.Items.Add(infoItem);
        var metaItem = MI("元数据信息(_M)", "Ctrl+M", (_, _) =>
        {
            _settings.ShowMetadata = !_settings.ShowMetadata;
            AppSettings.Save(_settings);
            ApplySettings(_settings);
        }, checkable: true);
        metaItem.IsChecked = _settings.ShowMetadata;
        panels.Items.Add(metaItem);
        var statusItem = MI("状态栏(_S)", null, (_, _) =>
        {
            _settings.ShowStatusBar = !_settings.ShowStatusBar;
            AppSettings.Save(_settings);
            ApplySettings(_settings);
        }, checkable: true);
        statusItem.IsChecked = _settings.ShowStatusBar;
        panels.Items.Add(statusItem);
        panels.Items.Add(MI("隐藏所有面板(_H)", "Ctrl+H", (_, _) =>
        {
            _settings.ShowThumbs = false;
            _settings.ShowInfoPanel = false;
            _settings.ShowMetadata = false;
            _settings.ShowStatusBar = false;
            AppSettings.Save(_settings);
            ApplySettings(_settings);
        }));

        // ── 工具(_T) ──
        var tools = TopMenu("工具(_T)");
        tools.Items.Add(MI("设置(_S)...", "Ctrl+,", (_, _) => SettingsDialog()));
        tools.Items.Add(MI("插件管理器(_P)...", null, (_, _) => PluginsDialog()));
        tools.Items.Add(MI("批处理(_B)", null, (_, _) =>
            MessageBox.Show(this, "批处理功能在此 FOSS 复刻版中暂未实现。", "nomacs", MessageBoxButton.OK, MessageBoxImage.Information)));
        tools.Items.Add(DarkSeparator());
        tools.Items.Add(MI("切换幻灯片(_T)", "F5", (_, _) =>
        {
            _slideshow.IsEnabled = !_slideshow.IsEnabled;
            UpdateStatus();
        }));
        tools.Items.Add(MI("幻灯片放映(_S)...", "Shift+F5", (_, _) =>
        {
            if (!_slideshow.IsEnabled && _files.Count > 0)
            {
                _slideshow.IsEnabled = true;
                UpdateStatus();
            }
        }));

        // ── 帮助(_H) ──
        var help = TopMenu("帮助(_H)");
        help.Items.Add(MI("快捷键(_K)", "F1", (_, _) => ShortcutsDialog()));
        help.Items.Add(DarkSeparator());
        help.Items.Add(MI("关于 Nomacs(_A)", null, (_, _) => AboutDialog()));

        _menuBar.Items.Add(file);
        _menuBar.Items.Add(edit);
        _menuBar.Items.Add(view);
        _menuBar.Items.Add(panels);
        _menuBar.Items.Add(tools);
        _menuBar.Items.Add(help);
    }

    private void SortFiles(int key)
    {
        if (_files.Count == 0) return;
        var current = _index >= 0 ? _files[_index] : null;
        var desc = _sortDesc != null && _sortDesc.IsChecked;
        IEnumerable<string> q = key switch
        {
            1 => _files.OrderBy(File.GetLastWriteTime),
            2 => _files.OrderBy(f => new FileInfo(f).Length),
            _ => _files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase),
        };
        var list = q.ToList();
        if (desc) list.Reverse();
        _files.Clear();
        _files.AddRange(list);
        _index = current == null ? -1 : _files.IndexOf(current);
        LoadCurrent();
        RebuildThumbs();
    }

    private void OpenInExplorer()
    {
        if (_index < 0 || _index >= _files.Count) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_files[_index]}\"") { UseShellExecute = true });
    }

    private void CopyImage()
    {
        if (_bitmap == null) return;
        try
        {
            var conv = new FormatConvertedBitmap(_bitmap, PixelFormats.Bgra32, null, 0);
            conv.Freeze();
            Clipboard.SetImage(conv);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "nomacs", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void PasteImage()
    {
        try
        {
            if (!Clipboard.ContainsImage()) return;
            var src = Clipboard.GetImage();
            if (src == null) return;
            encToFile(src, Path.Combine(Path.GetTempPath(), $"nomacs_paste_{DateTime.Now:yyyyMMddHHmmss}.png"), "png");
            OpenPath(Path.Combine(Path.GetTempPath(), $"nomacs_paste_{DateTime.Now:yyyyMMddHHmmss}.png"));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "无法从剪贴板读取图像:\n" + ex.Message, "nomacs", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void FlipY()
    {
        if (_bitmap == null) return;
        _flipY = !_flipY;
        if (_fitToWindow) ApplyFit(); else UpdateView();
    }

    private void PickBackground()
    {
        var presets = new (string Name, Color Color)[]
        {
            ("深灰 0x30", Color.FromRgb(0x30, 0x30, 0x30)),
            ("更暗黑 0x18", Color.FromRgb(0x18, 0x18, 0x18)),
            ("浅灰 0xE0", Color.FromRgb(0xE0, 0xE0, 0xE0)),
            ("白 0xFF", Colors.White),
            ("黑 0x00", Colors.Black),
        };
        var dlg = new Window
        {
            Title = "背景颜色",
            Width = 260,
            Height = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        var list = new ListBox { FontSize = 13, Margin = new Thickness(10) };
        foreach (var p in presets)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
            var sw = new Border
            {
                Width = 18, Height = 18,
                Background = new SolidColorBrush(p.Color),
                BorderBrush = ThumbBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 0, 8, 0)
            };
            panel.Children.Add(sw);
            panel.Children.Add(new TextBlock { Text = p.Name });
            var lb = new ListBoxItem { Content = panel, Tag = p.Color };
            list.Items.Add(lb);
        }
        var ok = new Button { Content = "确定", Width = 70, Margin = new Thickness(10), IsDefault = true };
        var cancel = new Button { Content = "取消", Width = 70, Margin = new Thickness(10), IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 10, 10) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var dock = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(list);
        dock.Children.Add(buttons);
        dlg.Content = dock;
        ok.Click += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: Color c })
            {
                _viewport.Background = new SolidColorBrush(c);
                _settings.BackgroundHex = c.ToString();
                AppSettings.Save(_settings);
            }
            dlg.Close();
        };
        dlg.ShowDialog();
    }

    // ---------- context menu ----------

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        MenuItem?[] items =
        {
            MI("打开(_O)", "Ctrl+O", (_, _) => OpenDialog()),
            MI("打开目录(_D)", "Ctrl+D", (_, _) => OpenDirDialog()),
            null,
            MI("上一张(_P)", "←", (_, _) => Next(-1)),
            MI("下一张(_N)", "→", (_, _) => Next(1)),
            null,
            MI("复制(_C)", "Ctrl+C", (_, _) => CopyImage()),
            MI("顺时针90°(_0)", "R", (_, _) => Rotate(90)),
            MI("逆时针90°(_9)", "Shift+R", (_, _) => Rotate(-90)),
            MI("水平翻转(_H)", "T", (_, _) => FlipX()),
            null,
            MI("窗口尺寸适应图像(_F)", "0", (_, _) => ToggleFit()),
            MI("100%显示(_1)", "1", (_, _) => SetZoom(1.0)),
            null,
            MI("删除(_D)", "Delete", (_, _) => DeleteCurrent()),
            null,
            MI("设置(_S)...", "Ctrl+,", (_, _) => SettingsDialog()),
            MI("退出(_E)", "Esc", (_, _) => Close()),
        };
        foreach (var it in items)
        {
            if (it == null) menu.Items.Add(DarkSeparator());
            else
            {
                it.ContextMenu = menu;
                menu.Items.Add(it);
            }
        }
        return menu;
    }

    // ---------- settings ----------

    private void ApplySettings(ViewerSettings s)
    {
        _viewport.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(s.BackgroundHex));
        _slideshow.Interval = TimeSpan.FromSeconds(Math.Max(0.5, s.SlideshowSeconds));
        RenderOptions.SetBitmapScalingMode(_image, AntiAliasMode());
        _thumbsScroll.Visibility = s.ShowThumbs && _files.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _infoPanel.Visibility = s.ShowInfoPanel ? Visibility.Visible : Visibility.Collapsed;
        _metaPanel.Visibility = s.ShowMetadata ? Visibility.Visible : Visibility.Collapsed;
        _statusBar.Visibility = s.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
        _navBox.Visibility = _files.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _navBox.Margin = new Thickness(0, 0, 0, s.ShowThumbs ? 110 : 14);
        ApplyWindowChrome();
    }

    private void ApplyWindowChrome()
    {
        if (_settings.Frameless)
        {
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = WindowState.Normal;
        }
    }

    // ---------- transform ----------

    private Matrix ComputeMatrix()
    {
        if (_bitmap == null) return Matrix.Identity;
        double bw = _bitmap.PixelWidth, bh = _bitmap.PixelHeight;
        double cx = bw / 2, cy = bh / 2;
        double rad = _angle * Math.PI / 180.0;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        double zx = (_flipX ? -1 : 1) * _zoom;
        double zy = (_flipY ? -1 : 1) * _zoom;
        double m11 = zx * cos;
        double m12 = zx * sin;
        double m21 = -zy * sin;
        double m22 = zy * cos;
        double ox = -m11 * cx - m21 * cy + _viewport.ActualWidth / 2 + _panX;
        double oy = -m12 * cx - m22 * cy + _viewport.ActualHeight / 2 + _panY;
        return new Matrix(m11, m12, m21, m22, ox, oy);
    }

    private void UpdateView()
    {
        _image.RenderTransform = new MatrixTransform(ComputeMatrix());
        UpdateStatus();
    }

    private (double w, double h) RotatedSize()
    {
        if (_bitmap == null) return (0, 0);
        double rad = _angle * Math.PI / 180.0;
        double c = Math.Abs(Math.Cos(rad)), s = Math.Abs(Math.Sin(rad));
        double bw = _bitmap.PixelWidth, bh = _bitmap.PixelHeight;
        return (c * bw + s * bh, s * bw + c * bh);
    }

    private void CenterImage()
    {
        _panX = _panY = 0;
        UpdateView();
    }

    // ---------- file loading ----------

    private static readonly string[] SaveExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };

    public void OpenPath(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                _files.Clear();
                var opt = _settings.RecursiveScan
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;
                _files.AddRange(Directory.EnumerateFiles(path, "*", opt)
                    .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
                _index = _files.Count > 0 ? 0 : -1;
            }
            else if (File.Exists(path))
            {
                var dir = Path.GetDirectoryName(path) ?? "";
                _files.Clear();
                _files.AddRange(Directory.EnumerateFiles(dir, "*", _settings.RecursiveScan ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                    .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
                _index = Math.Max(0, _files.IndexOf(path));
            }
            else
            {
                MessageBox.Show(this, "文件不存在: " + path, "nomacs", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "nomacs", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        AddRecent(path);
        RebuildThumbs();
        LoadCurrent();
    }

    private void Reload()
    {
        if (_index < 0 || _index >= _files.Count) return;
        var current = _files[_index];
        var dir = Path.GetDirectoryName(current) ?? "";
        var opt = _settings.RecursiveScan ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        _files.Clear();
        _files.AddRange(Directory.EnumerateFiles(dir, "*", opt)
            .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        _index = _files.IndexOf(current);
        if (_index < 0) _index = _files.Count > 0 ? 0 : -1;
        RebuildThumbs();
        LoadCurrent();
    }

    private void LoadCurrent()
    {
        if (_index < 0 || _index >= _files.Count)
        {
            _bitmap = null;
            _image.Source = null;
            _image.RenderTransform = MatrixTransform.Identity;
            Title = "nomacs";
            UpdateStatus();
            return;
        }
        var file = _files[_index];
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(file);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            _bitmap = bmp;
        }
        catch (Exception ex)
        {
            _bitmap = null;
            if (HeifDecoder.IsHeifFile(file))
            {
                try
                {
                    _bitmap = HeifDecoder.Decode(file);
                }
                catch (Exception ex2)
                {
                    Title = "nomacs";
                    MessageBox.Show(this,
                        $"无法打开 {file}\nWIC: {ex.Message}\nlibheif: {ex2.Message}",
                        "nomacs", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateStatus();
                    return;
                }
            }
            else
            {
                Title = "nomacs";
                MessageBox.Show(this, $"无法打开 {file}\n{ex.Message}", "nomacs", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus();
                return;
            }
        }
        _image.Source = _bitmap;
        _angle = 0;
        _flipX = false;
        _flipY = false;
        if (_fitToWindow) ApplyFit(); else SetZoom(1.0, resetPan: true);
        Title = $"{Path.GetFileName(file)} - nomacs";
        HighlightThumb();
        UpdateStatus();
    }

    private void Next(int step)
    {
        if (_files.Count == 0) return;
        _index = (_index + step + _files.Count) % _files.Count;
        LoadCurrent();
    }

    private void DeleteCurrent()
    {
        if (_index < 0 || _index >= _files.Count) return;
        var file = _files[_index];
        var r = MessageBox.Show(this, $"确定删除 {file} 吗？", "nomacs", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        try
        {
            File.Delete(file);
            _files.RemoveAt(_index);
            if (_files.Count == 0)
            {
                _index = -1;
                _bitmap = null;
                _image.Source = null;
                RebuildThumbs();
                UpdateStatus();
                return;
            }
            _index = Math.Min(_index, _files.Count - 1);
            LoadCurrent();
            RebuildThumbs();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "nomacs", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------- save / rename ----------

    private void SaveFile()
    {
        if (_bitmap == null || _index < 0 || _index >= _files.Count) return;
        var ext = Path.GetExtension(_files[_index]).ToLowerInvariant();
        if (!SaveExtensions.Contains(ext))
        {
            MessageBox.Show(this, $"该格式（{ext}）暂不支持保存，请使用“另存为”。", "nomacs", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var r = MessageBox.Show(this, $"确定覆盖当前文件吗？\n{_files[_index]}", "nomacs", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        try
        {
            encToFile(_bitmap, _files[_index], ext.TrimStart('.'));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "nomacs", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        LoadCurrent();
    }

    private void SaveAsDialog()
    {
        if (_bitmap == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|BMP (*.bmp)|*.bmp|TIFF (*.tif;*.tiff)|*.tif;*.tiff",
            FileName = _index >= 0 ? Path.GetFileNameWithoutExtension(_files[_index]) + "-copy" : "untitled",
            OverwritePrompt = true
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            encToFile(_bitmap, dlg.FileName, null);
            OpenPath(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "无法保存:\n" + ex.Message, "nomacs", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void encToFile(BitmapSource src, string path, string? ext)
    {
        ext ??= Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        ext = ext.ToLowerInvariant();
        BitmapEncoder enc = ext switch
        {
            "png" => new PngBitmapEncoder(),
            "jpg" or "jpeg" => new JpegBitmapEncoder { QualityLevel = 92 },
            "bmp" => new BmpBitmapEncoder(),
            "tif" or "tiff" => new TiffBitmapEncoder(),
            _ => throw new NotSupportedException($"暂不支持保存为 .{ext}")
        };
        var bmp = src.Format == PixelFormats.Bgr24 ? src : FormatConvertedBitmap(src);
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = new FileStream(path, FileMode.Create);
        enc.Save(fs);
    }

    private static BitmapSource FormatConvertedBitmap(BitmapSource src)
    {
        var conv = new FormatConvertedBitmap(src, PixelFormats.Bgr24, null, 0);
        conv.Freeze();
        return conv;
    }

    private void RenameDialog()
    {
        if (_index < 0 || _index >= _files.Count) return;
        var current = _files[_index];
        var name = Prompt("重命名图像", "新文件名：", Path.GetFileNameWithoutExtension(current), out var ok);
        if (!ok || string.IsNullOrWhiteSpace(name)) return;
        var dir = Path.GetDirectoryName(current) ?? "";
        var target = Path.Combine(dir, name + Path.GetExtension(current));
        try
        {
            File.Move(current, target);
            _files[_index] = target;
            Title = $"{Path.GetFileName(target)} - nomacs";
            RebuildThumbs();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "无法重命名:\n" + ex.Message, "nomacs", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void GotoDialog()
    {
        if (_files.Count == 0) return;
        var s = Prompt("图像跳转", $"图像索引 (1–{_files.Count})：", "", out var ok);
        if (!ok) return;
        if (int.TryParse(s, out int idx) && idx >= 1 && idx <= _files.Count)
        {
            _index = idx - 1;
            LoadCurrent();
        }
        else
        {
            MessageBox.Show(this, $"索引必须处于 1–{_files.Count} 之间。",
                "nomacs", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private string Prompt(string title, string label, string def, out bool ok)
    {
        var dlg = new Window
        {
            Title = title, Width = 380, Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false
        };
        var sp = new StackPanel { Margin = new Thickness(12) };
        var tb = new TextBox { Text = def, Margin = new Thickness(0, 4, 0, 10) };
        sp.Children.Add(new TextBlock { Text = label });
        sp.Children.Add(tb);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var okB = new Button { Content = "确定", Width = 70, Margin = new Thickness(4), IsDefault = true };
        var cancelB = new Button { Content = "取消", Width = 70, Margin = new Thickness(4), IsCancel = true };
        row.Children.Add(okB);
        row.Children.Add(cancelB);
        sp.Children.Add(row);
        dlg.Content = sp;
        okB.Click += (_, _) => { dlg.DialogResult = true; };
        dlg.ShowDialog();
        ok = dlg.DialogResult == true;
        return tb.Text;
    }

    private void OpenDialog()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.ico;*.wdp;*.jxr;*.avif;*.heic;*.heif;*.hif|所有文件|*.*"
        };
        if (dlg.ShowDialog(this) == true) OpenPath(dlg.FileName);
    }

    private void OpenDirDialog()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "打开目录",
            Multiselect = false
        };
        if (dlg.ShowDialog(this) == true) OpenPath(dlg.FolderName);
    }

    // ---------- thumbnails ----------

    private void RebuildThumbs()
    {
        _thumbCts.Cancel();
        _thumbCts = new CancellationTokenSource();
        _thumbItems.Clear();
        _thumbsHost.Children.Clear();
        for (int i = 0; i < _files.Count; i++)
        {
            var b = new Border
            {
                Width = 120, Height = 96,
                Margin = new Thickness(3),
                BorderBrush = ThumbBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
                Tag = i,
                Cursor = Cursors.Hand
            };
            b.MouseLeftButtonUp += TumbClick;
            _thumbItems.Add(b);
            _thumbsHost.Children.Add(b);
        }
        _thumbsScroll.Visibility = _settings.ShowThumbs && _files.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _navBox.Visibility = _files.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        int n = _files.Count;
        var files = _files.ToList();
        Task.Run(() =>
        {
            for (int i = 0; i < n; i++)
            {
                if (_thumbCts.IsCancellationRequested) return;
                try
                {
                    var img = LoadThumbImage(files[i]);
                    Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            if (i < _thumbItems.Count)
                            {
                                _thumbItems[i].Child = img;
                            }
                        }
                        catch { }
                    });
                }
                catch { }
            }
        });
        HighlightThumb();
    }

    private static Image LoadThumbImage(string file)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(file);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.DecodePixelWidth = 200;
        bmp.EndInit();
        bmp.Freeze();
        var img = new Image { Source = bmp, Stretch = Stretch.Uniform, Margin = new Thickness(4) };
        return img;
    }

    private void TumbClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: int idx } && idx >= 0 && idx < _files.Count)
        {
            _index = idx;
            LoadCurrent();
        }
    }

    private void HighlightThumb()
    {
        for (int i = 0; i < _thumbItems.Count; i++)
        {
            _thumbItems[i].BorderBrush = i == _index ? ThumbHighlight : ThumbBorder;
        }
        if (_index >= 0 && _index < _thumbItems.Count)
        {
            Dispatcher.BeginInvoke(() => _thumbItems[_index].BringIntoView());
        }
    }

    // ---------- HUD / status ----------

    private void UpdateStatus()
    {
        bool has = _index >= 0 && _index < _files.Count;
        string name = has ? $"{Path.GetFileName(_files[_index])}  ({_index + 1}/{_files.Count})"
                          : "无图片 — 拖拽图片到窗口，或按 Ctrl+O 打开";

        if (_bitmap == null)
        {
            _statusFile.Content = name;
            _statusSize.Content = "";
            _statusZoom.Content = "";
            _infoPanel.Visibility = _settings.ShowInfoPanel ? Visibility.Visible : Visibility.Collapsed;
        }
        _statusFile.Content = name;
        var (w, h) = RotatedSize();
        _statusSize.Content = _bitmap == null ? "" : $"{w:0} x {h:0} px";
        _statusZoom.Content = _bitmap == null ? "" : $"{_zoom * 100:0}%";

        // info panel
        if (_bitmap != null && has)
        {
            var file = _files[_index];
            var fi = new FileInfo(file);
            string size = fi.Length > 1_000_000 ? $"{fi.Length / 1_000_000.0:0.0} MB" : $"{fi.Length / 1000.0:0} KB";
            _infoName.Text = Path.GetFileName(_files[_index]);
            _infoMeta.Text = $"{fi.LastWriteTime:yyyy-MM-dd HH:mm}   {size}";
            _infoDims.Text = $"{w:0} x {h:0} px   {_zoom * 100:0}%" + (_slideshow.IsEnabled ? "   ▶ 幻灯放映中" : "");
            _metaPanel.Visibility = _settings.ShowMetadata ? Visibility.Visible : Visibility.Collapsed;
            FillMetadata(file, size);
        }
        else
        {
            _infoName.Text = "nomacs";
            _infoMeta.Text = "";
            _infoDims.Text = "";
        }

        // zoom label
        _zoomLabel.Text = _bitmap == null ? "" : (_fitToWindow ? "适配窗口" : $"{_zoom * 100:0}%");
        ShowZoomLabel();
    }

    private void ShowZoomLabel()
    {
        if (_bitmap == null) return;
        var fade = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromMilliseconds(600)))
        {
            BeginTime = TimeSpan.FromMilliseconds(900)
        };
        foreach (var child in ((Grid)Content).Children)
        {
            if (child is Border b && b.Child == _zoomLabel)
            {
                b.BeginAnimation(OpacityProperty, fade);
            }
        }
    }

    private void FillMetadata(string file, string size)
    {
        _metaList.Items.Clear();
        var fi = new FileInfo(file);
        var (w, h) = RotatedSize();
        void Add(string k, object v) => _metaList.Items.Add(new string[] { k, v?.ToString() ?? "" });
        var has = _index >= 0 && _index < _files.Count;
        if (!has) return;
        Add("文件名", Path.GetFileName(file));
        Add("文件夹", Path.GetDirectoryName(file) ?? "");
        Add("格式", Path.GetExtension(file).Substring(1).ToUpperInvariant());
        Add("文件大小", size);
        Add("修改日期", fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
        Add("创建日期", fi.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"));
        if (_bitmap != null)
        {
            Add("图像尺寸", $"{w:0} x {h:0} px");
            Add("像素格式", _bitmap.Format.ToString());
            if (_bitmap.Metadata is BitmapMetadata m)
            {
                try
                {
                    if (m.CameraModel != null) Add("相机型号", m.CameraModel);
                    if (m.CameraManufacturer != null) Add("相机制造商", m.CameraManufacturer);
                    if (m.DateTaken != null) Add("拍摄日期", m.DateTaken);
                    if (m.Title != null) Add("标题", m.Title);
                }
                catch { }
            }
        }
    }

    // ---------- input ----------

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (_bitmap == null) return;
        var pos = e.GetPosition(_viewport);
        var m = ComputeMatrix();
        m.Invert();
        var imgPt = m.Transform(pos);
        var factor = e.Delta > 0 ? 1.25 : 0.8;
        _zoom = Math.Clamp(_zoom * factor, 0.02, 32);
        _fitToWindow = false;
        var screenPt = ComputeMatrix().Transform(imgPt);
        _panX += pos.X - screenPt.X;
        _panY += pos.Y - screenPt.Y;
        UpdateView();
        ShowZoomLabel();
    }

    private void OnPanStart(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ToggleFit(); return; }
        if (_bitmap == null) return;
        _dragging = true;
        _dragStart = e.GetPosition(_viewport);
        _viewport.CaptureMouse();
        _viewport.Focus();
    }

    private void OnPanMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var pos = e.GetPosition(_viewport);
        _panX += pos.X - _dragStart.X;
        _panY += pos.Y - _dragStart.Y;
        _dragStart = pos;
        UpdateView();
    }

    private void OnPanEnd(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        _viewport.ReleaseMouseCapture();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Right or Key.Down or Key.Space or Key.PageDown:
                Next(1); break;
            case Key.Left or Key.Up or Key.Back or Key.PageUp:
                Next(-1); break;
            case Key.Home:
                if (_files.Count > 0) { _index = 0; LoadCurrent(); } break;
            case Key.End:
                if (_files.Count > 0) { _index = _files.Count - 1; LoadCurrent(); } break;
            case Key.Add or Key.OemPlus:
                SetZoom(_zoom * 1.25); break;
            case Key.Subtract or Key.OemMinus:
                SetZoom(_zoom * 0.8); break;
            case Key.D0 or Key.NumPad0:
                ToggleFit(); break;
            case Key.D1 or Key.NumPad1:
                SetZoom(1.0); break;
            case Key.R when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                Rotate(-90); break;
            case Key.R or Key.F:
                Rotate(90); break;
            case Key.T when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                FlipY(); break;
            case Key.T when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                {
                    _settings.ShowThumbs = !_settings.ShowThumbs;
                    AppSettings.Save(_settings);
                    ApplySettings(_settings);
                    break;
                }
            case Key.T:
                FlipX(); break;
            case Key.Delete:
                DeleteCurrent(); break;
            case Key.F5 when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                if (_files.Count > 0) { _slideshow.IsEnabled = true; UpdateStatus(); } break;
            case Key.F5:
                _slideshow.IsEnabled = !_slideshow.IsEnabled; UpdateStatus(); break;
            case Key.F6:
                Reload(); break;
            case Key.F11:
                ToggleFullscreen(); break;
            case Key.F2:
                RenameDialog(); break;
            case Key.F1:
                ShortcutsDialog(); break;
            case Key.H when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                Rotate(180); break;
            case Key.B:
                PickBackground(); break;
            case Key.I:
                {
                    _settings.ShowInfoPanel = !_settings.ShowInfoPanel;
                    AppSettings.Save(_settings);
                    ApplySettings(_settings);
                    break;
                }
            case Key.M when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                {
                    _settings.ShowMetadata = !_settings.ShowMetadata;
                    AppSettings.Save(_settings);
                    ApplySettings(_settings);
                    break;
                }
            case Key.OemComma when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                SettingsDialog(); break;
            case Key.O when Keyboard.Modifiers == ModifierKeys.Control:
                OpenDialog(); break;
            case Key.D when Keyboard.Modifiers == ModifierKeys.Control:
                OpenDirDialog(); break;
            case Key.S when Keyboard.Modifiers == ModifierKeys.Control:
                SaveFile(); break;
            case Key.S when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift):
                SaveAsDialog(); break;
            case Key.C when Keyboard.Modifiers == ModifierKeys.Control:
                CopyImage(); break;
            case Key.V when Keyboard.Modifiers == ModifierKeys.Control:
                PasteImage(); break;
            case Key.G when Keyboard.Modifiers == ModifierKeys.Control:
                GotoDialog(); break;
            case Key.Escape:
                if (_slideshow.IsEnabled) { _slideshow.IsEnabled = false; UpdateStatus(); }
                else if (_settings.Frameless || WindowState == WindowState.Maximized) ToggleFullscreen();
                else Close();
                break;
        }
    }

    private void SetZoom(double zoom, bool resetPan = false)
    {
        if (_bitmap == null) return;
        _fitToWindow = false;
        _zoom = Math.Clamp(zoom, 0.02, 32);
        if (resetPan) { _panX = _panY = 0; }
        UpdateView();
        ShowZoomLabel();
    }

    private void ApplyFit()
    {
        if (_bitmap == null || _viewport.ActualWidth < 1) return;
        var (w, h) = RotatedSize();
        _zoom = Math.Clamp(Math.Min(_viewport.ActualWidth / w, _viewport.ActualHeight / h), 0.02, 32);
        _panX = _panY = 0;
        UpdateView();
        ShowZoomLabel();
    }

    private void ToggleFit()
    {
        if (_bitmap == null) return;
        _fitToWindow = !_fitToWindow;
        if (_fitToWindow) ApplyFit(); else SetZoom(1.0, resetPan: true);
    }

    private void Rotate(double angle)
    {
        if (_bitmap == null) return;
        _angle = (_angle + angle) % 360;
        if (_angle < 0) _angle += 360;
        if (_fitToWindow) ApplyFit(); else UpdateView();
    }

    private void FlipX()
    {
        if (_bitmap == null) return;
        _flipX = !_flipX;
        UpdateView();
    }

    private void ToggleFullscreen()
    {
        if (_settings.Frameless)
        {
            _settings.Frameless = false;
            AppSettings.Save(_settings);
            ApplyWindowChrome();
            return;
        }
        WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
    }

    // ---------- recent files ----------

    private void AddRecent(string path)
    {
        _settings.RecentFiles.Remove(path);
        _settings.RecentFiles.Insert(0, path);
        if (_settings.RecentFiles.Count > 10)
            _settings.RecentFiles.RemoveRange(10, _settings.RecentFiles.Count - 10);
        AppSettings.Save(_settings);
        UpdateRecentMenu();
    }

    private void UpdateRecentMenu()
    {
        if (_recentMenu == null) return;
        _recentMenu.Items.Clear();
        if (_settings.RecentFiles.Count == 0)
        {
            var empty = MI("（无）", null, null);
            empty.IsEnabled = false;
            _recentMenu.Items.Add(empty);
            return;
        }
        foreach (var f in _settings.RecentFiles.ToList())
        {
            var p = f;
            _recentMenu.Items.Add(MI(Path.GetFileName(p), null, (_, _) => OpenPath(p)));
        }
    }

    // ---------- dialogs ----------

    private void SettingsDialog()
    {
        var dlg = new Window
        {
            Title = "设置", Width = 420, Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false
        };
        var sp = new StackPanel { Margin = new Thickness(14) };

        sp.Children.Add(new TextBlock { Text = "显示", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 6) });
        var bgBox = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
        var presets = new (string Name, string Hex)[]
        {
            ("深灰 0x30", "#FF303030"), ("更暗黑", "#FF181818"), ("纯黑", "#FF000000"),
            ("浅灰", "#FFE0E0E0"), ("白", "#FFFFFFFF"),
        };
        int sel = Array.FindIndex(presets, p => p.Hex.Equals(_settings.BackgroundHex, StringComparison.OrdinalIgnoreCase));
        foreach (var p in presets)
        {
            var cbi = new ComboBoxItem { Content = p.Name, Tag = p.Hex };
            bgBox.Items.Add(cbi);
        }
        bgBox.SelectedIndex = Math.Max(0, sel);
        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 6) };
        row1.Children.Add(new TextBlock { Text = "背景颜色：", VerticalAlignment = VerticalAlignment.Center, Width = 90 });
        row1.Children.Add(bgBox);
        sp.Children.Add(row1);

        var anti = new CheckBox { Content = "以高质量抗锯齿显示图像（缩放时可能变慢）", IsChecked = _settings.AntiAlias };
        sp.Children.Add(anti);

        sp.Children.Add(new TextBlock { Text = "幻灯片放映", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 6) });
        var rowS = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 6) };
        var secBox = new TextBox { Width = 60, Text = _settings.SlideshowSeconds.ToString("0.#") };
        rowS.Children.Add(new TextBlock { Text = "每张停留时间（秒）：", VerticalAlignment = VerticalAlignment.Center });
        rowS.Children.Add(secBox);
        sp.Children.Add(rowS);

        sp.Children.Add(new TextBlock { Text = "文件", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 6) });
        var rec = new CheckBox { Content = "递归扫描文件夹（打开目录时包含子文件夹）", IsChecked = _settings.RecursiveScan };
        sp.Children.Add(rec);

        var note = new TextBlock
        {
            Text = "提示：部分设置需要重新打开文件夹/重启程序后生效。",
            Foreground = GestureText,
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        sp.Children.Add(note);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var okB = new Button { Content = "确定", Width = 70, Margin = new Thickness(4), IsDefault = true };
        var cancelB = new Button { Content = "取消", Width = 70, Margin = new Thickness(4), IsCancel = true };
        buttons.Children.Add(okB);
        buttons.Children.Add(cancelB);
        sp.Children.Add(buttons);

        dlg.Content = sp;
        okB.Click += (_, _) =>
        {
            double.TryParse(secBox.Text, out var sec);
            _settings.SlideshowSeconds = Math.Clamp(sec <= 0 ? 3 : sec, 0.5, 60);
            _settings.AntiAlias = anti.IsChecked == true;
            _settings.RecursiveScan = rec.IsChecked == true;
            if (bgBox.SelectedItem is ComboBoxItem { Tag: string hex })
                _settings.BackgroundHex = hex;
            AppSettings.Save(_settings);
            ApplySettings(_settings);
            dlg.Close();
        };
        okB.IsDefault = true;
        dlg.ShowDialog();
    }

    private void PluginsDialog()
    {
        MessageBox.Show(this,
            "无已安装插件。\n\n此 FOSS 复刻版暂不支持菜单扩展的 nomacs 插件。",
            "插件管理器", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AboutDialog()
    {
        MessageBox.Show(this,
            "nomacs  3.22.4 （FOSS 复刻版）\n\n" +
            "纯 C# / .NET 8 WPF 从零实现的 nomacs 风格图片查看器。\n" +
            "AVIF/HEIF 解码由内置 libheif 提供。\n\n" +
            "nomacs 原作: www.nomacs.org （GPL v3）\n" +
            "复刻版同以 GPL v3+ 发布。",
            "关于 Nomacs", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShortcutsDialog()
    {
        var rows = new (string k, string v)[]
        {
            ("打开文件", "Ctrl+O"), ("打开目录", "Ctrl+D"),
            ("上一张 / 下一张", "← → 或 ↑ ↓（Space / PgUp / PgDn）"),
            ("首张 / 末张", "Home / End"),
            ("放大 / 缩小", "+ / − (Ctrl+滚轮也无妨)"),
            ("适配窗口 / 原始尺寸", "0（或双击）/ 1"),
            ("顺时针 / 逆时针旋转", "R 或 F / Shift+R"),
            ("水平翻转 / 垂直翻转", "T / Shift+T"),
            ("复制图像 / 粘贴", "Ctrl+C / Ctrl+V"),
            ("重命名 / 跳转", "F2 / Ctrl+G"),
            ("保存 / 另存为", "Ctrl+S / Ctrl+Shift+S"),
            ("重新加载", "F6"),
            ("删除当前图", "Delete"),
            ("幻灯播放", "F5（Shift+F5 播放）"),
            ("全屏", "F11 或 Ctrl+Shift+F"),
            ("缩略图 / 信息 / 元数据", "Ctrl+T / I / Ctrl+M"),
            ("设置", "Ctrl+,"),
            ("帮助", "F1"),
            ("退出", "Esc"),
        };
        var dlg = new Window
        {
            Title = "快捷键", Width = 480, Height = 600,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
            ShowInTaskbar = false
        };
        var sp = new StackPanel { Margin = new Thickness(14) };
        foreach (var (k, v) in rows)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = k });
            var right = new TextBlock { Text = v, Foreground = GestureText, Margin = new Thickness(16, 0, 0, 0) };
            Grid.SetColumn(right, 1);
            row.Children.Add(right);
            sp.Children.Add(row);
        }
        dlg.Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        dlg.ShowDialog();
    }
}
