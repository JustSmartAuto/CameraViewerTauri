using System;
using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace WebHMI;

public class FormMain : Form
{
	private Timer timerUpdate = new Timer();

	private bool isSetting;

	private IContainer components;

	private ToolStrip toolStrip1;

	private ToolStripButton btnSysSetting;

	private StatusStrip statusStrip1;

	private ToolStripStatusLabel lblTime;

	private ToolStripStatusLabel lblVersion;

	private TableLayoutPanel pnlMain;

	public FormMain()
	{
		InitializeComponent();
	}

	private void FormMain_Load(object sender, EventArgs e)
	{
		ProjectMgr.Inst.LoadConfig();
		ProjectMgr.Inst.CreateCameraWebView();
		LayoutDisplay();
		ProjectMgr.Inst.CreateFileTransform();
		ProjectMgr.Inst.Cleaner.StartClean();
		toolStrip1.Focus();
		timerUpdate.Interval = 1000;
		timerUpdate.Tick += delegate
		{
			lblTime.Text = $"系统时间：{DateTime.Now:yyyy年MM月dd日  HH:mm:ss}";
			if (!isSetting && base.WindowState != FormWindowState.Minimized)
			{
				base.TopMost = true;
				base.TopMost = false;
			}
		};
		timerUpdate.Start();
		lblVersion.Text = $"版本号：{Assembly.GetExecutingAssembly().GetName().Version}";
	}

