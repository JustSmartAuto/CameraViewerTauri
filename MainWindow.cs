using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NomacsNet;

public class MainWindow : Window
{
    private static readonly string[] Extensions =
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".ico", ".wdp", ".jxr", ".avif", ".heic", ".heif", ".heifs", ".hif", ".dng", ".cr2", ".nef", ".arw" };

    private readonly Border _viewport;
    private readonly Image _image;
    private readonly StatusBarItem _statusFile, _statusZoom, _statusSize;
    private readonly DispatcherTimer _slideshow = new() { Interval = TimeSpan.FromSeconds(3) };

    private readonly List<string> _files = new();
    private int _index = -1;
    private BitmapSource? _bitmap;
    private double _zoom = 1.0;
    private double _angle;
    private bool _flipX;
    private double _panX, _panY;
    private bool _fitToWindow = true;
    private Point _dragStart;
    private bool _dragging;

    public MainWindow()
    {
        Title = "nomacs";
        Width = 1100; Height = 750;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30));
        Icon = LoadIcon();

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _image = new Image
        {
            Stretch = Stretch.None,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);

        _viewport = new Border
        {
            Background = Brushes.Transparent,
            ClipToBounds = true,
            Focusable = true,
            Child = _image
        };
        grid.Children.Add(_viewport);

        var statusBar = new StatusBar { Background = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)) };
        _statusFile = MakeStatusItem();
        _statusSize = MakeStatusItem();
        _statusZoom = MakeStatusItem();
        _statusFile.Content = "无图片 — 拖拽图片到窗口，或按 Ctrl+O 打开";
        statusBar.Items.Add(_statusFile);
        Separator sep = new();
        statusBar.Items.Add(sep);
        statusBar.Items.Add(_statusSize);
        statusBar.Items.Add(_statusZoom);
        var hint = new StatusBarItem { Content = "F11 全屏  F5 幻灯  双击 适配  右键 菜单", HorizontalAlignment = HorizontalAlignment.Right };
        statusBar.Items.Add(hint);
        Grid.SetRow(statusBar, 1);
        grid.Children.Add(statusBar);

        Content = grid;
        _viewport.ContextMenu = BuildMenu();

        _slideshow.Tick += (_, _) => Next(1);
        _viewport.MouseWheel += OnWheel;
        _viewport.MouseLeftButtonDown += OnPanStart;
        _viewport.MouseLeftButtonUp += OnPanEnd;
        _viewport.MouseMove += OnPanMove;
        KeyDown += OnKey;
        SizeChanged += (_, _) => { if (_fitToWindow) ApplyFit(); };
        Closing += (_, _) => _slideshow.IsEnabled = false;
        AllowDrop = true;
        Drop += (_, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
                e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
                OpenPath(files[0]);
        };

        UpdateStatus();
    }

    private ContextMenu BuildMenu()
    {
        MenuItem Item(string header, RoutedEventHandler click, string? gesture = null)
            => new() { Header = header, InputGestureText = gesture ?? "" };

        var menu = new ContextMenu();
        var open = Item("打开… (Ctrl+O)", (_, _) => OpenDialog());
        var prev = Item("上一张 (←)", (_, _) => Next(-1));
        var next = Item("下一张 (→)", (_, _) => Next(1));
        var fit = Item("适配窗口 (0 / 双击)", (_, _) => ToggleFit());
        var one = Item("原始尺寸 (1)", (_, _) => SetZoom(1.0));
        var cw = Item("旋转 90° (R)", (_, _) => Rotate(90));
        var ccw = Item("旋转 -90° (Shift+R)", (_, _) => Rotate(-90));
        var flip = Item("水平镜像 (T)", (_, _) => FlipX());
        var slide = Item("幻灯播放 (F5)", (_, _) => { _slideshow.IsEnabled = !_slideshow.IsEnabled; UpdateStatus(); });
        var full = Item("全屏 (F11)", (_, _) => ToggleFullscreen());
        var del = Item("删除 (Del)", (_, _) => DeleteCurrent());
        var exit = Item("退出 (Esc)", (_, _) => Close());
        foreach (var mi in new object[] { open, new Separator(), prev, next, fit, one, cw, ccw, flip, new Separator(), slide, full, del, new Separator(), exit })
            menu.Items.Add(mi);
        return menu;
    }

    private static StatusBarItem MakeStatusItem() => new() { Padding = new Thickness(10, 2, 10, 2) };

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

    // ---------- transform ----------

    private Matrix ComputeMatrix()
    {
        if (_bitmap == null) return Matrix.Identity;
        double bw = _bitmap.PixelWidth, bh = _bitmap.PixelHeight;
        double cx = bw / 2, cy = bh / 2;
        double s = _flipX ? -1 : 1;
        double rad = _angle * Math.PI / 180.0;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        double m11 = _zoom * s * cos;
        double m12 = _zoom * s * sin;
        double m21 = -_zoom * sin;
        double m22 = _zoom * cos;
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

    public void OpenPath(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                _files.Clear();
                _files.AddRange(Directory.EnumerateFiles(path)
                    .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
                _index = _files.Count > 0 ? 0 : -1;
            }
            else if (File.Exists(path))
            {
                var dir = Path.GetDirectoryName(path)!;
                _files.Clear();
                _files.AddRange(Directory.EnumerateFiles(dir)
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
        LoadCurrent();
    }

    private void LoadCurrent()
    {
        if (_index < 0 || _index >= _files.Count) return;
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
            _image.Source = bmp;
            _angle = 0;
            _flipX = false;
            if (_fitToWindow) ApplyFit(); else SetZoom(1.0);
            Title = $"{Path.GetFileName(file)} - nomacs";
        }
        catch (Exception ex)
        {
            _bitmap = null;
            _image.Source = null;
            _image.RenderTransform = MatrixTransform.Identity;
            Title = "nomacs";
            // WIC 打开失败时，对 AVIF/HEIC 尝试 libheif 解码
            if (HeifDecoder.IsHeifFile(file))
            {
                try
                {
                    _bitmap = HeifDecoder.Decode(file);
                    _image.Source = _bitmap;
                    if (_fitToWindow) ApplyFit(); else SetZoom(1.0);
                    Title = $"{Path.GetFileName(file)} - nomacs";
                    UpdateStatus();
                    return;
                }
                catch (Exception ex2)
                {
                    MessageBox.Show(this,
                        $"无法打开 {file}\nWIC: {ex.Message}\nlibheif: {ex2.Message}",
                        "nomacs", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateStatus();
                    return;
                }
            }
            MessageBox.Show(this, $"无法打开 {file}\n{ex.Message}", "nomacs", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
            if (_files.Count == 0) { _index = -1; _bitmap = null; _image.Source = null; UpdateStatus(); return; }
            _index = Math.Min(_index, _files.Count - 1);
            LoadCurrent();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "nomacs", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------- view commands ----------

    private void SetZoom(double zoom)
    {
        if (_bitmap == null) return;
        _fitToWindow = false;
        _zoom = Math.Clamp(zoom, 0.02, 32);
        CenterImage();
    }

    private void ApplyFit()
    {
        if (_bitmap == null || _viewport.ActualWidth < 1) return;
        var (w, h) = RotatedSize();
        _zoom = Math.Clamp(Math.Min(_viewport.ActualWidth / w, _viewport.ActualHeight / h), 0.02, 32);
        CenterImage();
    }

    private void ToggleFit()
    {
        if (_bitmap == null) return;
        _fitToWindow = !_fitToWindow;
        if (_fitToWindow) ApplyFit(); else SetZoom(1.0);
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
        WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
        WindowStyle = WindowStyle == WindowStyle.None ? WindowStyle.SingleBorderWindow : WindowStyle.None;
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
            case Key.T:
                FlipX(); break;
            case Key.Delete:
                DeleteCurrent(); break;
            case Key.F5:
                _slideshow.IsEnabled = !_slideshow.IsEnabled; UpdateStatus(); break;
            case Key.F11:
                ToggleFullscreen(); break;
            case Key.Escape:
                if (_slideshow.IsEnabled) { _slideshow.IsEnabled = false; UpdateStatus(); }
                else if (WindowState == WindowState.Maximized) ToggleFullscreen();
                else Close();
                break;
            case Key.O when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                OpenDialog(); break;
        }
    }

    private void OpenDialog()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.ico;*.wdp;*.jxr;*.avif;*.heic;*.heif;*.hif|" + "所有文件|*.*"
        };
        if (dlg.ShowDialog(this) == true) OpenPath(dlg.FileName);
    }

    private void UpdateStatus()
    {
        _statusFile.Content = _index >= 0 && _index < _files.Count
            ? $"{Path.GetFileName(_files[_index])}  ({_index + 1}/{_files.Count})"
            : "无图片 — 拖拽图片到窗口，或按 Ctrl+O 打开";
        var (w, h) = RotatedSize();
        _statusSize.Content = _bitmap == null ? "" : $"{w:0} x {h:0} px";
        _statusZoom.Content = _bitmap == null ? "" : $"{_zoom * 100:0}%";
    }
}
