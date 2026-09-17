using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using AntdUI;
using CameraViewer;

namespace CameraViewer
{
    public class AboutWindow : AntdUI.Window
    {
        public AboutWindow()
        {
            Text = I18n.T("about");
            Size = new Size(440, 380);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Resizable = false;
            MaximizeBox = false;
            MinimizeBox = false;
            Mode = ThemeManager.TAMode;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(24) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // 头部：图标 + 应用名
            var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
            var icon = new AntdUI.Label
            {
                Text = "",
                PrefixSvg = AntIcon.Svg(AntIcon.InfoCircle),
                Size = new Size(32, 32),
                Margin = new Padding(0, 0, 10, 0),
            };
            header.Controls.Add(icon);
            var title = new AntdUI.Label
            {
                Text = I18n.T("appTitle"),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
                Margin = new Padding(0, 6, 0, 0),
            };
            header.Controls.Add(title);
            root.Controls.Add(header, 0, 0);

            // 内容：版本、描述、技术栈、仓库链接
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true };
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var ver = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "1.0.0";

            var verLabel = MakeLine(I18n.T("version") + " " + ver);
            var descLabel = MakeLine(I18n.T("aboutDesc"), true);
            var techLabel = MakeLine(I18n.T("aboutTech"));
            body.Controls.Add(verLabel, 0, 0);
            body.Controls.Add(descLabel, 0, 1);
            body.Controls.Add(techLabel, 0, 2);

            var repoRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
            var repoLabel = MakeLine(I18n.T("aboutRepo") + "：");
            repoRow.Controls.Add(repoLabel);
            var link = new LinkLabel
            {
                Text = "github.com/JustSmartAuto/CameraViewerTauri",
                AutoSize = true,
                LinkColor = ThemeManager.Accent,
                Margin = new Padding(4, 9, 0, 0),
                TabStop = true,
            };
            link.LinkClicked += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo("https://github.com/JustSmartAuto/CameraViewerTauri") { UseShellExecute = true }); }
                catch { }
            };
            repoRow.Controls.Add(link);
            body.Controls.Add(repoRow, 0, 3);
            root.Controls.Add(body, 0, 1);

            // 关闭按钮
            var closeRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            var closeBtn = new AntdUI.Button
            {
                Text = I18n.T("aboutClose"),
                Width = 90,
                Height = 30,
                Type = TTypeMini.Primary,
            };
            closeBtn.Click += (s, e) => Close();
            closeRow.Controls.Add(closeBtn);
            root.Controls.Add(closeRow, 0, 2);

            Controls.Add(root);

            I18n.LanguageChanged += UpdateTexts;
            FormClosed += (s, e) => I18n.LanguageChanged -= UpdateTexts;
            UpdateTexts();
        }

        private void UpdateTexts()
        {
            Text = I18n.T("about");
        }

        private static AntdUI.Label MakeLine(string text, bool wrap = false) => new AntdUI.Label
        {
            Text = text,
            AutoSize = !wrap,
            Dock = wrap ? DockStyle.Top : DockStyle.None,
            MaximumSize = new Size(360, 0),
            Margin = new Padding(0, 5, 0, 5),
        };
    }
}
