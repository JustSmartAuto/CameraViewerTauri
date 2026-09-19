using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CameraHelper;

public class FrmMain : AntdUI.Window
{
	private System.Windows.Forms.Timer startTimer = new System.Windows.Forms.Timer();

	private System.Windows.Forms.Timer timerUpdate = new System.Windows.Forms.Timer();

	private Mutex mutex;

	private bool IsGoingOnline;

	private IContainer components;

	private TableLayoutPanel pnlMain;

	private AntdUI.PageHeader header;

	private Panel toolbar;

	private AntdUI.Button btnOnline;

	private AntdUI.Button btnSystemSetting;

	private AntdUI.Button btnOperation;

	private AntdUI.Button btnTheme;

	private AntdUI.Select cmbxLanguage;

	private Panel statusPanel;

	private Label lblTime;

	private Label lblVersion;

	public int Rows => pnlMain.RowCount;

	public int Cols => pnlMain.ColumnCount;

	public FrmMain()
	{
		InitializeComponent();
		ThemeManager.ThemeChanged += ApplyTheme;
	}

	private void FrmMain_Load(object sender, EventArgs e)
	{
		ProjectMgr proj = ProjectMgr.Inst;
		proj.LoadSysConfig();
		ThemeManager.Apply(proj.SysConfig.Theme);
		bool isCh = ProjectMgr.Inst.SysConfig.Language == 0;
		mutex = new Mutex(initiallyOwned: true, proj.SysConfig.ProcessingName, out var canCreateNew);
		if (!canCreateNew)
		{
			MessageBox.Show(isCh ? "软件已运行" : "Software has been running");
			Close();
			return;
		}
		cmbxLanguage.SelectedIndex = ProjectMgr.Inst.SysConfig.Language;
		proj.ActionQueueWorker.Start(ThreadPriority.Normal);
		proj.LoadCameraConfigs();
		proj.CreateCameras();
		proj.CreateViewConfigs();
		proj.LoadViewConfigs();
		proj.CreateViews();
		proj.LoadCleanConfigs();
		proj.ImageCleaner.StartClean();
		LayoutDisplay();
		if (proj.SysConfig.IsSelectScreen)
		{
			base.Bounds = ProjectMgr.Inst.GetScreenBounds(proj.SysConfig.ScreenIndex);
		}
		if (proj.SysConfig.IsCloseFirewall)
		{
			ProjectMgr.OperateDefender(isOpen: false);
			ProjectMgr.OperateFirewall(isOpen: false);
		}
		UpdateCtrlLag();
		startTimer.Interval = 2000;
		startTimer.Tick += delegate
		{
			startTimer.Stop();
			SetOnline(isOnline: true);
			LayoutDisplay();
			foreach (CameraView view in ProjectMgr.Inst.Views)
			{
				view.Invalidate();
			}
		};
		startTimer.Start();
		timerUpdate.Interval = 1000;
		timerUpdate.Tick += delegate
		{
			try
			{
				timerUpdate.Stop();
				lblTime.Text = $"Now：{DateTime.Now:yyyy/MM/dd  HH:mm:ss}";
				if (!ProjectMgr.Inst.IsSetting && ProjectMgr.Inst.SysConfig.AlwaysTop && base.WindowState != FormWindowState.Minimized)
				{
					base.TopMost = true;
					base.TopMost = false;
				}
				Invoke((Action)async delegate
				{
					await ProjectMgr.Inst.CheckCameraConnection();
				});
				ProjectMgr.Inst.Views.ForEach(delegate(CameraView t)
				{
					t.UpdateConfigCtrls();
				});
			}
			catch
			{
			}
			finally
			{
				timerUpdate.Start();
			}
		};
		timerUpdate.Start();
		lblVersion.Text = $"Version：{Assembly.GetExecutingAssembly().GetName().Version}";
	}

