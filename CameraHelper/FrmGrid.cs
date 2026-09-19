using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Cognex.InSight.Web;
using Cognex.InSight.Web.Controls;

namespace CameraHelper;

public class FrmGrid : Form
{
	private CvsInSight camera;

	private CvsSpreadsheet ctrlSheet;

	private bool isOffline;

	private IContainer components;

	private Panel panel1;

	private AntdUI.Button btnOK;

	private AntdUI.Button btnClose;

	private Panel pnlMain;

	private AntdUI.Button btnOffline;

	public FrmGrid(CvsInSight cam)
	{
		InitializeComponent();
		ThemeManager.ThemeChanged += ApplyTheme;
		ApplyTheme();
		bool isCh = ProjectMgr.Inst.SysConfig.Language == 0;
		Text = (isCh ? "电子表格" : "Spreadsheet");
		btnOK.Text = (isCh ? "保存作业" : "Save Job");
		btnClose.Text = (isCh ? "关闭" : "Close");
		btnOffline.Text = (isCh ? "离线编辑" : "Offline And Edit");
		camera = cam;
		ctrlSheet = new CvsSpreadsheet();
		ctrlSheet.Dock = DockStyle.Fill;
		pnlMain.Controls.Add(ctrlSheet);
		ctrlSheet.SetInSight(camera);
		cam.ResultsChanged += Cam_ResultsChanged;
	}

	private void ApplyTheme()
	{
		ForeColor = ThemeManager.Fg;
		BackColor = ThemeManager.Bg3;
		panel1.BackColor = ThemeManager.Bg2;
	}

	private void FrmGrid_Load(object sender, EventArgs e)
	{
		ctrlSheet.InitSpreadsheet();
	}

	private void Cam_ResultsChanged(object sender, EventArgs e)
	{
		ctrlSheet.UpdateResults(camera.Results);
	}

	private async void btnOK_Click(object sender, EventArgs e)
	{
		bool isCh = ProjectMgr.Inst.SysConfig.Language == 0;
		try
		{
			CvsInSight cvsInSight = camera;
			await cvsInSight.SaveJob(await camera.GetJobName());
			MessageBox.Show(isCh ? "保存成功" : "Save successfully");
		}
		catch
		{
			MessageBox.Show(isCh ? "保存失败" : "Save failed");
		}
	}

	private void btnClose_Click(object sender, EventArgs e)
	{
		Close();
	}

	private async void btnOffline_Click(object sender, EventArgs e)
	{
		await camera.SetSoftOnlineAsync(value: false);
		isOffline = true;
	}

	private async void FrmGrid_FormClosing(object sender, FormClosingEventArgs e)
	{
		if (isOffline)
		{
			await camera.SetSoftOnlineAsync(value: true);
		}
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
		this.pnlMain = new System.Windows.Forms.Panel();
		this.btnOffline = new AntdUI.Button();
		this.panel1.SuspendLayout();
		base.SuspendLayout();
		this.panel1.Controls.Add(this.btnOffline);
		this.panel1.Controls.Add(this.btnOK);
		this.panel1.Controls.Add(this.btnClose);
		this.panel1.Dock = System.Windows.Forms.DockStyle.Bottom;
		this.panel1.Location = new System.Drawing.Point(0, 722);
		this.panel1.Name = "panel1";
		this.panel1.Size = new System.Drawing.Size(1008, 39);
		this.panel1.TabIndex = 2;
		this.btnOK.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.btnOK.Location = new System.Drawing.Point(770, 5);
		this.btnOK.Name = "btnOK";
		this.btnOK.Size = new System.Drawing.Size(100, 30);
		this.btnOK.TabIndex = 1;
		this.btnOK.Text = "保存作业";
		this.btnOK.Click += new System.EventHandler(btnOK_Click);
		this.btnClose.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.btnClose.Location = new System.Drawing.Point(897, 5);
		this.btnClose.Name = "btnClose";
		this.btnClose.Size = new System.Drawing.Size(100, 30);
		this.btnClose.TabIndex = 0;
		this.btnClose.Text = "关闭";
		this.btnClose.Click += new System.EventHandler(btnClose_Click);
		this.pnlMain.Dock = System.Windows.Forms.DockStyle.Fill;
		this.pnlMain.Location = new System.Drawing.Point(0, 0);
		this.pnlMain.Name = "pnlMain";
		this.pnlMain.Size = new System.Drawing.Size(1008, 722);
		this.pnlMain.TabIndex = 3;
		this.btnOffline.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.btnOffline.Location = new System.Drawing.Point(12, 5);
		this.btnOffline.Name = "btnOffline";
		this.btnOffline.Size = new System.Drawing.Size(132, 30);
		this.btnOffline.TabIndex = 2;
		this.btnOffline.Text = "离线编辑";
		this.btnOffline.Click += new System.EventHandler(btnOffline_Click);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
		base.ClientSize = new System.Drawing.Size(1008, 761);
		base.Controls.Add(this.pnlMain);
		base.Controls.Add(this.panel1);
		this.Font = new System.Drawing.Font("微软雅黑", 9f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 134);
		base.MinimizeBox = false;
		base.Name = "FrmGrid";
		base.ShowIcon = false;
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
		this.Text = "电子表格";
		base.FormClosing += new System.Windows.Forms.FormClosingEventHandler(FrmGrid_FormClosing);
		base.Load += new System.EventHandler(FrmGrid_Load);
		this.panel1.ResumeLayout(false);
		base.ResumeLayout(false);
	}
}
