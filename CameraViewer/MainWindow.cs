using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CameraViewer;
using Application = System.Windows.Application;

namespace CameraViewerDotnet
{
    public class MainWindow : Window
    {
        private readonly UniformGrid grid;
        private readonly Grid container;
        private readonly Border maxHost;
        private readonly TextBlock timeText;
        private readonly TextBlock titleText;
        private readonly NotifyIcon notifyIcon;
        private readonly List<CameraCell> cells = new List<CameraCell>();
        private readonly DispatcherTimer timer;
        private CameraCell maximizedCell;

        private static readonly Uri IconUri = new Uri("pack://application:,,,/icon.ico");

        public static ImageSource LoadAppIcon()
        {
            var s = Application.GetResourceStream(IconUri)?.Stream;
            if (s == null) return null;
            return new IconBitmapDecoder(s, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        }

        public static System.Drawing.Icon LoadTrayIcon()
        {
            var s = Application.GetResourceStream(IconUri)?.Stream;
            return s == null ? System.Drawing.SystemIcons.Application : new System.Drawing.Icon(s);
        }

        public MainWindow()
        {
            Title = I18n.T("appTitle");
            Icon = LoadAppIcon();
            Width = 1200;
            Height = 800;
            WindowState = WindowState.Maximized;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            Background = (Brush)Application.Current.Resources["BgBrush"];
            MinHeight = 400;
            MinWidth = 600;

            container = new Grid();
            container.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
            container.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            container.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });

            var toolbar = new Border
            {
                Background = (Brush)Application.Current.Resources["BgBrush2"],
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(0, 0, 0, 1),
            };
            var tbGrid = new Grid { Margin = new Thickness(8, 0, 8, 0) };
            tbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            tbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            titleText = new TextBlock
            {
                Text = I18n.T("appTitle"),
                Foreground = (Brush)Application.Current.Resources["FgBrush"],
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
            };
            tbGrid.Children.Add(titleText);

            var btns = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(btns, 2);

            settingsBtn = MakeButton(AntIcon.Setting, I18n.T("setting"));
            settingsBtn.Click += (s, e) =>
            {
                var dlg = new SettingsWindow { Owner = this };
                dlg.ShowDialog();
            };
            btns.Children.Add(settingsBtn);

            themeBtn = MakeButton("", "");
            themeBtn.Click += (s, e) =>
            {
                var next = ThemeManager.Theme == "dark" ? "light" : "dark";
                ThemeManager.Apply(next);
                ConfigService.App.theme = next;
                ConfigService.SaveApp();
                UpdateToolbarButtons();
                Background = (Brush)Application.Current.Resources["BgBrush"];
            };
            btns.Children.Add(themeBtn);

            langBtn = MakeButton(AntIcon.Global, I18n.Language == "en" ? I18n.T("languageZh") : I18n.T("languageEn"));
            langBtn.Click += (s, e) =>
            {
                I18n.SetLanguage(I18n.Language == "en" ? "zh" : "en");
                ConfigService.App.language = I18n.Language;
                ConfigService.SaveApp();
                UpdateToolbarButtons();
            };
            btns.Children.Add(langBtn);

            aboutBtn = MakeButton(AntIcon.InfoCircle, I18n.T("about"));
            aboutBtn.Click += (s, e) =>
            {
                var dlg = new AboutWindow { Owner = this };
                dlg.ShowDialog();
            };
            btns.Children.Add(aboutBtn);
            UpdateToolbarButtons();

            tbGrid.Children.Add(btns);
            toolbar.Child = tbGrid;
            container.Children.Add(toolbar);

            var center = new Grid();
            Grid.SetRow(center, 1);

            grid = new UniformGrid { Margin = new Thickness(4) };
            center.Children.Add(grid);

            maxHost = new Border { Visibility = Visibility.Collapsed, Margin = new Thickness(4) };
            center.Children.Add(maxHost);

            container.Children.Add(center);

            var status = new Border
            {
                Background = (Brush)Application.Current.Resources["BgBrush2"],
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(0, 1, 0, 0),
            };
            var sp = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            timeText = new TextBlock { Foreground = (Brush)Application.Current.Resources["FgDimBrush"], FontSize = 12 };
            var versionText = new TextBlock
            {
                Text = I18n.T("version") + " " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? ""),
                Foreground = (Brush)Application.Current.Resources["FgDimBrush"],
                FontSize = 12,
                Margin = new Thickness(16, 0, 0, 0),
            };
            sp.Children.Add(timeText);
            sp.Children.Add(versionText);
            status.Child = sp;
            Grid.SetRow(status, 2);
            container.Children.Add(status);

