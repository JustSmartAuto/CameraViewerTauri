using System;
using System.Drawing;
using AntdUI;

namespace CameraViewer
{
    /// <summary>
    /// 主题管理：AntdUI 黑白/BeOS 模式 + 原生控件配色
    /// 通过 AntdUI.Style.Set(Colour.XXX, Color) 覆盖控件色卡，
    /// Style.SetPrimary(Color) 自动 HSV 生成品牌色全套衍生色。
    /// </summary>
    public static class ThemeManager
    {
        public static string Theme = "light";
        public static bool IsDark => Theme == "dark";
        public static bool IsBeos => Theme == "beos";

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
        public static Color Highlight { get; private set; }

        // BeOS 配色 token（来自 retro-design-system-main/tokens/07-beos.css）
        public static class Beos
        {
            public static readonly Color Bg           = Color.FromArgb(0x2A, 0x55, 0x56);
            public static readonly Color Chrome       = Color.FromArgb(0xCF, 0xCF, 0xCF);
            public static readonly Color ChromeLight  = Color.FromArgb(0xE8, 0xE8, 0xE8);
            public static readonly Color ChromeLighter = Color.FromArgb(0xFF, 0xFF, 0xFF);
            public static readonly Color ChromeDark   = Color.FromArgb(0x88, 0x88, 0x88);
            public static readonly Color ChromeDarker = Color.FromArgb(0x40, 0x40, 0x40);
            public static readonly Color Teal         = Color.FromArgb(0x00, 0x66, 0x66);
            public static readonly Color Ink          = Color.FromArgb(0x00, 0x00, 0x00);
            public static readonly Color Yellow       = Color.FromArgb(0xFF, 0xCC, 0x00);
            public static readonly Color RedDot       = Color.FromArgb(0xE0, 0x40, 0x40);
        }

        public static TAMode TAMode => IsDark ? TAMode.Dark : TAMode.Light;

        public static void Apply(string theme)
        {
            if (theme != "dark" && theme != "beos") theme = "light";
            Theme = theme;

            // 先清空 AntdUI 自定义色卡（light/dark 用默认，beos 后面重新设）
            Style.Clear();

            if (IsBeos)
            {
                Config.Mode = TMode.Dark; // 让没覆盖的 token 走 dark 默认值

                // === 背景 ===
                Style.Set(Colour.BgBase, Beos.Bg);
                Style.Set(Colour.BgLayout, Beos.Bg);
                Style.Set(Colour.BgContainer, Beos.Chrome);
                Style.Set(Colour.BgElevated, Beos.ChromeLight);

                // === 文字 ===
                Style.Set(Colour.TextBase, Beos.Ink);
                Style.Set(Colour.Text, Beos.Ink);
                Style.Set(Colour.TextSecondary, Beos.ChromeDarker);
                Style.Set(Colour.TextTertiary, Beos.ChromeDark);
                Style.Set(Colour.TextQuaternary, Beos.ChromeDark);

                // === 边框 ===
                Style.Set(Colour.BorderColor, Beos.ChromeDarker);
                Style.Set(Colour.BorderSecondary, Beos.ChromeDark);
                Style.Set(Colour.DefaultBorder, Beos.ChromeDarker);
                Style.Set(Colour.BorderColorDisable, Beos.ChromeDark);

                // === 默认控件（按钮/输入框 底） ===
                Style.Set(Colour.DefaultBg, Beos.Chrome);
                Style.Set(Colour.DefaultColor, Beos.Ink);
                Style.Set(Colour.TagDefaultBg, Beos.ChromeLight);
                Style.Set(Colour.TagDefaultColor, Beos.Ink);

                // === 品牌色（手动，不用 SetPrimary 自动生成 —— 我们要黄色在 chrome 上效果）===
                Style.Set(Colour.Primary, Beos.Yellow);
                Style.Set(Colour.PrimaryColor, Beos.Ink);   // 黄底黑字
                Style.Set(Colour.PrimaryHover, Color.FromArgb(0xFF, 0xDD, 0x33));
                Style.Set(Colour.PrimaryActive, Color.FromArgb(0xCC, 0x99, 0x00));
                Style.Set(Colour.PrimaryBg, Beos.ChromeLight);
                Style.Set(Colour.PrimaryBgHover, Beos.ChromeLighter);
                Style.Set(Colour.PrimaryBorder, Beos.Yellow);
                Style.Set(Colour.PrimaryBorderHover, Color.FromArgb(0xFF, 0xDD, 0x33));

                // === 语义色 ===
                Style.Set(Colour.Success, Color.FromArgb(0x00, 0x88, 0x44));
                Style.Set(Colour.SuccessColor, Color.White);
                Style.Set(Colour.Error, Beos.RedDot);
                Style.Set(Colour.ErrorColor, Color.White);
                Style.Set(Colour.Warning, Beos.Yellow);
                Style.Set(Colour.WarningColor, Beos.Ink);
                Style.Set(Colour.Info, Beos.Teal);
                Style.Set(Colour.InfoColor, Color.White);

                // === 填充/悬浮 ===
                Style.Set(Colour.Fill, Color.FromArgb(0x40, Beos.Ink));         // 64 alpha
                Style.Set(Colour.FillSecondary, Color.FromArgb(0x2E, Beos.Ink)); // 46 alpha
                Style.Set(Colour.FillTertiary, Color.FromArgb(0x1E, Beos.Ink));  // 30 alpha
                Style.Set(Colour.FillQuaternary, Color.FromArgb(0x14, Beos.Ink));
                Style.Set(Colour.HoverBg, Color.FromArgb(0x20, Beos.Yellow));   // 悬浮轻微黄
                Style.Set(Colour.HoverColor, Beos.Ink);
                Style.Set(Colour.Split, Color.FromArgb(0x1F, Beos.Ink));

                // === 其他 ===
                Style.Set(Colour.SwitchHandleBg, Beos.ChromeLighter);
                Style.Set(Colour.TextSpotlight, Beos.Chrome);
                Style.Set(Colour.BgSpotlight, Beos.Ink);
            }
            else
            {
                Config.Mode = IsDark ? TMode.Dark : TMode.Light;
                // light/dark 不覆盖任何颜色，用 AntdUI 默认色卡
            }

            // === 原生控件调色板 ===
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
                Highlight = Color.FromArgb(0x91, 0xCA, 0xFF);
            }
            else if (IsBeos)
            {
                Bg = Beos.Bg;
                Bg2 = Beos.Bg;
                Bg3 = Beos.Chrome;
                Fg = Beos.Ink;
                FgDim = Beos.ChromeDarker;
                Accent = Beos.Yellow;
                Border = Beos.ChromeDarker;
                BtnBorder = Beos.ChromeDarker; // AntdUI Chrome按钮有自己的黄描边，原生按钮用深灰
                Highlight = Beos.Yellow;
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
                Highlight = Color.FromArgb(0x91, 0xCA, 0xFF);
            }

            ThemeChanged?.Invoke();
        }
    }
}
