using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CameraHelper;

public class FrmSysSetting : Form
{
	private Timer updateTimer = new Timer();

	private IContainer components;

	private AntdUI.Tabs tabControl1;

	private AntdUI.TabPage tabCamSetting;

	private AntdUI.TabPage tabImageClean;

	private Panel panel2;

	private Panel panel3;

	private AntdUI.Button btnCameraCount;

	private AntdUI.Checkbox checkBoxCleanEnable;

	private AntdUI.Button btnCleanApply;

	private AntdUI.InputNumber numCleanInterval;

	private Label lblCleanInterval;

	private AntdUI.InputNumber numCleanRemaining;

	private AntdUI.Radio rbtnCleanEnableRemaining;

	private Label lblRemaining;

	private AntdUI.InputNumber numCleanHoldDay;

	private AntdUI.Radio rbtnCleanEnableHoldDay;

	private Label lblCleanHoldDays;

	private AntdUI.Checkbox ckbxCleanIsEmptyFolder;

	private Label lblCleanIsEmptyFolder;

	private AntdUI.InputNumber numCleanScanInterval;

	private Label lblCleanScanInterval;

	private AntdUI.Button btnCleanPathOpen;

	private AntdUI.Input txtCleanPath;

	private Label lblPath;

	private Label lblIP;

	private DataGridView dgvCameras;

	private AntdUI.Input txtIP;

	private AntdUI.Input txtPort;

	private Label lblPort;

	private Label lblUserName;

	private AntdUI.Input txtPassword;

	private Label lblPassword;

	private AntdUI.Input txtUserName;

	private Label lblShootKeyCell;

	private AntdUI.Input txtShotKeyCell;

	private AntdUI.Input txtResultCell;

	private Label lblResultCell;

	private AntdUI.Input txtSourcePathCell;

	private Label lblSourcePathCell;

	private AntdUI.TabPage tabOthers;

	private AntdUI.Checkbox ckbxTop;

	private AntdUI.TabPage tabViewSetting;

	private AntdUI.Button btnSaveView;

	private AntdUI.InputNumber numViewCount;

	private Label lblViewCount;

	private AntdUI.InputNumber numReviewCount;

	private Label lblMaxReviewCount;

	private AntdUI.Button btnSaveSys;

	private AntdUI.Button btnCamOK;

	private AntdUI.Button btnSaveReviewCount;

	private Label lblResultTips;

	private AntdUI.Input txtDumpPathCell;

	private Label lblResPathCell;

	private AntdUI.Checkbox ckbxEnableSelectScreen;

	private AntdUI.InputNumber numScreenIndex;

	private Label lblScreenIndex;

	private AntdUI.Checkbox chbxCloseFirewall;

	private Label lblRotation;

	private AntdUI.Select cmbxRotation;

	private AntdUI.Input txtNoRotation;

	private Label lblNoRotation;

	private AntdUI.Input txtProcessingName;

	private Label lblProcessingName;

	private Label lblTips;

	private Label lblDefaultResPath;

	private Label lblDefaultSourcePath;

	private ProjectMgr Proj => ProjectMgr.Inst;

	private InSightConfig CurCameraConfig
	{
		get
		{
			try
			{
				int index = dgvCameras.SelectedRows[0].Cells[0].RowIndex;
				return Proj.CameraConfigs[index];
			}
			catch
			{
				return null;
			}
		}
	}

	public FrmSysSetting()
	{
		InitializeComponent();
		ThemeManager.ApplyIcon(this);
		ThemeManager.ThemeChanged += ApplyTheme;
		ApplyTheme();
		bool isCh = ProjectMgr.Inst.SysConfig.Language == 0;
		Text = (isCh ? "系统设置" : "System Setting");
		tabCamSetting.Text = (isCh ? "相机设置" : "Camera Setting");
		btnCameraCount.Text = (isCh ? "相机数量" : "Camera Count");
		dgvCameras.Columns.Add(new DataGridViewTextBoxColumn
		{
			HeaderText = (isCh ? "序号" : "Index"),
			Width = 80
		});
		dgvCameras.Columns.Add(new DataGridViewTextBoxColumn
		{
			HeaderText = (isCh ? "状态" : "Status"),
			Width = 100
		});
		dgvCameras.Columns.Add(new DataGridViewTextBoxColumn
		{
			HeaderText = (isCh ? "名称" : "Name"),
			AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
		});
		lblIP.Text = (isCh ? "IP：" : "IP:");
		lblPort.Text = (isCh ? "端口：" : "Port:");
		lblUserName.Text = (isCh ? "用户名" : "UserName:");
		lblPassword.Text = (isCh ? "密码：" : "Password:");
		lblShootKeyCell.Text = (isCh ? "拍照标识单元格：" : "Shooting Key Cell:");
		lblResultCell.Text = (isCh ? "结果单元格" : "Result Cell:");
		lblResultTips.Text = (isCh ? "1:OK，其他值:NG" : "1:OK, Others:NG");
		lblSourcePathCell.Text = (isCh ? "原图路径单元格:" : "Source Image Cell");
		lblResPathCell.Text = (isCh ? "结果图路径单元格" : "Result Image Cell");
		lblRotation.Text = (isCh ? "图像旋转角度" : "Image Rotation");
		lblNoRotation.Text = (isCh ? "不旋转的标识" : "Sign Without Rotation");
		btnCamOK.Text = (isCh ? "应用" : "Apply");
		lblDefaultSourcePath.Text = (isCh ? "默认路径：" : "Default:") + ProjectMgr.SourceImageDir;
		lblDefaultResPath.Text = (isCh ? "默认路径：" : "Default:") + ProjectMgr.ResultImageDir;
		tabViewSetting.Text = (isCh ? "窗口设置" : "Window Setting");
		lblViewCount.Text = (isCh ? "窗口数量：" : "Window Count:");
		lblMaxReviewCount.Text = (isCh ? "缓存回看图片最大数量：" : "Review Maximum Count:");
		btnSaveView.Text = (isCh ? "应用" : "Apply");
		btnSaveReviewCount.Text = (isCh ? "应用" : "Apply");
		tabImageClean.Text = (isCh ? "图片清理" : "Image Cleaning");
		checkBoxCleanEnable.Text = (isCh ? "启用清理" : "Enable Cleaning");
		lblPath.Text = (isCh ? "路径：" : "Path:");
		btnCleanPathOpen.Text = (isCh ? "浏览" : "Browse");
		lblCleanScanInterval.Text = (isCh ? "扫描间隔（小时）：" : "Scaning Interval(hour):");
		lblCleanIsEmptyFolder.Text = (isCh ? "清理空文件夹：" : "Clean Empty Folders");
		ckbxCleanIsEmptyFolder.Text = (isCh ? "是" : "Enable");
		lblCleanHoldDays.Text = (isCh ? "文件保留天数：" : "Keeping Days Of Images:");
		rbtnCleanEnableHoldDay.Text = (isCh ? "启用" : "Enable");
		lblRemaining.Text = (isCh ? "磁盘剩余空间（GB）：" : "Remaining Space(GB):");
		rbtnCleanEnableRemaining.Text = (isCh ? "启用" : "Enable");
		lblCleanInterval.Text = (isCh ? "删除文件间隔（ms）：" : "Deletion Interval(ms):");
		btnCleanApply.Text = (isCh ? "应用" : "Apply");
		tabOthers.Text = (isCh ? "其他设置" : "Others");
		ckbxTop.Text = (isCh ? "软件置顶" : "Top Most");
		chbxCloseFirewall.Text = (isCh ? "关闭防火墙" : "Close The Firewall");
		ckbxEnableSelectScreen.Text = (isCh ? "选择显示的屏幕" : "Select The Displayed Screen");
		lblScreenIndex.Text = (isCh ? "屏幕序号：" : "Screen Index:");
		lblProcessingName.Text = (isCh ? "进程名：" : "Processing Name:");
		btnSaveSys.Text = (isCh ? "应用" : "Apply");
		lblTips.Text = (isCh ? "重启后生效" : "It will take effect after restarting");
	}

