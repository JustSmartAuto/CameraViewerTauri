using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace CameraHelper;

public class FrmCamCount : Form
{
	private IContainer components;

	private Panel panel1;

	private AntdUI.Button btnOK;

	private AntdUI.Button btnClose;

	private Label lblCount;

	private AntdUI.InputNumber numCount;

	public bool IsOK { get; private set; }

	public int Count { get; private set; } = 1;

	public FrmCamCount()
	{
		InitializeComponent();
		ThemeManager.ThemeChanged += ApplyTheme;
		ApplyTheme();
		bool isChinese = ProjectMgr.Inst.SysConfig.Language == 0;
		Text = (isChinese ? "相机数量设置" : "Camera count setting");
		lblCount.Text = (isChinese ? "数量：" : "Count: ");
		btnOK.Text = (isChinese ? "是" : "Yes");
		btnClose.Text = (isChinese ? "否" : "No");
	}

	private void ApplyTheme()
	{
		ForeColor = ThemeManager.Fg;
		BackColor = ThemeManager.Bg3;
		panel1.BackColor = ThemeManager.Bg3;
	}

	private void btnOK_Click(object sender, EventArgs e)
	{
		Count = (int)numCount.Value;
		IsOK = true;
		Close();
	}

	private void btnClose_Click(object sender, EventArgs e)
	{
		IsOK = false;
		Close();
	}

	protected override void Dispose(bool disposing)
	{
		ThemeManager.ThemeChanged -= ApplyTheme;
		if (disposing && components != null)
		{
			components.Dispose();
		}
		base.Dispose(disposing);
	}

	private void InitializeComponent()
	{
		this.panel1 = new System.Windows.Forms.Panel();
		this.btnOK = new AntdUI.Button();
		this.btnClose = new AntdUI.Button();
		this.lblCount = new System.Windows.Forms.Label();
		this.numCount = new AntdUI.InputNumber();
		this.panel1.SuspendLayout();
		base.SuspendLayout();
		this.panel1.Controls.Add(this.btnOK);
		this.panel1.Controls.Add(this.btnClose);
		this.panel1.Dock = System.Windows.Forms.DockStyle.Bottom;
		this.panel1.Location = new System.Drawing.Point(0, 134);
		this.panel1.Name = "panel1";
		this.panel1.Size = new System.Drawing.Size(444, 39);
		this.panel1.TabIndex = 3;
		this.btnOK.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.btnOK.Location = new System.Drawing.Point(274, 3);
		this.btnOK.Name = "btnOK";
		this.btnOK.Size = new System.Drawing.Size(75, 33);
		this.btnOK.TabIndex = 1;
		this.btnOK.Text = "是";
		this.btnOK.Click += new System.EventHandler(btnOK_Click);
		this.btnClose.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.btnClose.Location = new System.Drawing.Point(362, 3);
		this.btnClose.Name = "btnClose";
		this.btnClose.Size = new System.Drawing.Size(75, 33);
		this.btnClose.TabIndex = 0;
		this.btnClose.Text = "否";
		this.btnClose.Click += new System.EventHandler(btnClose_Click);
		this.lblCount.AutoSize = true;
		this.lblCount.Location = new System.Drawing.Point(31, 48);
		this.lblCount.Name = "lblCount";
		this.lblCount.Size = new System.Drawing.Size(51, 20);
		this.lblCount.TabIndex = 4;
		this.lblCount.Text = "数量：";
		this.numCount.Location = new System.Drawing.Point(123, 42);
		this.numCount.Maximum = 16m;
		this.numCount.Minimum = 1m;
		this.numCount.Name = "numCount";
		this.numCount.Size = new System.Drawing.Size(120, 34);
		this.numCount.TabIndex = 5;
		this.numCount.Value = 1m;
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
		base.ClientSize = new System.Drawing.Size(444, 173);
		base.Controls.Add(this.numCount);
		base.Controls.Add(this.lblCount);
		base.Controls.Add(this.panel1);
		this.Font = new System.Drawing.Font("微软雅黑", 10.5f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 134);
		base.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
		base.Name = "FrmCamCount";
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
		this.Text = "设置数量";
		this.panel1.ResumeLayout(false);
		base.ResumeLayout(false);
		base.PerformLayout();
	}
}
