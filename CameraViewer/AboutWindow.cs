using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace CameraViewer
{
    public class AboutWindow : Window
    {
        public AboutWindow()
        {
            Title = I18n.T("about");
            Width = 440;
            Height = 380;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = (Brush)Application.Current.Resources["BgBrush"];
            Foreground = (Brush)Application.Current.Resources["FgBrush"];
            ShowInTaskbar = false;

            var root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header: icon + app name
            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 16),
            };
            var icon = AntIcon.Create(AntIcon.InfoCircle, 28);
            icon.Margin = new Thickness(0, 0, 10, 0);
            header.Children.Add(icon);
            header.Children.Add(new TextBlock
            {
                Text = I18n.T("appTitle"),
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["FgBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            root.Children.Add(header);

            // Body: version, description, tech, repo link
            var body = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            var ver = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "1.0.0";

            body.Children.Add(MakeLine(I18n.T("version") + " " + ver));
            body.Children.Add(MakeGap(10));
            body.Children.Add(MakeLine(I18n.T("aboutDesc"), true));
            body.Children.Add(MakeGap(10));
            body.Children.Add(MakeLine(I18n.T("aboutTech")));
            body.Children.Add(MakeGap(10));

            var repoRow = new StackPanel { Orientation = Orientation.Horizontal };
            repoRow.Children.Add(new TextBlock
            {
                Text = I18n.T("aboutRepo") + "：",
                Foreground = (Brush)Application.Current.Resources["FgBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            var link = new Hyperlink(new Run("github.com/JustSmartAuto/CameraViewerTauri"))
            {
                NavigateUri = new System.Uri("https://github.com/JustSmartAuto/CameraViewerTauri"),
                Foreground = (Brush)Application.Current.Resources["AccentBrush"],
            };
            link.RequestNavigate += (s, e) => { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); e.Handled = true; };
            var linkBlock = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            linkBlock.Inlines.Add(link);
            repoRow.Children.Add(linkBlock);
            body.Children.Add(repoRow);
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            // Close button
            var closeBtn = new Button
            {
                Content = I18n.T("aboutClose"),
                Width = 90,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0),
            };
            closeBtn.Click += (s, e) => Close();
            Grid.SetRow(closeBtn, 2);
            root.Children.Add(closeBtn);

            Content = root;
        }

        private static TextBlock MakeLine(string text, bool wrap = false)
        {
            var tb = new TextBlock
            {
                Text = text,
                Foreground = (Brush)Application.Current.Resources["FgBrush"],
                TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            };
            if (wrap) tb.TextAlignment = TextAlignment.Left;
            return tb;
        }

        private static FrameworkElement MakeGap(double h) => new Border { Height = h };
    }
}
