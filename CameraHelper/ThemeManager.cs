using System;
using System.Drawing;
using System.Windows.Forms;
using AntdUI;

namespace CameraHelper;

/// <summary>
/// 主题管理：AntdUI 黑白模式 + 原生控件配色（AntdUI 控件自动跟随 Config.Mode，
/// 原生 Label/DataGridView/GroupBox 等通过 ThemeManager 调色板手动适配）。
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

	public static TAMode TAMode => IsDark ? TAMode.Dark : TAMode.Light;

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
		}
		else
		{
			Bg = Color.FromArgb(0xF0, 0xF0, 0xF0);
			Bg2 = Color.FromArgb(0xFF, 0xFF, 0xFF);
			Bg3 = Color.FromArgb(0xF7, 0xF7, 0xF7);
			Fg = Color.FromArgb(0x33, 0x33, 0x33);
			FgDim = Color.FromArgb(0x77, 0x77, 0x77);
			Border = Color.FromArgb(0xD0, 0xD0, 0xD0);
		}
		ThemeChanged?.Invoke();
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