	public void LayoutDisplay()
	{
		int num = 0;
		int num2 = 0;
		int count = ProjectMgr.Inst.CameraWebViews.Count;
		if (count == 0)
		{
			return;
		}
		if (count == 1)
		{
			num = (num2 = 1);
		}
		else if (count == 2)
		{
			num = 1;
			num2 = 2;
		}
		else if (count <= 4)
		{
			num = (num2 = 2);
		}
		else if (count <= 6)
		{
			num = 2;
			num2 = 3;
		}
		else if (count <= 9)
		{
			num = 3;
			num2 = 3;
		}
		else if (count <= 12)
		{
			num = 4;
			num2 = 3;
		}
		pnlMain.RowCount = num;
		float num3 = 100f / (float)num;
		pnlMain.RowStyles.Clear();
		for (int i = 0; i < num; i++)
		{
			pnlMain.RowStyles.Add(new RowStyle(SizeType.Percent, num3));
		}
		pnlMain.ColumnCount = num2;
		float num4 = 100f / (float)num2;
		pnlMain.ColumnStyles.Clear();
		for (int j = 0; j < num2; j++)
		{
			pnlMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, num4));
		}
		try
		{
			pnlMain.Controls.Clear();
			int num5 = 0;
			for (int k = 0; k < num; k++)
			{
				for (int l = 0; l < num2; l++)
				{
					if (num5 >= count)
					{
						return;
					}
					pnlMain.Controls.Add(ProjectMgr.Inst.CameraWebViews[num5], l, k);
					num5++;
				}
			}
		}
		catch
		{
		}
		finally
		{
			ProjectMgr.Inst.CameraWebViews.ForEach(delegate(WebView t)
			{
				t.MaxiEvent -= T_MaxiEvent;
				t.MaxiEvent += T_MaxiEvent;
			});
		}
	}

	private void T_MaxiEvent(object sender, bool e)
	{
		if (sender is WebView value)
		{
			if (e)
			{
				pnlMain.Controls.Clear();
				TableLayoutPanel tableLayoutPanel = pnlMain;
				int rowCount = (pnlMain.ColumnCount = 1);
				tableLayoutPanel.RowCount = rowCount;
				pnlMain.RowStyles.Clear();
				pnlMain.ColumnStyles.Clear();
				pnlMain.Controls.Add(value);
			}
			else
			{
				LayoutDisplay();
			}
		}
	}

	private void btnSysSetting_Click(object sender, EventArgs e)
	{
		try
		{
			isSetting = true;
			new FormSetting().ShowDialog();
			LayoutDisplay();
		}
		finally
		{
			isSetting = false;
		}
	}

	private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
	{
		ProjectMgr.Inst.Close();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && components != null)
		{
			components.Dispose();
		}
		base.Dispose(disposing);
	}

	private void InitializeComponent()
	{
		System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(WebHMI.FormMain));
		this.toolStrip1 = new System.Windows.Forms.ToolStrip();
		this.btnSysSetting = new System.Windows.Forms.ToolStripButton();
		this.statusStrip1 = new System.Windows.Forms.StatusStrip();
		this.lblTime = new System.Windows.Forms.ToolStripStatusLabel();
		this.lblVersion = new System.Windows.Forms.ToolStripStatusLabel();
		this.pnlMain = new System.Windows.Forms.TableLayoutPanel();
		this.toolStrip1.SuspendLayout();
		this.statusStrip1.SuspendLayout();
		base.SuspendLayout();
		this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[1] { this.btnSysSetting });
		this.toolStrip1.Location = new System.Drawing.Point(0, 0);
		this.toolStrip1.Name = "toolStrip1";
		this.toolStrip1.Size = new System.Drawing.Size(919, 25);
		this.toolStrip1.TabIndex = 0;
		this.toolStrip1.Text = "toolStrip1";
		this.btnSysSetting.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
		this.btnSysSetting.Image = (System.Drawing.Image)resources.GetObject("btnSysSetting.Image");
		this.btnSysSetting.ImageTransparentColor = System.Drawing.Color.Magenta;
		this.btnSysSetting.Name = "btnSysSetting";
		this.btnSysSetting.Size = new System.Drawing.Size(52, 22);
		this.btnSysSetting.Text = "Setting";
		this.btnSysSetting.Click += new System.EventHandler(btnSysSetting_Click);
		this.statusStrip1.Font = new System.Drawing.Font("Microsoft YaHei UI", 10.5f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 134);
		this.statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[2] { this.lblTime, this.lblVersion });
		this.statusStrip1.Location = new System.Drawing.Point(0, 645);
		this.statusStrip1.Name = "statusStrip1";
		this.statusStrip1.Size = new System.Drawing.Size(919, 24);
		this.statusStrip1.TabIndex = 1;
		this.statusStrip1.Text = "statusStrip1";
		this.lblTime.Margin = new System.Windows.Forms.Padding(20, 2, 0, 2);
		this.lblTime.Name = "lblTime";
		this.lblTime.Size = new System.Drawing.Size(37, 20);
		this.lblTime.Text = "时间";
		this.lblVersion.Margin = new System.Windows.Forms.Padding(30, 2, 10, 2);
		this.lblVersion.Name = "lblVersion";
		this.lblVersion.Size = new System.Drawing.Size(51, 20);
		this.lblVersion.Text = "版本号";
		this.pnlMain.ColumnCount = 1;
		this.pnlMain.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100f));
		this.pnlMain.Dock = System.Windows.Forms.DockStyle.Fill;
		this.pnlMain.Location = new System.Drawing.Point(0, 25);
		this.pnlMain.Name = "pnlMain";
		this.pnlMain.RowCount = 1;
		this.pnlMain.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100f));
		this.pnlMain.Size = new System.Drawing.Size(919, 620);
		this.pnlMain.TabIndex = 2;
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
		base.ClientSize = new System.Drawing.Size(919, 669);
		base.Controls.Add(this.pnlMain);
		base.Controls.Add(this.statusStrip1);
		base.Controls.Add(this.toolStrip1);
		base.Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
		base.Name = "FormMain";
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
		this.Text = "CameraViewerLens";
		base.WindowState = System.Windows.Forms.FormWindowState.Maximized;
		base.FormClosing += new System.Windows.Forms.FormClosingEventHandler(FormMain_FormClosing);
		base.Load += new System.EventHandler(FormMain_Load);
		this.toolStrip1.ResumeLayout(false);
		this.toolStrip1.PerformLayout();
		this.statusStrip1.ResumeLayout(false);
		this.statusStrip1.PerformLayout();
		base.ResumeLayout(false);
		base.PerformLayout();
	}
}