	private void ApplyTheme()
	{
		ForeColor = ThemeManager.Fg;
		BackColor = ThemeManager.Bg3;
		panel2.BackColor = ThemeManager.Bg3;
		panel3.BackColor = ThemeManager.Bg3;
		ThemeManager.StyleGrid(dgvCameras);
		ThemeManager.StyleButtons(this);
	}

	private void FrmSysSetting_Load(object sender, EventArgs e)
	{
		CreateCameraDgv();
		numViewCount.Value = Proj.SysConfig.ViewCount;
		numReviewCount.Value = Proj.SysConfig.ReviewMaxCount;
		checkBoxCleanEnable.Checked = Proj.CleanConfig.IsEnable;
		txtCleanPath.Text = Proj.CleanConfig.FolderPath;
		numCleanScanInterval.Value = (decimal)Proj.CleanConfig.ScanInterval;
		ckbxCleanIsEmptyFolder.Checked = Proj.CleanConfig.IsCleanEmptyFolders;
		rbtnCleanEnableHoldDay.Checked = Proj.CleanConfig.IsEnableHoldDays;
		numCleanHoldDay.Value = Proj.CleanConfig.HoldDays;
		rbtnCleanEnableRemaining.Checked = Proj.CleanConfig.IsEnableRemainingSpace;
		numCleanRemaining.Value = (decimal)Proj.CleanConfig.RemainingSpace;
		numCleanInterval.Value = Proj.CleanConfig.DeleteInterval;
		ckbxTop.Checked = Proj.SysConfig.AlwaysTop;
		chbxCloseFirewall.Checked = Proj.SysConfig.IsCloseFirewall;
		ckbxEnableSelectScreen.Checked = Proj.SysConfig.IsSelectScreen;
		numScreenIndex.Value = Proj.SysConfig.ScreenIndex;
		txtProcessingName.Text = Proj.SysConfig.ProcessingName;
		updateTimer.Interval = 1000;
		updateTimer.Tick += delegate
		{
			UpdateCameraDgv();
		};
		updateTimer.Start();
	}

	private void btnCameraCount_Click(object sender, EventArgs e)
	{
		FrmCamCount dlg = new FrmCamCount();
		dlg.ShowDialog();
		if (dlg.IsOK)
		{
			Proj.CameraConfigs.Clear();
			for (int i = 0; i < dlg.Count; i++)
			{
				Proj.CameraConfigs.Add(new InSightConfig
				{
					Index = i
				});
			}
			Proj.CreateCameras();
			CreateCameraDgv();
			Proj.SaveCameraConfigs();
		}
	}

	private void CreateCameraDgv()
	{
		dgvCameras.Rows.Clear();
		for (int i = 0; i < Proj.CameraConfigs.Count; i++)
		{
			DataGridViewRow row = new DataGridViewRow();
			row.Cells.Add(new DataGridViewTextBoxCell
			{
				Value = i
			});
			row.Cells.Add(new DataGridViewTextBoxCell());
			row.Cells.Add(new DataGridViewTextBoxCell());
			dgvCameras.Rows.Add(row);
		}
	}

	private void UpdateCameraDgv()
	{
		bool isCh = ProjectMgr.Inst.SysConfig.Language == 0;
		for (int i = 0; i < Proj.CameraConfigs.Count; i++)
		{
			try
			{
				int index = Proj.CameraConfigs[i].Index;
				CvsInSightExt cam = Proj.Cameras.FirstOrDefault((CvsInSightExt t) => t.Index == index);
				if (cam != null && cam.InSight.Connected)
				{
					bool isConnect = cam.IsConnected;
					string name = cam.InSight.CameraInfo.HostName;
					DataGridViewRow dataGridViewRow = dgvCameras.Rows[i];
					DataGridViewCell dataGridViewCell = dataGridViewRow.Cells[1];
					dataGridViewCell.Value = ((!isConnect) ? (isCh ? "未连接" : "Disconnected") : (isCh ? "已连接" : "Connected"));
					dataGridViewCell.Style.ForeColor = (isConnect ? Color.Green : Color.Gray);
					dataGridViewRow.Cells[2].Value = name;
				}
			}
			catch
			{
			}
		}
	}

	private void dgvCameras_SelectionChanged(object sender, EventArgs e)
	{
		try
		{
			InSightConfig config = CurCameraConfig;
			if (config != null)
			{
				txtIP.Text = config.Address;
				txtPort.Text = config.Port;
				txtUserName.Text = config.UserName;
				txtPassword.Text = config.Password;
				txtShotKeyCell.Text = config.ShotKeyCellName;
				txtResultCell.Text = config.ResultCellName;
				txtSourcePathCell.Text = config.SrcImagePathCellName;
				txtDumpPathCell.Text = config.DumpImagePathCellName;
				cmbxRotation.SelectedIndex = (int)config.Rotation;
				txtNoRotation.Text = config.CharWithoutRotation;
			}
		}
		catch
		{
		}
	}