	public void LayoutDisplay()
	{
		int row = 0;
		int col = 0;
		int count = ProjectMgr.Inst.SysConfig.ViewCount;
		pnlMain.Controls.Clear();
		if (count == 0)
		{
			return;
		}
		if (count == 1)
		{
			row = (col = 1);
		}
		else if (count == 2)
		{
			row = 1;
			col = 2;
		}
		else if (count <= 4)
		{
			row = (col = 2);
		}
		else if (count <= 6)
		{
			row = 2;
			col = 3;
		}
		else if (count <= 9)
		{
			row = 3;
			col = 3;
		}
		else if (count <= 12)
		{
			row = 4;
			col = 3;
		}
		else if (count <= 16)
		{
			row = 4;
			col = 4;
		}
		pnlMain.RowCount = row;
		float rowp = 100f / (float)row;
		pnlMain.RowStyles.Clear();
		for (int i = 0; i < row; i++)
		{
			pnlMain.RowStyles.Add(new RowStyle(SizeType.Percent, rowp));
		}
		pnlMain.ColumnCount = col;
		float colp = 100f / (float)col;
		pnlMain.ColumnStyles.Clear();
		for (int j = 0; j < col; j++)
		{
			pnlMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, colp));
		}
		try
		{
			int index = 0;
			for (int k = 0; k < row; k++)
			{
				for (int l = 0; l < col; l++)
				{
					if (index >= count)
					{
						return;
					}
					CameraView cameraView = ProjectMgr.Inst.Views[index];
					cameraView.Dock = DockStyle.Fill;
					pnlMain.Controls.Add(ProjectMgr.Inst.Views[index], l, k);
					cameraView.UpdateViewSize();
					index++;
				}
			}
		}
		catch
		{
		}
		finally
		{
			ProjectMgr.Inst.Views.ForEach(delegate(CameraView t)
			{
				t.ViewModeChanged -= T_ViewModeChanged;
				t.ViewModeChanged += T_ViewModeChanged;
			});
		}
	}

	private void T_ViewModeChanged(object sender, int e)
	{
		if (sender is CameraView ctrl)
		{
			if (e == 1)
			{
				pnlMain.Controls.Clear();
				TableLayoutPanel tableLayoutPanel = pnlMain;
				int rowCount = (pnlMain.ColumnCount = 1);
				tableLayoutPanel.RowCount = rowCount;
				pnlMain.RowStyles.Clear();
				pnlMain.ColumnStyles.Clear();
				pnlMain.Controls.Add(ctrl);
				ctrl.UpdateViewSize();
			}
			else
			{
				LayoutDisplay();
			}
		}
	}

	public void SetOnline(bool isOnline)
	{
		ProjectMgr.Inst.IsOnLine = isOnline;
		bool isCh = ProjectMgr.Inst.SysConfig.Language == 0;
		btnOnline.Text = ((!isCh) ? (ProjectMgr.Inst.IsOnLine ? "Online" : "Offline") : (ProjectMgr.Inst.IsOnLine ? "在线中" : "离线中"));
		btnOnline.BackColor = (isOnline ? Color.Green : Color.Red);
		btnSystemSetting.Enabled = !isOnline;
		btnOperation.Enabled = !isOnline;
		if (!isOnline)
		{
			return;
		}
		Task.Run(async delegate
		{
			if (IsGoingOnline)
			{
				return;
			}
			try
			{
				IsGoingOnline = true;
				foreach (CvsInSightExt item in ProjectMgr.Inst.Cameras)
				{
					try
					{
						if (item.IsConnected)
						{
							await item.InSight.SetSoftOnlineAsync(value: true);
						}
					}
					catch
					{
					}
				}
			}
			finally
			{
				IsGoingOnline = false;
			}
		});
	}

	private void UpdateCtrlLag()
	{
		bool isCh = ProjectMgr.Inst.SysConfig.Language == 0;
		btnSystemSetting.Text = (isCh ? "设置" : "Setting");
		btnOperation.Text = (isCh ? "一键操作" : "Operation");
		btnOnline.Text = ((!isCh) ? (ProjectMgr.Inst.IsOnLine ? "Online" : "Offline") : (ProjectMgr.Inst.IsOnLine ? "在线中" : "离线中"));
	}

	private void btnOnline_Click(object sender, EventArgs e)
	{
		SetOnline(!ProjectMgr.Inst.IsOnLine);
	}

	private void btnSystemSetting_Click(object sender, EventArgs e)
	{
		ProjectMgr.Inst.ShowFrmSysSetting();
	}

	private void btnTheme_Click(object sender, EventArgs e)
	{
		ProjectMgr proj = ProjectMgr.Inst;
		proj.SysConfig.Theme = (ThemeManager.IsDark ? "light" : "dark");
		ThemeManager.Apply(proj.SysConfig.Theme);
		proj.SaveSysConfig();
	}

	private void FrmMain_FormClosing(object sender, FormClosingEventArgs e)
	{
		try
		{
			mutex?.ReleaseMutex();
			timerUpdate.Stop();
			ProjectMgr.Inst.Views.Clear();
			ProjectMgr.Inst.Cameras.ForEach(async delegate(CvsInSightExt t)
			{
				await t.InSight.Disconnect();
			});
			ProjectMgr.Inst.ActionQueueWorker.Close();
			ProjectMgr.Inst.ImageCleaner.StopClean();
			ProjectMgr.Inst.CloseForms();
			Process.GetCurrentProcess().Kill();
		}
		catch
		{
		}
	}

	private void cmbxLanguage_SelectedIndexChanged(object sender, AntdUI.IntEventArgs e)
	{
		ProjectMgr.Inst.SysConfig.Language = cmbxLanguage.SelectedIndex;
		ProjectMgr.Inst.SaveSysConfig();
		UpdateCtrlLag();
	}

	private void btnOperation_Click(object sender, EventArgs e)
	{
		ProjectMgr.Inst.ShowFrmOperation();
	}

	private void ApplyTheme()
	{
		toolbar.BackColor = ThemeManager.Bg2;
		statusPanel.BackColor = ThemeManager.Bg2;
		lblTime.ForeColor = ThemeManager.Fg;
		lblVersion.ForeColor = ThemeManager.FgDim;
		btnTheme.Text = (ThemeManager.IsDark ? "亮色模式" : "深色模式");
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
		System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(CameraHelper.FrmMain));
		this.pnlMain = new System.Windows.Forms.TableLayoutPanel();
		this.header = new AntdUI.PageHeader();
		this.toolbar = new System.Windows.Forms.Panel();
		this.btnOnline = new AntdUI.Button();
		this.btnSystemSetting = new AntdUI.Button();
		this.btnOperation = new AntdUI.Button();
		this.btnTheme = new AntdUI.Button();
		this.cmbxLanguage = new AntdUI.Select();
		this.statusPanel = new System.Windows.Forms.Panel();
		this.lblTime = new System.Windows.Forms.Label();
		this.lblVersion = new System.Windows.Forms.Label();
		this.toolbar.SuspendLayout();
		this.statusPanel.SuspendLayout();
		base.SuspendLayout();
		this.pnlMain.ColumnCount = 1;
		this.pnlMain.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100f));
		this.pnlMain.Dock = System.Windows.Forms.DockStyle.Fill;
		this.pnlMain.Location = new System.Drawing.Point(0, 90);
		this.pnlMain.Name = "pnlMain";
		this.pnlMain.RowCount = 1;
		this.pnlMain.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100f));
		this.pnlMain.Size = new System.Drawing.Size(1083, 631);
		this.pnlMain.TabIndex = 5;
		this.header.Dock = System.Windows.Forms.DockStyle.Top;
		this.header.Location = new System.Drawing.Point(0, 0);
		this.header.Name = "header";
		this.header.ShowButton = true;
		this.header.ShowIcon = true;
		this.header.Size = new System.Drawing.Size(1083, 40);
		this.header.TabIndex = 6;
		this.header.Text = "CameraHelperJimJack";
		this.toolbar.Controls.Add(this.btnOnline);
		this.toolbar.Controls.Add(this.btnSystemSetting);
		this.toolbar.Controls.Add(this.btnOperation);
		this.toolbar.Controls.Add(this.btnTheme);
		this.toolbar.Controls.Add(this.cmbxLanguage);
		this.toolbar.Dock = System.Windows.Forms.DockStyle.Top;
		this.toolbar.Location = new System.Drawing.Point(0, 40);
		this.toolbar.Name = "toolbar";
		this.toolbar.Size = new System.Drawing.Size(1083, 50);
		this.toolbar.TabIndex = 3;
		this.btnOnline.ForeColor = System.Drawing.Color.White;
		this.btnOnline.BackColor = System.Drawing.Color.Red;
		this.btnOnline.Location = new System.Drawing.Point(12, 8);
		this.btnOnline.Name = "btnOnline";
		this.btnOnline.Size = new System.Drawing.Size(96, 34);
		this.btnOnline.TabIndex = 0;
		this.btnOnline.Text = "离线中";
		this.btnOnline.Click += new System.EventHandler(btnOnline_Click);
		this.btnSystemSetting.Location = new System.Drawing.Point(118, 8);
		this.btnSystemSetting.Name = "btnSystemSetting";
		this.btnSystemSetting.Size = new System.Drawing.Size(96, 34);
		this.btnSystemSetting.TabIndex = 1;
		this.btnSystemSetting.Text = "设置";
		this.btnSystemSetting.Click += new System.EventHandler(btnSystemSetting_Click);
		this.btnOperation.Location = new System.Drawing.Point(224, 8);
		this.btnOperation.Name = "btnOperation";
		this.btnOperation.Size = new System.Drawing.Size(110, 34);
		this.btnOperation.TabIndex = 2;
		this.btnOperation.Text = "一键操作";
		this.btnOperation.Click += new System.EventHandler(btnOperation_Click);
		this.btnTheme.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.btnTheme.Location = new System.Drawing.Point(863, 8);
		this.btnTheme.Name = "btnTheme";
		this.btnTheme.Size = new System.Drawing.Size(90, 34);
		this.btnTheme.TabIndex = 3;
		this.btnTheme.Text = "深色模式";
		this.btnTheme.Click += new System.EventHandler(btnTheme_Click);
		this.cmbxLanguage.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.cmbxLanguage.Location = new System.Drawing.Point(961, 8);
		this.cmbxLanguage.Name = "cmbxLanguage";
		this.cmbxLanguage.Size = new System.Drawing.Size(110, 34);
		this.cmbxLanguage.TabIndex = 4;
		this.cmbxLanguage.Items.Add("中文");
		this.cmbxLanguage.Items.Add("English");
		this.cmbxLanguage.SelectedIndex = 0;
		this.cmbxLanguage.SelectedIndexChanged += cmbxLanguage_SelectedIndexChanged;
		this.statusPanel.Controls.Add(this.lblTime);
		this.statusPanel.Controls.Add(this.lblVersion);
		this.statusPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
		this.statusPanel.Location = new System.Drawing.Point(0, 721);
		this.statusPanel.Name = "statusPanel";
		this.statusPanel.Size = new System.Drawing.Size(1083, 26);
		this.statusPanel.TabIndex = 4;
		this.lblTime.AutoSize = true;
		this.lblTime.Location = new System.Drawing.Point(12, 4);
		this.lblTime.Name = "lblTime";
		this.lblTime.Size = new System.Drawing.Size(40, 20);
		this.lblTime.TabIndex = 0;
		this.lblTime.Text = "      ";
		this.lblVersion.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.lblVersion.AutoSize = true;
		this.lblVersion.Location = new System.Drawing.Point(883, 4);
		this.lblVersion.Name = "lblVersion";
		this.lblVersion.Size = new System.Drawing.Size(30, 20);
		this.lblVersion.TabIndex = 1;
		this.lblVersion.Text = "    ";
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
		base.ClientSize = new System.Drawing.Size(1083, 747);
		base.Controls.Add(this.pnlMain);
		base.Controls.Add(this.toolbar);
		base.Controls.Add(this.statusPanel);
		base.Controls.Add(this.header);
		this.Font = new System.Drawing.Font("微软雅黑", 10.5f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 134);
		base.Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
		base.Name = "FrmMain";
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
		this.Text = "CameraHelperJimJack";
		base.WindowState = System.Windows.Forms.FormWindowState.Maximized;
		base.FormClosing += new System.Windows.Forms.FormClosingEventHandler(FrmMain_FormClosing);
		base.Load += new System.EventHandler(FrmMain_Load);
		this.toolbar.ResumeLayout(false);
		this.statusPanel.ResumeLayout(false);
		this.statusPanel.PerformLayout();
		base.ResumeLayout(false);
		base.PerformLayout();
	}
}
