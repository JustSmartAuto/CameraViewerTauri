using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using AntdUI;

namespace CameraHelper;

/// <summary>
/// 主题管理：AntdUI 黑白模式 + 原生控件配色（AntdUI 控件自动跟随 Config.Mode，
/// 原生 Label/DataGridView/GroupBox 等通过 ThemeManager 调色板手动适配）。
/// 浅色主题为马卡龙浅黄色配色，按钮统一浅蓝色描边（#91CAFF）。
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

	public static Color Border { get; private set; }

	/// <summary>按钮浅蓝色描边色（与参考项目一致：#91CAFF）。</summary>
	public static Color BtnBorder { get; private set; }

	public static TAMode TAMode => IsDark ? TAMode.Dark : TAMode.Light;

	private static Icon appIcon;

	private static MemoryStream iconStream;

	/// <summary>
	/// 全部窗体统一的 Logo（来自内嵌的 assets/icon.ico，按系统小图标尺寸选取最佳帧）。
	/// </summary>
	public static Icon AppIcon
	{
		get
		{
			if (appIcon == null)
			{
				using (Stream source = typeof(ThemeManager).Assembly
					.GetManifestResourceStream("appicon.ico"))
				{
					if (source == null)
					{
						throw new FileNotFoundException("内嵌 Logo 资源缺失：appicon.ico");
					}
					iconStream = new MemoryStream();
					source.CopyTo(iconStream);
				}
				iconStream.Position = 0;
				int size = SystemInformation.SmallIconSize.Width;
				appIcon = new Icon(iconStream, size, size);
			}
			return appIcon;
		}
	}

	/// <summary>为窗体应用统一 Logo（标题栏 / 任务栏 / PageHeader 图标）。</summary>
	public static void ApplyIcon(Form form)
	{
		if (form != null)
		{
			form.Icon = AppIcon;
		}
	}

	public static void Apply(string theme)
	{
		Theme = (theme == "dark") ? "dark" : "light";
		Config.Mode = IsDark ? TMode.Dark : TMode.Light;
		if (IsDark)
		{
			Bg = Color.FromArgb(0x1E, 0x1E, 0x1E);
			Bg2 = Color.FromArgb(0x25, 0x25, 0x26);
			Bg3 = Color.FromArgb(0x2D, 0x2D, 0x30);
			Fg = Color.FromArgb(0xCC, 0xCC, 0xCC);
			FgDim = Color.FromArgb(0x88, 0x88, 0x88);
			Border = Color.FromArgb(0x3F, 0x3F, 0x46);
			BtnBorder = Color.FromArgb(0x91, 0xCA, 0xFF);
		}
		else
		{
			// 马卡龙浅黄色配色（低饱和奶黄系）
			Bg = Color.FromArgb(0xFD, 0xF6, 0xD8);
			Bg2 = Color.FromArgb(0xFF, 0xFB, 0xEA);
			Bg3 = Color.FromArgb(0xF9, 0xED, 0xBE);
			Fg = Color.FromArgb(0x4A, 0x3F, 0x1B);
			FgDim = Color.FromArgb(0x94, 0x86, 0x5C);
			Border = Color.FromArgb(0xE8, 0xDD, 0xA8);
			BtnBorder = Color.FromArgb(0x91, 0xCA, 0xFF);
			// 让 AntdUI 窗口（AntdUI.Window）背景与文字也使用马卡龙配色
			Config.Theme().Light(Bg, Fg);
		}
		ThemeChanged?.Invoke();
	}

	/// <summary>
	/// 单个 AntdUI 按钮浅蓝色描边（AntdUI 2.4.10 属性名为 DefaultBorderColor，配合 BorderWidth=1）。
	/// </summary>
	public static void StyleButton(AntdUI.Button btn)
	{
		if (btn == null)
		{
			return;
		}
		btn.DefaultBorderColor = BtnBorder;
		btn.BorderWidth = 1f;
	}

	/// <summary>
	/// 递归遍历控件树，为全部 AntdUI 按钮添加浅蓝描边；skip 中的按钮跳过（如红/绿色在线状态按钮）。
	/// </summary>
	public static void StyleButtons(Control root, params AntdUI.Button[] skip)
	{
		if (root == null)
		{
			return;
		}
		AntdUI.Button btn = root as AntdUI.Button;
		if (btn != null && (skip == null || Array.IndexOf(skip, btn) < 0))
		{
			StyleButton(btn);
		}
		foreach (Control child in root.Controls)
		{
			StyleButtons(child, skip);
		}
	}

	/// <summary>
	/// 原生 DataGridView 主题化（AntdUI 无表格控件，保留原生并手动配色）。
	/// </summary>
	public static void StyleGrid(DataGridView dgv)
	{
		dgv.BackgroundColor = Bg2;
		dgv.GridColor = Border;
		dgv.BorderStyle = BorderStyle.FixedSingle;
		dgv.EnableHeadersVisualStyles = false;
		dgv.ColumnHeadersDefaultCellStyle.BackColor = Bg3;
		dgv.ColumnHeadersDefaultCellStyle.ForeColor = Fg;
		dgv.ColumnHeadersDefaultCellStyle.SelectionBackColor = Bg3;
		dgv.ColumnHeadersDefaultCellStyle.SelectionForeColor = Fg;
		dgv.RowHeadersDefaultCellStyle.BackColor = Bg3;
		dgv.RowHeadersDefaultCellStyle.ForeColor = Fg;
		dgv.DefaultCellStyle.BackColor = Bg2;
		dgv.DefaultCellStyle.ForeColor = Fg;
		dgv.DefaultCellStyle.SelectionBackColor = IsDark ? Color.FromArgb(0x0E, 0x2F, 0x4A) : Color.FromArgb(0xCC, 0xE0, 0xF5);
		dgv.DefaultCellStyle.SelectionForeColor = Fg;
	}
}