	private async void btnCamOK_Click(object sender, EventArgs e)
	{
		try
		{
			InSightConfig config = CurCameraConfig;
			config.Address = txtIP.Text;
			config.Port = txtPort.Text;
			config.UserName = txtUserName.Text;
			config.Password = txtPassword.Text;
			config.ShotKeyCellName = txtShotKeyCell.Text;
			config.ResultCellName = txtResultCell.Text;
			config.SrcImagePathCellName = txtSourcePathCell.Text;
			config.DumpImagePathCellName = txtDumpPathCell.Text;
			config.Rotation = (ERotation)cmbxRotation.SelectedIndex;
			config.CharWithoutRotation = txtNoRotation.Text;
			await Proj.Cameras[config.Index].InSight.Disconnect();
			Proj.SaveCameraConfigs();
		}
		catch
		{
		}
	}

	private void btnSaveView_Click(object sender, EventArgs e)
	{
		Proj.SysConfig.ViewCount = (int)numViewCount.Value;
		Proj.CreateViewConfigs();
		Proj.CreateViews();
		Proj.SaveSysConfig();
		Proj.SaveViewConfigs();
		Proj.MainForm.LayoutDisplay();
	}

	private void btnSaveReviewCount_Click(object sender, EventArgs e)
	{
		Proj.SysConfig.ReviewMaxCount = (int)numReviewCount.Value;
		Proj.SaveSysConfig();
		Proj.Views.ForEach(delegate(CameraView t)
		{
			t.ClearRecord();
		});
	}

	private void btnCleanPathOpen_Click(object sender, EventArgs e)
	{
		FolderBrowserDialog dlg = new FolderBrowserDialog();
		dlg.RootFolder = Environment.SpecialFolder.MyComputer;
		if (dlg.ShowDialog() == DialogResult.OK)
		{
			txtCleanPath.Text = dlg.SelectedPath;
		}
	}

