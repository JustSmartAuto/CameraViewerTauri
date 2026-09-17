using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using CameraViewer;
using Application = System.Windows.Application;

namespace CameraViewerDotnet
{
    public class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            Title = I18n.T("systemSettings");
            Width = 720;
            Height = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = (System.Windows.Media.Brush)Application.Current.Resources["BgBrush"];
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["FgBrush"];

            var tabs = new TabControl { Margin = new Thickness(8) };

            tabs.Items.Add(MakePage(I18n.T("cameraDisplay"), BuildDisplayPage()));
            tabs.Items.Add(MakePage(I18n.T("cameraSettings"), BuildCameraPage()));
            tabs.Items.Add(MakePage(I18n.T("displaySettings"), BuildThemePage()));
            tabs.Items.Add(MakePage(I18n.T("appSettings"), BuildLanguagePage()));
            tabs.Items.Add(MakePage(I18n.T("jobxBackup"), BuildJobxPage()));

            Content = tabs;

            I18n.LanguageChanged += OnLanguageChanged;
            Closed += (s, e) =>
            {
                I18n.LanguageChanged -= OnLanguageChanged;
                jobxLogTimer?.Stop();
            };
        }

        private DataGrid jobxGrid;
        private TextBox jobxLog;
        private TextBlock jobxLogLabel;
        private Button jobxAddBtn, jobxBackupBtn, jobxBackupAllBtn, jobxOpenDirBtn;
        private readonly List<KeyValuePair<DataGridTextColumn, string>> jobxColumns = new List<KeyValuePair<DataGridTextColumn, string>>();
        private DispatcherTimer jobxLogTimer;

        private void OnLanguageChanged()
        {
            Title = I18n.T("systemSettings");
            if (Content is TabControl tabs)
            {
                var names = new[] { I18n.T("cameraDisplay"), I18n.T("cameraSettings"), I18n.T("displaySettings"), I18n.T("appSettings"), I18n.T("jobxBackup") };
                for (int i = 0; i < tabs.Items.Count && i < names.Length; i++)
                    ((TabItem)tabs.Items[i]).Header = names[i];
            }
            UpdateJobxTexts();
        }

        private void UpdateJobxTexts()
        {
            if (jobxAddBtn == null) return;
            jobxAddBtn.Content = I18n.T("addCamera");
            jobxBackupBtn.Content = I18n.T("backup");
            jobxBackupAllBtn.Content = I18n.T("backupAll");
            jobxOpenDirBtn.Content = I18n.T("openDir");
            jobxLogLabel.Text = I18n.T("log");
            foreach (var kv in jobxColumns)
                kv.Key.Header = I18n.T(kv.Value);
        }

        private static TabItem MakePage(string header, UIElement content) =>
            new TabItem { Header = header, Content = content };

        private static TextBlock Label(string text) =>
            new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };

        private UIElement BuildDisplayPage()
        {
            var panel = new StackPanel { Margin = new Thickness(16) };

            var countRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            countRow.Children.Add(Label(I18n.T("count")));
            var countBox = new ComboBox { Width = 80 };
            foreach (var n in new[] { 1, 2, 4, 6, 9, 12, 16 })
                countBox.Items.Add(n);
            countBox.SelectedItem = ConfigService.Camera.count;
            countBox.SelectionChanged += (s, e) =>
            {
                if (countBox.SelectedItem is int n)
                {
                    ConfigService.Camera.count = n;
                    ConfigService.SaveCamera();
                    (Application.Current.MainWindow as MainWindow)?.RebuildGrid();
                }
            };
            countRow.Children.Add(countBox);
            panel.Children.Add(countRow);

            var delayRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            delayRow.Children.Add(Label(I18n.T("delay")));
            var delayBox = new TextBox { Width = 80, Text = ConfigService.Camera.delay.ToString() };
            delayBox.LostFocus += (s, e) =>
            {
                if (int.TryParse(delayBox.Text, out var d) && d >= 0)
                {
                    ConfigService.Camera.delay = d;
                    ConfigService.SaveCamera();
                }
                else
                {
                    delayBox.Text = ConfigService.Camera.delay.ToString();
                }
            };
            delayRow.Children.Add(delayBox);
            panel.Children.Add(delayRow);

            return panel;
        }

        private UIElement BuildCameraPage()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var panel = new StackPanel { Margin = new Thickness(16) };

            for (int i = 0; i < ConfigService.Camera.count; i++)
            {
                var item = ConfigService.EnsureItem(i);
                var border = new Border
                {
                    BorderBrush = (System.Windows.Media.Brush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 0, 8),
                    Padding = new Thickness(8),
                };
                var inner = new StackPanel();

                var header = new TextBlock
                {
                    Text = I18n.T("camera") + " " + (i + 1),
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 6),
                };
                inner.Children.Add(header);

                var urlRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                urlRow.Children.Add(Label(I18n.T("url")));
                var urlBox = new TextBox { Width = 320, Text = item.ip, IsReadOnly = item.locked };
                urlBox.LostFocus += (s, e) =>
                {
                    item.ip = urlBox.Text;
                    ConfigService.SaveCamera();
                };
                urlRow.Children.Add(urlBox);
                inner.Children.Add(urlRow);

                var remarkRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                remarkRow.Children.Add(Label(I18n.T("remark")));
                var remarkBox = new TextBox { Width = 320, Text = item.remark, IsReadOnly = item.locked };
                remarkBox.LostFocus += (s, e) =>
                {
                    item.remark = remarkBox.Text;
                    ConfigService.SaveCamera();
                };
                remarkRow.Children.Add(remarkBox);
                inner.Children.Add(remarkRow);

                var lockRow = new StackPanel { Orientation = Orientation.Horizontal };
                var lockBox = new CheckBox { Content = I18n.T("locked"), IsChecked = item.locked };
                lockBox.Checked += (s, e) => SetLock(item, true, urlBox, remarkBox);
                lockBox.Unchecked += (s, e) => SetLock(item, false, urlBox, remarkBox);
                lockRow.Children.Add(lockBox);
                inner.Children.Add(lockRow);

                border.Child = inner;
                panel.Children.Add(border);
            }

            scroll.Content = panel;
            return scroll;
        }

        private static void SetLock(CameraItem item, bool locked, TextBox urlBox, TextBox remarkBox)
        {
            item.locked = locked;
            urlBox.IsReadOnly = locked;
            remarkBox.IsReadOnly = locked;
            ConfigService.SaveCamera();
        }

        private UIElement BuildThemePage()
        {
            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(Label(I18n.T("themeSetting")));

            var dark = new RadioButton { Content = I18n.T("themeDark"), GroupName = "Theme", IsChecked = ThemeManager.Theme == "dark", Margin = new Thickness(0, 8, 0, 4) };
            dark.Checked += (s, e) => ApplyTheme("dark");
            var light = new RadioButton { Content = I18n.T("themeLight"), GroupName = "Theme", IsChecked = ThemeManager.Theme == "light", Margin = new Thickness(0, 0, 0, 4) };
            light.Checked += (s, e) => ApplyTheme("light");

            panel.Children.Add(dark);
            panel.Children.Add(light);
            return panel;
        }

        private static void ApplyTheme(string theme)
        {
            ThemeManager.Apply(theme);
            ConfigService.App.theme = theme;
            ConfigService.SaveApp();
            if (Application.Current.MainWindow != null)
                Application.Current.MainWindow.Background = (System.Windows.Media.Brush)Application.Current.Resources["BgBrush"];
        }

        private UIElement BuildLanguagePage()
        {
            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(Label(I18n.T("languageSetting")));

            var zh = new RadioButton { Content = I18n.T("languageZh"), GroupName = "Language", IsChecked = I18n.Language == "zh", Margin = new Thickness(0, 8, 0, 4) };
            zh.Checked += (s, e) => ApplyLanguage("zh");
            var en = new RadioButton { Content = I18n.T("languageEn"), GroupName = "Language", IsChecked = I18n.Language == "en", Margin = new Thickness(0, 0, 0, 4) };
            en.Checked += (s, e) => ApplyLanguage("en");

            panel.Children.Add(zh);
            panel.Children.Add(en);
            return panel;
        }

        private static void ApplyLanguage(string lang)
        {
            I18n.SetLanguage(lang);
            ConfigService.App.language = lang;
            ConfigService.SaveApp();
        }

        private UIElement BuildJobxPage()
        {
            var panel = new StackPanel { Margin = new Thickness(12) };

            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

            jobxAddBtn = new Button { MinWidth = 90, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
            jobxAddBtn.Click += (s, e) =>
            {
                ConfigService.JobxBackup.cameras.Add(new JobxCameraConfig());
                ConfigService.SaveJobxBackup();
                jobxGrid.Items.Refresh();
            };
            btns.Children.Add(jobxAddBtn);

            jobxBackupBtn = new Button { MinWidth = 90, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
            jobxBackupBtn.Click += (s, e) =>
            {
                var idx = jobxGrid.SelectedIndex;
                if (idx < 0)
                {
                    JobxBackupService.AddLog("WARN", I18n.T("jobxSelectCamera"));
                    return;
                }
                System.Threading.Tasks.Task.Run(() => JobxBackupService.BackupCamera(idx));
            };
            btns.Children.Add(jobxBackupBtn);

            jobxBackupAllBtn = new Button { MinWidth = 90, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
            jobxBackupAllBtn.Click += (s, e) => JobxBackupService.BackupAll();
            btns.Children.Add(jobxBackupAllBtn);

            jobxOpenDirBtn = new Button { MinWidth = 90, Height = 28 };
            jobxOpenDirBtn.Click += (s, e) =>
            {
                string dir = null;
                if (jobxGrid.SelectedItem is JobxCameraConfig cam && !string.IsNullOrWhiteSpace(cam.backup_directory))
                    dir = cam.backup_directory;
                JobxBackupService.OpenBackupDirectory(dir);
            };
            btns.Children.Add(jobxOpenDirBtn);
            panel.Children.Add(btns);

            jobxGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                Height = 220,
                Margin = new Thickness(0, 0, 0, 8),
                ItemsSource = ConfigService.JobxBackup.cameras,
            };

            DataGridTextColumn TextCol(string key, string path, double width)
            {
                var col = new DataGridTextColumn
                {
                    Header = I18n.T(key),
                    Width = new DataGridLength(width),
                    Binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                };
                jobxColumns.Add(new KeyValuePair<DataGridTextColumn, string>(col, key));
                return col;
            }

            jobxGrid.Columns.Add(TextCol("name", "name", 110));
            jobxGrid.Columns.Add(TextCol("ip", "ip", 100));
            jobxGrid.Columns.Add(TextCol("port", "ftp_port", 55));
            jobxGrid.Columns.Add(TextCol("username", "ftp_username", 80));
            jobxGrid.Columns.Add(TextCol("password", "ftp_password", 80));
            jobxGrid.Columns.Add(TextCol("backupDir", "backup_directory", 130));

            var selectCol = new DataGridTemplateColumn { Header = I18n.T("select"), Width = 64 };
            var selFactory = new FrameworkElementFactory(typeof(Button));
            selFactory.SetValue(Button.ContentProperty, I18n.T("select"));
            selFactory.SetValue(Button.HeightProperty, 24.0);
            selFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                if (((FrameworkElement)s).DataContext is JobxCameraConfig cam)
                {
                    try
                    {
                        var dlg = new System.Windows.Forms.FolderBrowserDialog
                        {
                            Description = I18n.T("selectBackupDir"),
                            ShowNewFolderButton = true,
                        };
                        if (!string.IsNullOrWhiteSpace(cam.backup_directory) && System.IO.Directory.Exists(cam.backup_directory))
                            dlg.SelectedPath = cam.backup_directory;
                        var owner = Win32WindowHost.FromVisual((DependencyObject)s);
                        if (dlg.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK
                            && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                        {
                            cam.backup_directory = dlg.SelectedPath;
                            ConfigService.SaveJobxBackup();
                            jobxGrid.Items.Refresh();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show(ex.Message, I18n.T("error"),
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }));
            selectCol.CellTemplate = new DataTemplate { VisualTree = selFactory };
            jobxGrid.Columns.Add(selectCol);

            var checkStyle = new Style(typeof(CheckBox));
            checkStyle.Setters.Add(new EventSetter(CheckBox.CheckedEvent, new RoutedEventHandler((s, e) => ConfigService.SaveJobxBackup())));
            checkStyle.Setters.Add(new EventSetter(CheckBox.UncheckedEvent, new RoutedEventHandler((s, e) => ConfigService.SaveJobxBackup())));

            jobxGrid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = I18n.T("ftps"),
                Width = 55,
                Binding = new Binding("ftps_enabled") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                ElementStyle = checkStyle,
            });
            jobxGrid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = I18n.T("trustCerts"),
                Width = 70,
                Binding = new Binding("trust_all_certs") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                ElementStyle = checkStyle,
            });

            var deleteCol = new DataGridTemplateColumn { Header = "", Width = 60 };
            var deleteFactory = new FrameworkElementFactory(typeof(Button));
            deleteFactory.SetValue(Button.ContentProperty, I18n.T("delete"));
            deleteFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                if (((FrameworkElement)s).DataContext is JobxCameraConfig cam)
                {
                    ConfigService.JobxBackup.cameras.Remove(cam);
                    ConfigService.SaveJobxBackup();
                    jobxGrid.Items.Refresh();
                }
            }));
            deleteCol.CellTemplate = new DataTemplate { VisualTree = deleteFactory };
            jobxGrid.Columns.Add(deleteCol);

            jobxGrid.CellEditEnding += (s, e) => ConfigService.SaveJobxBackup();
            panel.Children.Add(jobxGrid);

            jobxLogLabel = new TextBlock { Text = I18n.T("log"), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) };
            panel.Children.Add(jobxLogLabel);

            jobxLog = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 170,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                Text = "",
            };
            panel.Children.Add(jobxLog);

            jobxLogTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            jobxLogTimer.Tick += (s, e) => RefreshJobxLog();
            jobxLogTimer.Start();

            UpdateJobxTexts();
            return panel;
        }

        private void RefreshJobxLog()
        {
            if (jobxLog == null) return;
            var logs = JobxBackupService.GetLogs();
            var sb = new System.Text.StringBuilder();
            foreach (var entry in logs)
                sb.Append('[').Append(entry.timestamp).Append("] [").Append(entry.level).Append("] ").AppendLine(entry.message);
            var text = sb.ToString();
            jobxLog.Text = text;
            jobxLog.ScrollToEnd();
        }

        private class Win32WindowHost : System.Windows.Forms.IWin32Window
        {
            public IntPtr Handle { get; private set; }
            private Win32WindowHost(IntPtr handle) { Handle = handle; }
            public static Win32WindowHost FromVisual(System.Windows.DependencyObject d)
            {
                try
                {
                    var win = System.Windows.Window.GetWindow(d);
                    if (win != null)
                    {
                        var helper = new System.Windows.Interop.WindowInteropHelper(win);
                        return new Win32WindowHost(helper.Handle);
                    }
                }
                catch { }
                return null;
            }
        }
    }
}