            Content = container;

            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) => UpdateTime();
            timer.Start();
            UpdateTime();

            notifyIcon = new NotifyIcon
            {
                Text = I18n.T("appTitle"),
                Icon = LoadTrayIcon(),
                Visible = true,
            };
            var menu = new ContextMenuStrip();
            var showItem = new ToolStripMenuItem(I18n.T("showWindow"));
            showItem.Click += (s, e) => ShowFromTray();
            var exitItem = new ToolStripMenuItem(I18n.T("exit"));
            exitItem.Click += (s, e) => Application.Current.Shutdown();
            menu.Items.Add(showItem);
            menu.Items.Add(exitItem);
            notifyIcon.ContextMenuStrip = menu;
            notifyIcon.DoubleClick += (s, e) => ShowFromTray();
            notifyIcon.Tag = showItem;

            Closing += (s, e) =>
            {
                e.Cancel = true;
                Hide();
            };

            I18n.LanguageChanged += OnLanguageChanged;
            Loaded += (s, e) => RebuildGrid();

            RebuildGrid();
        }

        private readonly System.Windows.Controls.Button settingsBtn;
        private readonly System.Windows.Controls.Button themeBtn;
        private readonly System.Windows.Controls.Button langBtn;
        private readonly System.Windows.Controls.Button aboutBtn;

        private void UpdateToolbarButtons()
        {
            settingsBtn.Content = AntIcon.Content(AntIcon.Setting, I18n.T("setting"));
            themeBtn.Content = ThemeManager.Theme == "dark"
                ? AntIcon.Content(AntIcon.Sun, I18n.T("themeLight"))
                : AntIcon.Content(AntIcon.Moon, I18n.T("themeDark"));
            langBtn.Content = AntIcon.Content(AntIcon.Global, I18n.Language == "en" ? I18n.T("languageZh") : I18n.T("languageEn"));
            aboutBtn.Content = AntIcon.Content(AntIcon.InfoCircle, I18n.T("about"));
        }

        private static System.Windows.Controls.Button MakeButton(string iconData, string text) =>
            new System.Windows.Controls.Button { Content = AntIcon.Content(iconData, text), Height = 28, MinWidth = 64, Margin = new Thickness(4, 0, 0, 0) };

        private void UpdateTime() =>
            timeText.Text = I18n.T("systemTime") + " " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        private void OnLanguageChanged()
        {
            Title = I18n.T("appTitle");
            titleText.Text = I18n.T("appTitle");
            notifyIcon.Text = I18n.T("appTitle");
            UpdateToolbarButtons();
            UpdateTime();
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Maximized;
            Activate();
        }

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

        public void RebuildGrid()
        {
            foreach (var c in cells)
            {
                c.ToggleMaximize -= OnToggleMaximize;
                if (maximizedCell == c)
                {
                    maxHost.Child = null;
                    maxHost.Visibility = Visibility.Collapsed;
                    maximizedCell = null;
                }
            }
            cells.Clear();
            grid.Children.Clear();

            var count = ConfigService.Camera.count;
            var layout = Layouts.TryGetValue(count, out var l)
                ? l
                : (cols: (int)Math.Ceiling(Math.Sqrt(count)), rows: (int)Math.Ceiling(Math.Sqrt(count)));
            grid.Columns = layout.cols;
            grid.Rows = layout.rows;

            var delay = ConfigService.Camera.delay;
            for (int i = 0; i < count; i++)
            {
                var cell = new CameraCell(i);
                cell.ToggleMaximize += OnToggleMaximize;
                cells.Add(cell);
                grid.Children.Add(cell);

                var idx = i;
                var ms = (long)idx * delay * 1000;
                if (ms <= 0)
                {
                    _ = cell.ReloadAsync();
                }
                else
                {
                    var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
                    t.Tick += (s, e) =>
                    {
                        t.Stop();
                        _ = cell.ReloadAsync();
                    };
                    t.Start();
                }
            }
        }

        private void OnToggleMaximize(CameraCell cell)
        {
            if (maximizedCell == null)
            {
                maximizedCell = cell;
                grid.Children.Remove(cell);
                grid.Visibility = Visibility.Collapsed;
                maxHost.Child = cell;
                maxHost.Visibility = Visibility.Visible;
            }
            else
            {
                maxHost.Child = null;
                maxHost.Visibility = Visibility.Collapsed;
                grid.Children.Add(maximizedCell);
                grid.Visibility = Visibility.Visible;
                maximizedCell = null;
            }
        }
    }
}
