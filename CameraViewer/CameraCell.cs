using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CameraViewer;

namespace CameraViewerDotnet
{
    public class CameraCell : UserControl
    {
        private readonly int id;
        private readonly TextBox urlBox;
        private readonly TextBox remarkBox;
        private readonly Button lockBtn;
        private readonly Microsoft.Web.WebView2.Wpf.WebView2 webView;
        private bool webViewReady;
        private bool isMaximized;

        public event Action<CameraCell> ToggleMaximize;

        public CameraCell(int id)
        {
            this.id = id;
            var item = ConfigService.EnsureItem(id);

            Background = (System.Windows.Media.Brush)Application.Current.Resources["BgBrush2"];

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var top = new Grid { Margin = new Thickness(4, 4, 4, 2) };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            urlBox = new TextBox
            {
                Text = item.ip,
                IsReadOnly = item.locked,
                ToolTip = I18n.T("url"),
                VerticalContentAlignment = VerticalAlignment.Center,
                Height = 26,
            };
            urlBox.LostFocus += (s, e) => CommitUrl();
            urlBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) CommitUrl(); };
            Grid.SetColumn(urlBox, 0);
            top.Children.Add(urlBox);

            lockBtn = new Button { Width = 78, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
            lockBtn.Click += (s, e) => ToggleLock();
            Grid.SetColumn(lockBtn, 1);
            top.Children.Add(lockBtn);

            refreshBtn = new Button { Width = 78, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
            refreshBtn.Click += async (s, e) => await ReloadAsync();
            Grid.SetColumn(refreshBtn, 2);
            top.Children.Add(refreshBtn);

            maxBtn = new Button { Width = 78, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
            maxBtn.Click += (s, e) =>
            {
                isMaximized = !isMaximized;
                UpdateMaxBtn();
                ToggleMaximize?.Invoke(this);
            };
            Grid.SetColumn(maxBtn, 3);
            top.Children.Add(maxBtn);
            UpdateLockBtn();
            UpdateMaxBtn();
            refreshBtn.Content = AntIcon.Content(AntIcon.Reload, I18n.T("refresh"));

            grid.Children.Add(top);

            remarkBox = new TextBox
            {
                Text = item.remark,
                IsReadOnly = item.locked,
                ToolTip = I18n.T("remark"),
                VerticalContentAlignment = VerticalAlignment.Center,
                Height = 26,
                Margin = new Thickness(4, 2, 4, 2),
            };
            remarkBox.LostFocus += (s, e) =>
            {
                var it = ConfigService.EnsureItem(id);
                it.remark = remarkBox.Text;
                ConfigService.SaveCamera();
            };
            Grid.SetRow(remarkBox, 1);
            grid.Children.Add(remarkBox);

            webView = new Microsoft.Web.WebView2.Wpf.WebView2();
            webView.Margin = new Thickness(4, 2, 4, 4);
            webView.CoreWebView2InitializationCompleted += (s, e) =>
            {
                webViewReady = true;
                Navigate();
            };
            Grid.SetRow(webView, 2);
            grid.Children.Add(webView);

            Content = grid;

            I18n.LanguageChanged += OnLanguageChanged;
            Unloaded += (s, e) => I18n.LanguageChanged -= OnLanguageChanged;
        }

        private readonly Button maxBtn;
        private readonly Button refreshBtn;

        private void UpdateLockBtn()
        {
            var locked = ConfigService.EnsureItem(id).locked;
            lockBtn.Content = AntIcon.Content(locked ? AntIcon.Lock : AntIcon.Unlock, locked ? I18n.T("unlock") : I18n.T("lock"));
        }

        private void UpdateMaxBtn() =>
            maxBtn.Content = AntIcon.Content(isMaximized ? AntIcon.Shrink : AntIcon.Expand, isMaximized ? I18n.T("restore") : I18n.T("maximize"));

        private void OnLanguageChanged()
        {
            UpdateLockBtn();
            UpdateMaxBtn();
            refreshBtn.Content = AntIcon.Content(AntIcon.Reload, I18n.T("refresh"));
            urlBox.ToolTip = I18n.T("url");
            remarkBox.ToolTip = I18n.T("remark");
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
            urlBox.IsReadOnly = item.locked;
            remarkBox.IsReadOnly = item.locked;
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