	private void btnCleanApply_Click(object sender, EventArgs e)
	{
		try
		{
			Proj.CleanConfig.IsEnable = checkBoxCleanEnable.Checked;
			Proj.CleanConfig.FolderPath = txtCleanPath.Text;
			Proj.CleanConfig.ScanInterval = (double)numCleanScanInterval.Value;
			Proj.CleanConfig.IsCleanEmptyFolders = ckbxCleanIsEmptyFolder.Checked;
			Proj.CleanConfig.IsEnableHoldDays = rbtnCleanEnableHoldDay.Checked;
			Proj.CleanConfig.HoldDays = (int)numCleanHoldDay.Value;
			Proj.CleanConfig.IsEnableRemainingSpace = rbtnCleanEnableRemaining.Checked;
			Proj.CleanConfig.RemainingSpace = (double)numCleanRemaining.Value;
			Proj.CleanConfig.DeleteInterval = (int)numCleanInterval.Value;
			Proj.ImageCleaner.StopClean();
			Proj.SaveCleanConfigs();
			Proj.ImageCleaner.StartClean();
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.Message);
		}
	}

	private void btnSaveSys_Click(object sender, EventArgs e)
	{
		Proj.SysConfig.AlwaysTop = ckbxTop.Checked;
		Proj.SysConfig.IsCloseFirewall = chbxCloseFirewall.Checked;
		Proj.SysConfig.IsSelectScreen = ckbxEnableSelectScreen.Checked;
		Proj.SysConfig.ScreenIndex = (int)numScreenIndex.Value;
		Proj.SysConfig.ProcessingName = txtProcessingName.Text;
		Proj.SaveSysConfig();
	}

	private void FrmSysSetting_FormClosing(object sender, FormClosingEventArgs e)
	{
		ProjectMgr.Inst.IsSetting = false;
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
		this.tabControl1 = new AntdUI.Tabs();
		this.tabCamSetting = new AntdUI.TabPage();
		this.txtNoRotation = new AntdUI.Input();
		this.lblNoRotation = new System.Windows.Forms.Label();
		this.cmbxRotation = new AntdUI.Select();
		this.lblRotation = new System.Windows.Forms.Label();
		this.txtDumpPathCell = new AntdUI.Input();
		this.lblResPathCell = new System.Windows.Forms.Label();
		this.lblResultTips = new System.Windows.Forms.Label();
		this.btnCamOK = new AntdUI.Button();
		this.txtSourcePathCell = new AntdUI.Input();
		this.lblSourcePathCell = new System.Windows.Forms.Label();
		this.txtResultCell = new AntdUI.Input();
		this.lblResultCell = new System.Windows.Forms.Label();
		this.txtShotKeyCell = new AntdUI.Input();
		this.lblShootKeyCell = new System.Windows.Forms.Label();
		this.txtPassword = new AntdUI.Input();
		this.lblPassword = new System.Windows.Forms.Label();
		this.txtUserName = new AntdUI.Input();
		this.lblUserName = new System.Windows.Forms.Label();
		this.txtPort = new AntdUI.Input();
		this.lblPort = new System.Windows.Forms.Label();
		this.txtIP = new AntdUI.Input();
		this.lblIP = new System.Windows.Forms.Label();
		this.panel2 = new System.Windows.Forms.Panel();
		this.dgvCameras = new System.Windows.Forms.DataGridView();
		this.panel3 = new System.Windows.Forms.Panel();
		this.btnCameraCount = new AntdUI.Button();
		this.tabViewSetting = new AntdUI.TabPage();
		this.btnSaveReviewCount = new AntdUI.Button();
		this.numReviewCount = new AntdUI.InputNumber();
		this.lblMaxReviewCount = new System.Windows.Forms.Label();
		this.btnSaveView = new AntdUI.Button();
		this.numViewCount = new AntdUI.InputNumber();
		this.lblViewCount = new System.Windows.Forms.Label();
		this.tabImageClean = new AntdUI.TabPage();
		this.checkBoxCleanEnable = new AntdUI.Checkbox();
		this.btnCleanApply = new AntdUI.Button();
		this.numCleanInterval = new AntdUI.InputNumber();
		this.lblCleanInterval = new System.Windows.Forms.Label();
		this.numCleanRemaining = new AntdUI.InputNumber();
		this.rbtnCleanEnableRemaining = new AntdUI.Radio();
		this.lblRemaining = new System.Windows.Forms.Label();
		this.numCleanHoldDay = new AntdUI.InputNumber();
		this.rbtnCleanEnableHoldDay = new AntdUI.Radio();
		this.lblCleanHoldDays = new System.Windows.Forms.Label();
		this.ckbxCleanIsEmptyFolder = new AntdUI.Checkbox();
		this.lblCleanIsEmptyFolder = new System.Windows.Forms.Label();
		this.numCleanScanInterval = new AntdUI.InputNumber();
		this.lblCleanScanInterval = new System.Windows.Forms.Label();
		this.btnCleanPathOpen = new AntdUI.Button();
		this.txtCleanPath = new AntdUI.Input();
		this.lblPath = new System.Windows.Forms.Label();
		this.tabOthers = new AntdUI.TabPage();
		this.lblTips = new System.Windows.Forms.Label();
		this.txtProcessingName = new AntdUI.Input();
		this.lblProcessingName = new System.Windows.Forms.Label();
		this.chbxCloseFirewall = new AntdUI.Checkbox();
		this.numScreenIndex = new AntdUI.InputNumber();
		this.lblScreenIndex = new System.Windows.Forms.Label();
		this.ckbxEnableSelectScreen = new AntdUI.Checkbox();
		this.btnSaveSys = new AntdUI.Button();
		this.ckbxTop = new AntdUI.Checkbox();
		this.lblDefaultSourcePath = new System.Windows.Forms.Label();
		this.lblDefaultResPath = new System.Windows.Forms.Label();
		this.panel2.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.dgvCameras).BeginInit();
		this.panel3.SuspendLayout();
		base.SuspendLayout();
		this.tabControl1.Dock = System.Windows.Forms.DockStyle.Fill;
		this.tabControl1.Location = new System.Drawing.Point(0, 0);
		this.tabControl1.Name = "tabControl1";
		this.tabControl1.Size = new System.Drawing.Size(960, 674);
		this.tabControl1.TabIndex = 3;
		this.tabControl1.Pages.Add(this.tabCamSetting);
		this.tabControl1.Pages.Add(this.tabViewSetting);
		this.tabControl1.Pages.Add(this.tabImageClean);
		this.tabControl1.Pages.Add(this.tabOthers);
		this.tabCamSetting.Controls.Add(this.lblDefaultResPath);
		this.tabCamSetting.Controls.Add(this.lblDefaultSourcePath);
		this.tabCamSetting.Controls.Add(this.txtNoRotation);
		this.tabCamSetting.Controls.Add(this.lblNoRotation);
		this.tabCamSetting.Controls.Add(this.cmbxRotation);
		this.tabCamSetting.Controls.Add(this.lblRotation);
		this.tabCamSetting.Controls.Add(this.txtDumpPathCell);
		this.tabCamSetting.Controls.Add(this.lblResPathCell);
		this.tabCamSetting.Controls.Add(this.lblResultTips);
		this.tabCamSetting.Controls.Add(this.btnCamOK);
		this.tabCamSetting.Controls.Add(this.txtSourcePathCell);
		this.tabCamSetting.Controls.Add(this.lblSourcePathCell);
		this.tabCamSetting.Controls.Add(this.txtResultCell);
		this.tabCamSetting.Controls.Add(this.lblResultCell);
		this.tabCamSetting.Controls.Add(this.txtShotKeyCell);
		this.tabCamSetting.Controls.Add(this.lblShootKeyCell);
		this.tabCamSetting.Controls.Add(this.txtPassword);
		this.tabCamSetting.Controls.Add(this.lblPassword);
		this.tabCamSetting.Controls.Add(this.txtUserName);
		this.tabCamSetting.Controls.Add(this.lblUserName);
		this.tabCamSetting.Controls.Add(this.txtPort);
		this.tabCamSetting.Controls.Add(this.lblPort);
		this.tabCamSetting.Controls.Add(this.txtIP);
		this.tabCamSetting.Controls.Add(this.lblIP);
		this.tabCamSetting.Controls.Add(this.panel2);
		this.tabCamSetting.Location = new System.Drawing.Point(4, 29);
		this.tabCamSetting.Name = "tabCamSetting";
		this.tabCamSetting.Size = new System.Drawing.Size(952, 641);
		this.tabCamSetting.TabIndex = 0;
		this.tabCamSetting.Text = "相机设置";
		this.txtNoRotation.Location = new System.Drawing.Point(539, 507);
		this.txtNoRotation.Name = "txtNoRotation";
		this.txtNoRotation.Size = new System.Drawing.Size(280, 34);
		this.txtNoRotation.TabIndex = 24;
		this.lblNoRotation.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.lblNoRotation.AutoSize = true;
		this.lblNoRotation.Location = new System.Drawing.Point(366, 512);
		this.lblNoRotation.Name = "lblNoRotation";
		this.lblNoRotation.Size = new System.Drawing.Size(107, 20);
		this.lblNoRotation.TabIndex = 23;
		this.lblNoRotation.Text = "不旋转的标识：";
		this.lblNoRotation.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.cmbxRotation.Location = new System.Drawing.Point(539, 450);
		this.cmbxRotation.Name = "cmbxRotation";
		this.cmbxRotation.Size = new System.Drawing.Size(280, 34);
		this.cmbxRotation.TabIndex = 22;
		this.cmbxRotation.Items.Add("0");
		this.cmbxRotation.Items.Add("90");
		this.cmbxRotation.Items.Add("180");
		this.cmbxRotation.Items.Add("270");
		this.lblRotation.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.lblRotation.AutoSize = true;
		this.lblRotation.Location = new System.Drawing.Point(366, 459);
		this.lblRotation.Name = "lblRotation";
		this.lblRotation.Size = new System.Drawing.Size(107, 20);
		this.lblRotation.TabIndex = 21;
		this.lblRotation.Text = "图像旋转角度：";
		this.lblRotation.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.txtDumpPathCell.Location = new System.Drawing.Point(539, 384);
		this.txtDumpPathCell.Name = "txtDumpPathCell";
		this.txtDumpPathCell.Size = new System.Drawing.Size(280, 34);
		this.txtDumpPathCell.TabIndex = 20;
		this.lblResPathCell.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.lblResPathCell.AutoSize = true;
		this.lblResPathCell.Location = new System.Drawing.Point(366, 391);
		this.lblResPathCell.Name = "lblResPathCell";
		this.lblResPathCell.Size = new System.Drawing.Size(135, 20);
		this.lblResPathCell.TabIndex = 19;
		this.lblResPathCell.Text = "结果图路径单元格：";
		this.lblResPathCell.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.lblResultTips.AutoSize = true;
		this.lblResultTips.Location = new System.Drawing.Point(825, 280);
		this.lblResultTips.Name = "lblResultTips";
		this.lblResultTips.Size = new System.Drawing.Size(113, 20);
		this.lblResultTips.TabIndex = 18;
		this.lblResultTips.Text = "1:OK, 其他值:NG";
		this.btnCamOK.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.btnCamOK.Location = new System.Drawing.Point(539, 560);
		this.btnCamOK.Name = "btnCamOK";
		this.btnCamOK.Size = new System.Drawing.Size(280, 35);
		this.btnCamOK.TabIndex = 17;
		this.btnCamOK.Text = "应用";
		this.btnCamOK.Click += new System.EventHandler(btnCamOK_Click);
		this.txtSourcePathCell.Location = new System.Drawing.Point(539, 320);
		this.txtSourcePathCell.Name = "txtSourcePathCell";
		this.txtSourcePathCell.Size = new System.Drawing.Size(280, 34);
		this.txtSourcePathCell.TabIndex = 15;
		this.lblSourcePathCell.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.lblSourcePathCell.AutoSize = true;
		this.lblSourcePathCell.Location = new System.Drawing.Point(366, 327);
		this.lblSourcePathCell.Name = "lblSourcePathCell";
		this.lblSourcePathCell.Size = new System.Drawing.Size(121, 20);
		this.lblSourcePathCell.TabIndex = 14;
		this.lblSourcePathCell.Text = "原图路径单元格：";
		this.lblSourcePathCell.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.txtResultCell.Location = new System.Drawing.Point(539, 269);
		this.txtResultCell.Name = "txtResultCell";
		this.txtResultCell.Size = new System.Drawing.Size(280, 34);
		this.txtResultCell.TabIndex = 13;
		this.lblResultCell.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.lblResultCell.AutoSize = true;
		this.lblResultCell.Location = new System.Drawing.Point(366, 276);
		this.lblResultCell.Name = "lblResultCell";
		this.lblResultCell.Size = new System.Drawing.Size(93, 20);
		this.lblResultCell.TabIndex = 12;
		this.lblResultCell.Text = "结果单元格：";
		this.lblResultCell.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.txtShotKeyCell.Location = new System.Drawing.Point(539, 218);
		this.txtShotKeyCell.Name = "txtShotKeyCell";
		this.txtShotKeyCell.Size = new System.Drawing.Size(280, 34);
		this.txtShotKeyCell.TabIndex = 10;
		this.lblShootKeyCell.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.lblShootKeyCell.AutoSize = true;
		this.lblShootKeyCell.Location = new System.Drawing.Point(366, 225);
		this.lblShootKeyCell.Name = "lblShootKeyCell";
		this.lblShootKeyCell.Size = new System.Drawing.Size(121, 20);
		this.lblShootKeyCell.TabIndex = 9;
		this.lblShootKeyCell.Text = "拍照标识单元格：";
		this.lblShootKeyCell.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.txtPassword.Location = new System.Drawing.Point(539, 167);
		this.txtPassword.Name = "txtPassword";
		this.txtPassword.Size = new System.Drawing.Size(280, 34);
		this.txtPassword.TabIndex = 8;
		this.lblPassword.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.lblPassword.AutoSize = true;
		this.lblPassword.Location = new System.Drawing.Point(366, 174);
		this.lblPassword.Name = "lblPassword";
		this.lblPassword.Size = new System.Drawing.Size(51, 20);
		this.lblPassword.TabIndex = 7;
		this.lblPassword.Text = "密码：";
		this.lblPassword.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.txtUserName.Location = new System.Drawing.Point(539, 116);
		this.txtUserName.Name = "txtUserName";
		this.txtUserName.Size = new System.Drawing.Size(280, 34);
		this.txtUserName.TabIndex = 6;
		this.lblUserName.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.lblUserName.AutoSize = true;
		this.lblUserName.Location = new System.Drawing.Point(366, 123);
		this.lblUserName.Name = "lblUserName";
		this.lblUserName.Size = new System.Drawing.Size(65, 20);
		this.lblUserName.TabIndex = 5;
		this.lblUserName.Text = "用户名：";
		this.lblUserName.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.txtPort.Location = new System.Drawing.Point(539, 65);
		this.txtPort.Name = "txtPort";
		this.txtPort.Size = new System.Drawing.Size(280, 34);
		this.txtPort.TabIndex = 4;
		this.lblPort.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.lblPort.AutoSize = true;
		this.lblPort.Location = new System.Drawing.Point(366, 72);
		this.lblPort.Name = "lblPort";
		this.lblPort.Size = new System.Drawing.Size(51, 20);
		this.lblPort.TabIndex = 3;
		this.lblPort.Text = "端口：";
		this.lblPort.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.txtIP.Location = new System.Drawing.Point(539, 14);
		this.txtIP.Name = "txtIP";
		this.txtIP.Size = new System.Drawing.Size(280, 34);
		this.txtIP.TabIndex = 2;
		this.lblIP.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.lblIP.AutoSize = true;
		this.lblIP.Location = new System.Drawing.Point(366, 21);
		this.lblIP.Name = "lblIP";
		this.lblIP.Size = new System.Drawing.Size(64, 20);
		this.lblIP.TabIndex = 1;
		this.lblIP.Text = "IP地址：";
		this.lblIP.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.panel2.Controls.Add(this.dgvCameras);
		this.panel2.Controls.Add(this.panel3);
		this.panel2.Dock = System.Windows.Forms.DockStyle.Left;
		this.panel2.Location = new System.Drawing.Point(3, 3);
		this.panel2.Name = "panel2";
		this.panel2.Size = new System.Drawing.Size(333, 635);
		this.panel2.TabIndex = 0;
		this.dgvCameras.AllowUserToAddRows = false;
		this.dgvCameras.AllowUserToDeleteRows = false;
		this.dgvCameras.AllowUserToResizeColumns = false;
		this.dgvCameras.AllowUserToResizeRows = false;
		this.dgvCameras.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
		this.dgvCameras.Dock = System.Windows.Forms.DockStyle.Fill;
		this.dgvCameras.Location = new System.Drawing.Point(0, 40);
		this.dgvCameras.MultiSelect = false;
		this.dgvCameras.Name = "dgvCameras";
		this.dgvCameras.RowHeadersVisible = false;
		this.dgvCameras.RowTemplate.Height = 23;
		this.dgvCameras.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
		this.dgvCameras.Size = new System.Drawing.Size(333, 595);
		this.dgvCameras.TabIndex = 2;
		this.dgvCameras.SelectionChanged += new System.EventHandler(dgvCameras_SelectionChanged);
		this.panel3.Controls.Add(this.btnCameraCount);
		this.panel3.Dock = System.Windows.Forms.DockStyle.Top;
		this.panel3.Location = new System.Drawing.Point(0, 0);
		this.panel3.Name = "panel3";
		this.panel3.Padding = new System.Windows.Forms.Padding(2);
		this.panel3.Size = new System.Drawing.Size(333, 40);
		this.panel3.TabIndex = 0;
		this.btnCameraCount.Location = new System.Drawing.Point(11, 5);
		this.btnCameraCount.Name = "btnCameraCount";
		this.btnCameraCount.Size = new System.Drawing.Size(128, 30);
		this.btnCameraCount.TabIndex = 2;
		this.btnCameraCount.Text = "相机数量";
		this.btnCameraCount.Click += new System.EventHandler(btnCameraCount_Click);
		this.tabViewSetting.Controls.Add(this.btnSaveReviewCount);
		this.tabViewSetting.Controls.Add(this.numReviewCount);
		this.tabViewSetting.Controls.Add(this.lblMaxReviewCount);
		this.tabViewSetting.Controls.Add(this.btnSaveView);
		this.tabViewSetting.Controls.Add(this.numViewCount);
		this.tabViewSetting.Controls.Add(this.lblViewCount);
		this.tabViewSetting.Location = new System.Drawing.Point(4, 22);
		this.tabViewSetting.Name = "tabViewSetting";
		this.tabViewSetting.Size = new System.Drawing.Size(952, 632);
		this.tabViewSetting.TabIndex = 3;
		this.tabViewSetting.Text = "窗口设置";
		this.btnSaveReviewCount.Location = new System.Drawing.Point(396, 96);
		this.btnSaveReviewCount.Name = "btnSaveReviewCount";
		this.btnSaveReviewCount.Size = new System.Drawing.Size(87, 35);
		this.btnSaveReviewCount.TabIndex = 37;
		this.btnSaveReviewCount.Text = "应用";
		this.btnSaveReviewCount.Click += new System.EventHandler(btnSaveReviewCount_Click);
		this.numReviewCount.Location = new System.Drawing.Point(240, 96);
		this.numReviewCount.Maximum = 200m;
		this.numReviewCount.Minimum = 1m;
		this.numReviewCount.Name = "numReviewCount";
		this.numReviewCount.Size = new System.Drawing.Size(140, 34);
		this.numReviewCount.TabIndex = 36;
		this.numReviewCount.Value = 1m;
		this.lblMaxReviewCount.AutoSize = true;
		this.lblMaxReviewCount.Location = new System.Drawing.Point(18, 104);
		this.lblMaxReviewCount.Name = "lblMaxReviewCount";
		this.lblMaxReviewCount.Size = new System.Drawing.Size(163, 20);
		this.lblMaxReviewCount.TabIndex = 35;
		this.lblMaxReviewCount.Text = "缓存回看图片最大数量：";
		this.btnSaveView.Location = new System.Drawing.Point(396, 35);
		this.btnSaveView.Name = "btnSaveView";
		this.btnSaveView.Size = new System.Drawing.Size(87, 35);
		this.btnSaveView.TabIndex = 34;
		this.btnSaveView.Text = "应用";
		this.btnSaveView.Click += new System.EventHandler(btnSaveView_Click);
		this.numViewCount.Location = new System.Drawing.Point(240, 35);
		this.numViewCount.Maximum = 16m;
		this.numViewCount.Minimum = 1m;
		this.numViewCount.Name = "numViewCount";
		this.numViewCount.Size = new System.Drawing.Size(140, 34);
		this.numViewCount.TabIndex = 33;
		this.numViewCount.Value = 1m;
		this.lblViewCount.AutoSize = true;
		this.lblViewCount.Location = new System.Drawing.Point(18, 42);
		this.lblViewCount.Name = "lblViewCount";
		this.lblViewCount.Size = new System.Drawing.Size(79, 20);
		this.lblViewCount.TabIndex = 32;
		this.lblViewCount.Text = "窗口数量：";
		this.tabImageClean.Controls.Add(this.checkBoxCleanEnable);
		this.tabImageClean.Controls.Add(this.btnCleanApply);
		this.tabImageClean.Controls.Add(this.numCleanInterval);
		this.tabImageClean.Controls.Add(this.lblCleanInterval);
		this.tabImageClean.Controls.Add(this.numCleanRemaining);
		this.tabImageClean.Controls.Add(this.rbtnCleanEnableRemaining);
		this.tabImageClean.Controls.Add(this.lblRemaining);
		this.tabImageClean.Controls.Add(this.numCleanHoldDay);
		this.tabImageClean.Controls.Add(this.rbtnCleanEnableHoldDay);
		this.tabImageClean.Controls.Add(this.lblCleanHoldDays);
		this.tabImageClean.Controls.Add(this.ckbxCleanIsEmptyFolder);
		this.tabImageClean.Controls.Add(this.lblCleanIsEmptyFolder);
		this.tabImageClean.Controls.Add(this.numCleanScanInterval);
		this.tabImageClean.Controls.Add(this.lblCleanScanInterval);
		this.tabImageClean.Controls.Add(this.btnCleanPathOpen);
		this.tabImageClean.Controls.Add(this.txtCleanPath);
		this.tabImageClean.Controls.Add(this.lblPath);
		this.tabImageClean.Location = new System.Drawing.Point(4, 22);
		this.tabImageClean.Name = "tabImageClean";
		this.tabImageClean.Size = new System.Drawing.Size(952, 632);
		this.tabImageClean.TabIndex = 1;
		this.tabImageClean.Text = "图片清理";
		this.checkBoxCleanEnable.AutoSize = true;
		this.checkBoxCleanEnable.Location = new System.Drawing.Point(251, 22);
		this.checkBoxCleanEnable.Name = "checkBoxCleanEnable";
		this.checkBoxCleanEnable.TabIndex = 33;
		this.checkBoxCleanEnable.Text = "启用清理";
		this.btnCleanApply.Location = new System.Drawing.Point(251, 435);
		this.btnCleanApply.Name = "btnCleanApply";
		this.btnCleanApply.Size = new System.Drawing.Size(375, 35);
		this.btnCleanApply.TabIndex = 32;
		this.btnCleanApply.Text = "应用";
		this.btnCleanApply.Click += new System.EventHandler(btnCleanApply_Click);
		this.numCleanInterval.Location = new System.Drawing.Point(251, 377);
		this.numCleanInterval.Maximum = 9999m;
		this.numCleanInterval.Minimum = 1m;
		this.numCleanInterval.Name = "numCleanInterval";
		this.numCleanInterval.Size = new System.Drawing.Size(140, 34);
		this.numCleanInterval.TabIndex = 31;
		this.numCleanInterval.Value = 1m;
		this.lblCleanInterval.AutoSize = true;
		this.lblCleanInterval.Location = new System.Drawing.Point(34, 383);
		this.lblCleanInterval.Name = "lblCleanInterval";
		this.lblCleanInterval.Size = new System.Drawing.Size(140, 20);
		this.lblCleanInterval.TabIndex = 30;
		this.lblCleanInterval.Text = "删除文件间隔（ms）";
		this.numCleanRemaining.Location = new System.Drawing.Point(325, 314);
		this.numCleanRemaining.Maximum = 9999m;
		this.numCleanRemaining.Minimum = 1m;
		this.numCleanRemaining.Name = "numCleanRemaining";
		this.numCleanRemaining.Size = new System.Drawing.Size(140, 34);
		this.numCleanRemaining.TabIndex = 29;
		this.numCleanRemaining.Value = 1m;
		this.rbtnCleanEnableRemaining.AutoSize = true;
		this.rbtnCleanEnableRemaining.Location = new System.Drawing.Point(251, 318);
		this.rbtnCleanEnableRemaining.Name = "rbtnCleanEnableRemaining";
		this.rbtnCleanEnableRemaining.TabIndex = 28;
		this.rbtnCleanEnableRemaining.Text = "启用";
		this.lblRemaining.AutoSize = true;
		this.lblRemaining.Location = new System.Drawing.Point(34, 320);
		this.lblRemaining.Name = "lblRemaining";
		this.lblRemaining.Size = new System.Drawing.Size(140, 20);
		this.lblRemaining.TabIndex = 27;
		this.lblRemaining.Text = "磁盘剩余空间（GB）";
		this.numCleanHoldDay.Location = new System.Drawing.Point(325, 251);
		this.numCleanHoldDay.Maximum = 9999m;
		this.numCleanHoldDay.Minimum = 1m;
		this.numCleanHoldDay.Name = "numCleanHoldDay";
		this.numCleanHoldDay.Size = new System.Drawing.Size(140, 34);
		this.numCleanHoldDay.TabIndex = 26;
		this.numCleanHoldDay.Value = 1m;
		this.rbtnCleanEnableHoldDay.AutoSize = true;
		this.rbtnCleanEnableHoldDay.Location = new System.Drawing.Point(251, 255);
		this.rbtnCleanEnableHoldDay.Name = "rbtnCleanEnableHoldDay";
		this.rbtnCleanEnableHoldDay.TabIndex = 25;
		this.rbtnCleanEnableHoldDay.Text = "启用";
		this.lblCleanHoldDays.AutoSize = true;
		this.lblCleanHoldDays.Location = new System.Drawing.Point(34, 257);
		this.lblCleanHoldDays.Name = "lblCleanHoldDays";
		this.lblCleanHoldDays.Size = new System.Drawing.Size(93, 20);
		this.lblCleanHoldDays.TabIndex = 24;
		this.lblCleanHoldDays.Text = "文件保留天数";
		this.ckbxCleanIsEmptyFolder.AutoSize = true;
		this.ckbxCleanIsEmptyFolder.Location = new System.Drawing.Point(251, 193);
		this.ckbxCleanIsEmptyFolder.Name = "ckbxCleanIsEmptyFolder";
		this.ckbxCleanIsEmptyFolder.TabIndex = 23;
		this.ckbxCleanIsEmptyFolder.Text = "是";
		this.lblCleanIsEmptyFolder.AutoSize = true;
		this.lblCleanIsEmptyFolder.Location = new System.Drawing.Point(34, 194);
		this.lblCleanIsEmptyFolder.Name = "lblCleanIsEmptyFolder";
		this.lblCleanIsEmptyFolder.Size = new System.Drawing.Size(93, 20);
		this.lblCleanIsEmptyFolder.TabIndex = 22;
		this.lblCleanIsEmptyFolder.Text = "清理空文件夹";
		this.numCleanScanInterval.DecimalPlaces = 2;
		this.numCleanScanInterval.Location = new System.Drawing.Point(251, 125);
		this.numCleanScanInterval.Minimum = 0.01m;
		this.numCleanScanInterval.Name = "numCleanScanInterval";
		this.numCleanScanInterval.Size = new System.Drawing.Size(140, 34);
		this.numCleanScanInterval.TabIndex = 21;
		this.numCleanScanInterval.Value = 1m;
		this.lblCleanScanInterval.AutoSize = true;
		this.lblCleanScanInterval.Location = new System.Drawing.Point(34, 131);
		this.lblCleanScanInterval.Name = "lblCleanScanInterval";
		this.lblCleanScanInterval.Size = new System.Drawing.Size(121, 20);
		this.lblCleanScanInterval.TabIndex = 20;
		this.lblCleanScanInterval.Text = "扫描间隔（小时）";
		this.btnCleanPathOpen.Location = new System.Drawing.Point(751, 61);
		this.btnCleanPathOpen.Name = "btnCleanPathOpen";
		this.btnCleanPathOpen.Size = new System.Drawing.Size(75, 35);
		this.btnCleanPathOpen.TabIndex = 19;
		this.btnCleanPathOpen.Text = "浏览";
		this.btnCleanPathOpen.Click += new System.EventHandler(btnCleanPathOpen_Click);
		this.txtCleanPath.Location = new System.Drawing.Point(251, 61);
		this.txtCleanPath.Name = "txtCleanPath";
		this.txtCleanPath.Size = new System.Drawing.Size(482, 34);
		this.txtCleanPath.TabIndex = 18;
		this.lblPath.AutoSize = true;
		this.lblPath.Location = new System.Drawing.Point(34, 68);
		this.lblPath.Name = "lblPath";
		this.lblPath.Size = new System.Drawing.Size(37, 20);
		this.lblPath.TabIndex = 17;
		this.lblPath.Text = "路径";
		this.tabOthers.Controls.Add(this.lblTips);
		this.tabOthers.Controls.Add(this.txtProcessingName);
		this.tabOthers.Controls.Add(this.lblProcessingName);
		this.tabOthers.Controls.Add(this.chbxCloseFirewall);
		this.tabOthers.Controls.Add(this.numScreenIndex);
		this.tabOthers.Controls.Add(this.lblScreenIndex);
		this.tabOthers.Controls.Add(this.ckbxEnableSelectScreen);
		this.tabOthers.Controls.Add(this.btnSaveSys);
		this.tabOthers.Controls.Add(this.ckbxTop);
		this.tabOthers.Location = new System.Drawing.Point(4, 22);
		this.tabOthers.Name = "tabOthers";
		this.tabOthers.Size = new System.Drawing.Size(952, 632);
		this.tabOthers.TabIndex = 2;
		this.tabOthers.Text = "其他设置";
		this.lblTips.AutoSize = true;
		this.lblTips.Location = new System.Drawing.Point(344, 272);
		this.lblTips.Name = "lblTips";
		this.lblTips.Size = new System.Drawing.Size(79, 20);
		this.lblTips.TabIndex = 42;
		this.lblTips.Text = "重启后生效";
		this.txtProcessingName.Location = new System.Drawing.Point(217, 196);
		this.txtProcessingName.Name = "txtProcessingName";
		this.txtProcessingName.Size = new System.Drawing.Size(206, 34);
		this.txtProcessingName.TabIndex = 41;
		this.lblProcessingName.AutoSize = true;
		this.lblProcessingName.Location = new System.Drawing.Point(81, 203);
		this.lblProcessingName.Name = "lblProcessingName";
		this.lblProcessingName.Size = new System.Drawing.Size(65, 20);
		this.lblProcessingName.TabIndex = 40;
		this.lblProcessingName.Text = "进程名：";
		this.chbxCloseFirewall.AutoSize = true;
		this.chbxCloseFirewall.Location = new System.Drawing.Point(85, 99);
		this.chbxCloseFirewall.Name = "chbxCloseFirewall";
		this.chbxCloseFirewall.TabIndex = 39;
		this.chbxCloseFirewall.Text = "关闭防火墙";
		this.numScreenIndex.Location = new System.Drawing.Point(467, 145);
		this.numScreenIndex.Maximum = 100m;
		this.numScreenIndex.Minimum = 0m;
		this.numScreenIndex.Name = "numScreenIndex";
		this.numScreenIndex.Size = new System.Drawing.Size(120, 34);
		this.numScreenIndex.TabIndex = 38;
		this.lblScreenIndex.AutoSize = true;
		this.lblScreenIndex.Location = new System.Drawing.Point(344, 151);
		this.lblScreenIndex.Name = "lblScreenIndex";
		this.lblScreenIndex.Size = new System.Drawing.Size(79, 20);
		this.lblScreenIndex.TabIndex = 37;
		this.lblScreenIndex.Text = "屏幕序号：";
		this.ckbxEnableSelectScreen.AutoSize = true;
		this.ckbxEnableSelectScreen.Location = new System.Drawing.Point(85, 151);
		this.ckbxEnableSelectScreen.Name = "ckbxEnableSelectScreen";
		this.ckbxEnableSelectScreen.TabIndex = 36;
		this.ckbxEnableSelectScreen.Text = "选择显示的屏幕";
		this.btnSaveSys.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
		this.btnSaveSys.Location = new System.Drawing.Point(85, 265);
		this.btnSaveSys.Name = "btnSaveSys";
		this.btnSaveSys.Size = new System.Drawing.Size(223, 35);
		this.btnSaveSys.TabIndex = 35;
		this.btnSaveSys.Text = "应用";
		this.btnSaveSys.Click += new System.EventHandler(btnSaveSys_Click);
		this.ckbxTop.AutoSize = true;
		this.ckbxTop.Location = new System.Drawing.Point(85, 47);
		this.ckbxTop.Name = "ckbxTop";
		this.ckbxTop.TabIndex = 1;
		this.ckbxTop.Text = "软件置顶";
		this.lblDefaultSourcePath.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.lblDefaultSourcePath.AutoSize = true;
		this.lblDefaultSourcePath.Location = new System.Drawing.Point(542, 354);
		this.lblDefaultSourcePath.Name = "lblDefaultSourcePath";
		this.lblDefaultSourcePath.Size = new System.Drawing.Size(33, 20);
		this.lblDefaultSourcePath.TabIndex = 25;
		this.lblDefaultSourcePath.Text = "      ";
		this.lblDefaultSourcePath.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.lblDefaultResPath.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.lblDefaultResPath.AutoSize = true;
		this.lblDefaultResPath.Location = new System.Drawing.Point(542, 418);
		this.lblDefaultResPath.Name = "lblDefaultResPath";
		this.lblDefaultResPath.Size = new System.Drawing.Size(33, 20);
		this.lblDefaultResPath.TabIndex = 26;
		this.lblDefaultResPath.Text = "      ";
		this.lblDefaultResPath.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
		base.ClientSize = new System.Drawing.Size(960, 674);
		base.Controls.Add(this.tabControl1);
		this.Font = new System.Drawing.Font("微软雅黑", 10.5f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 134);
		base.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		base.Name = "FrmSysSetting";
		base.ShowIcon = false;
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
		this.Text = "系统设置";
		base.FormClosing += new System.Windows.Forms.FormClosingEventHandler(FrmSysSetting_FormClosing);
		base.Load += new System.EventHandler(FrmSysSetting_Load);
		((System.ComponentModel.ISupportInitialize)this.dgvCameras).EndInit();
		this.panel2.ResumeLayout(false);
		this.panel3.ResumeLayout(false);
		base.ResumeLayout(false);
	}
}
