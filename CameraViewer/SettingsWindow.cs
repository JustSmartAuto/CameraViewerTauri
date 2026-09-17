using System;
using System.Windows;
using System.Windows.Controls;
using CameraViewer;
using Application = System.Windows.Application;

namespace CameraViewerDotnet
{
    public class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            Title = I18n.T("systemSettings");
            Width = 640;
            Height = 520;
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

            Content = tabs;

            I18n.LanguageChanged += OnLanguageChanged;
            Closed += (s, e) => I18n.LanguageChanged -= OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            Title = I18n.T("systemSettings");
            if (Content is TabControl tabs)
            {
                var names = new[] { I18n.T("cameraDisplay"), I18n.T("cameraSettings"), I18n.T("displaySettings"), I18n.T("appSettings") };
                for (int i = 0; i < tabs.Items.Count && i < names.Length; i++)
                    ((TabItem)tabs.Items[i]).Header = names[i];
            }
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
            foreach (var n in new[] { 1, 2, 4, 6, 9, 12 })
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
    }
}
