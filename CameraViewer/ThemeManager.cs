using System;
using System.Drawing;
using AntdUI;

namespace CameraViewer
{
    /// <summary>
    /// 主题管理：AntdUI 黑白模式 + 原生控件配色
    /// </summary>
    public static class ThemeManager
    {
        public static string Theme = "light";
        public static bool IsDark => Theme == "dark";

        public static event Action ThemeChanged;

        // 原生（非 AntdUI）控件使用的调色板
        public static Color Bg { get; private set; }
        public static Color Bg2 { get; private set; }
        public static Color Bg3 { get; private set; }
        public static Color Fg { get; private set; }
        public static Color FgDim { get; private set; }
        public static Color Accent { get; private set; }
        public static Color Border { get; private set; }
        public static Color BtnBorder { get; private set; }

        public static TAMode TAMode => IsDark ? TAMode.Dark : TAMode.Light;

        public static void Apply(string theme)
        {
            if (theme != "dark") theme = "light";
            Theme = theme;

            Config.Mode = IsDark ? TMode.Dark : TMode.Light;

            if (IsDark)
            {
                Bg = Color.FromArgb(0x1E, 0x1E, 0x1E);
                Bg2 = Color.FromArgb(0x25, 0x25, 0x26);
                Bg3 = Color.FromArgb(0x2D, 0x2D, 0x30);
                Fg = Color.FromArgb(0xCC, 0xCC, 0xCC);
                FgDim = Color.FromArgb(0x88, 0x88, 0x88);
                Accent = Color.FromArgb(0x0E, 0x63, 0x9C);
                Border = Color.FromArgb(0x3F, 0x3F, 0x46);
                BtnBorder = Color.FromArgb(0x91, 0xCA, 0xFF);
            }
            else
            {
                Bg = Color.FromArgb(0xF0, 0xF0, 0xF0);
                Bg2 = Color.FromArgb(0xFF, 0xFF, 0xFF);
                Bg3 = Color.FromArgb(0xF7, 0xF7, 0xF7);
                Fg = Color.FromArgb(0x33, 0x33, 0x33);
                FgDim = Color.FromArgb(0x77, 0x77, 0x77);
                Accent = Color.FromArgb(0x00, 0x78, 0xD4);
                Border = Color.FromArgb(0xD0, 0xD0, 0xD0);
                BtnBorder = Color.FromArgb(0x91, 0xCA, 0xFF);
            }

            ThemeChanged?.Invoke();
        }
    }
}
