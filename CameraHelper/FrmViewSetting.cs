using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace CameraHelper;

public class FrmViewSetting : Form
{
	private CameraViewConfig Config;

	private IContainer components;

	private AntdUI.Tabs tabControl1;

	private AntdUI.TabPage tabView;

	private Label lblShootTips;

	private AntdUI.Input txtShotID;

	private Label lblShoot;

	private AntdUI.InputNumber numCamIndex;

	private Label lblCamIndex;

	private Panel panel1;

	private AntdUI.Button btnOK;

	private AntdUI.Button btnClose;

	private AntdUI.Select cmbxDispMode;

	private Label lblDispMode;

	private AntdUI.TabPage tabSaveImage;

	private AntdUI.Select cmbxSaveType;

	private Label lblSaveType;

	private AntdUI.Select cmbxSaveMode;

	private Label lblSaveMode;

	private AntdUI.Select cmbxReviewMode;

	private Label lblReviewMode;

	private AntdUI.Button btnCamSetting;

	public FrmViewSetting()
	{
		InitializeComponent();
		ThemeManager.ThemeChanged += ApplyTheme;
		ApplyTheme();
		bool isCh = ProjectMgr.Inst.SysConfig.Language == 0;
		tabView.Text = (isCh ? "窗口设置" : "View Setting");
		tabSaveImage.Text = (isCh ? "保存图片" : "Save Image Setting");
		lblCamIndex.Text = (isCh ? "相机序号：" : "Camera Index:");
		btnCamSetting.Text = (isCh ? "相机设置" : "Camera Setting");
		lblShoot.Text = (isCh ? "拍照标识：" : "Image Key:");
		lblShootTips.Text = (isCh ? "(第几次拍照的标识)" : "(The index of taking images)");
		lblDispMode.Text = (isCh ? "显示模式：" : "Display Mode:");
		lblReviewMode.Text = (isCh ? "回看模式：" : "Review Mode:");
		lblSaveMode.Text = (isCh ? "保存模式：" : "Save Mode:");
		lblSaveType.Text = (isCh ? "保存类型：" : "Save Type:");
		cmbxDispMode.Items.Add(isCh ? "显示所有图片" : "Display All Images");
		cmbxDispMode.Items.Add(isCh ? "只显示NG图片" : "Display NG Images");
		cmbxReviewMode.Items.Add(isCh ? "显示所有图片" : "Display All Images");
		cmbxReviewMode.Items.Add(isCh ? "只显示OK图片" : "Display OK Images");
		cmbxReviewMode.Items.Add(isCh ? "只显示NG图片" : "Display NG Images");
		cmbxSaveMode.Items.Add(isCh ? "不保存" : "Not Save");
		cmbxSaveMode.Items.Add(isCh ? "保存所有" : "Save All Images");
		cmbxSaveMode.Items.Add(isCh ? "保存NG" : "Save NG Images");
		cmbxSaveType.Items.Add(isCh ? "保存所有" : "Save All Types");
		cmbxSaveType.Items.Add(isCh ? "只保存效果图" : "Save Result Images");
	}

	private void ApplyTheme()
	{
		ForeColor = ThemeManager.Fg;
		BackColor = ThemeManager.Bg3;
		panel1.BackColor = ThemeManager.Bg2;
		ThemeManager.StyleButtons(this);
	}

	public void Init(CameraViewConfig cfg)
	{
		Config = cfg;
		numCamIndex.Value = Config.Index;
		txtShotID.Text = Config.ShotKey;
		cmbxDispMode.SelectedIndex = Config.DisplayMode;
		cmbxReviewMode.SelectedIndex = Config.ReviewMode;
		cmbxSaveMode.SelectedIndex = Config.SaveImageMode;
		cmbxSaveType.SelectedIndex = Config.SaveImageType;
	}

	private void btnOK_Click(object sender, EventArgs e)
	{
		Config.Index = (int)numCamIndex.Value;
		Config.ShotKey = txtShotID.Text;
		Config.DisplayMode = cmbxDispMode.SelectedIndex;
		Config.SaveImageMode = cmbxSaveMode.SelectedIndex;
		Config.SaveImageType = cmbxSaveType.SelectedIndex;
		Config.ReviewMode = cmbxReviewMode.SelectedIndex;
		ProjectMgr.Inst.SaveViewConfigs();
		Close();
	}

	private void btnClose_Click(object sender, EventArgs e)
	{
		Close();
	}

	private void btnCamSetting_Click(object sender, EventArgs e)
	{
		ProjectMgr.Inst.ShowFrmSysSetting();
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
		this.tabView = new AntdUI.TabPage();
		this.btnCamSetting = new AntdUI.Button();
		this.cmbxReviewMode = new AntdUI.Select();
		this.lblReviewMode = new System.Windows.Forms.Label();
		this.cmbxDispMode = new AntdUI.Select();
		this.lblDispMode = new System.Windows.Forms.Label();
		this.lblShootTips = new System.Windows.Forms.Label();
		this.txtShotID = new AntdUI.Input();
		this.lblShoot = new System.Windows.Forms.Label();
		this.numCamIndex = new AntdUI.InputNumber();
		this.lblCamIndex = new System.Windows.Forms.Label();
		this.tabSaveImage = new AntdUI.TabPage();
		this.cmbxSaveType = new AntdUI.Select();
		this.lblSaveType = new System.Windows.Forms.Label();
		this.cmbxSaveMode = new AntdUI.Select();
		this.lblSaveMode = new System.Windows.Forms.Label();
		this.panel1 = new System.Windows.Forms.Panel();
		this.btnOK = new AntdUI.Button();
		this.btnClose = new AntdUI.Button();
		this.panel1.SuspendLayout();
		base.SuspendLayout();
		this.tabControl1.Controls.Add(this.tabView);
		this.tabControl1.Controls.Add(this.tabSaveImage);
		this.tabControl1.Dock = System.Windows.Forms.DockStyle.Fill;
		this.tabControl1.Location = new System.Drawing.Point(0, 0);
		this.tabControl1.Name = "tabControl1";
		this.tabControl1.Size = new System.Drawing.Size(513, 437);
		this.tabControl1.TabIndex = 0;
		this.tabControl1.Pages.Add(this.tabView);
		this.tabControl1.Pages.Add(this.tabSaveImage);
		this.tabView.Controls.Add(this.btnCamSetting);
		this.tabView.Controls.Add(this.cmbxReviewMode);
		this.tabView.Controls.Add(this.lblReviewMode);
		this.tabView.Controls.Add(this.cmbxDispMode);
		this.tabView.Controls.Add(this.lblDispMode);
		this.tabView.Controls.Add(this.lblShootTips);
		this.tabView.Controls.Add(this.txtShotID);
		this.tabView.Controls.Add(this.lblShoot);
		this.tabView.Controls.Add(this.numCamIndex);
		this.tabView.Controls.Add(this.lblCamIndex);
		this.tabView.Location = new System.Drawing.Point(4, 29);
		this.tabView.Name = "tabView";
		this.tabView.Size = new System.Drawing.Size(505, 404);
		this.tabView.TabIndex = 0;
		this.tabView.Text = "窗口设置";
		this.btnCamSetting.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.btnCamSetting.Location = new System.Drawing.Point(309, 30);
		this.btnCamSetting.Name = "btnCamSetting";
		this.btnCamSetting.Size = new System.Drawing.Size(84, 33);
		this.btnCamSetting.TabIndex = 18;
		this.btnCamSetting.Text = "相机设置";
		this.btnCamSetting.Click += new System.EventHandler(btnCamSetting_Click);
		this.cmbxReviewMode.Location = new System.Drawing.Point(157, 201);
		this.cmbxReviewMode.Name = "cmbxReviewMode";
		this.cmbxReviewMode.Size = new System.Drawing.Size(175, 34);
		this.cmbxReviewMode.TabIndex = 17;
		this.lblReviewMode.AutoSize = true;
		this.lblReviewMode.Location = new System.Drawing.Point(43, 208);
		this.lblReviewMode.Name = "lblReviewMode";
		this.lblReviewMode.Size = new System.Drawing.Size(79, 20);
		this.lblReviewMode.TabIndex = 16;
		this.lblReviewMode.Text = "回看模式：";
		this.cmbxDispMode.Location = new System.Drawing.Point(157, 145);
		this.cmbxDispMode.Name = "cmbxDispMode";
		this.cmbxDispMode.Size = new System.Drawing.Size(175, 34);
		this.cmbxDispMode.TabIndex = 15;
		this.lblDispMode.AutoSize = true;
		this.lblDispMode.Location = new System.Drawing.Point(43, 152);
		this.lblDispMode.Name = "lblDispMode";
		this.lblDispMode.Size = new System.Drawing.Size(79, 20);
		this.lblDispMode.TabIndex = 14;
		this.lblDispMode.Text = "显示模式：";
		this.lblShootTips.AutoSize = true;
		this.lblShootTips.Location = new System.Drawing.Point(283, 98);
		this.lblShootTips.Name = "lblShootTips";
		this.lblShootTips.Size = new System.Drawing.Size(131, 20);
		this.lblShootTips.TabIndex = 11;
		this.lblShootTips.Text = "(第几次拍照的标识)";
		this.txtShotID.Location = new System.Drawing.Point(155, 93);
		this.txtShotID.Name = "txtShotID";
		this.txtShotID.Size = new System.Drawing.Size(120, 34);
		this.txtShotID.TabIndex = 10;
		this.lblShoot.AutoSize = true;
		this.lblShoot.Location = new System.Drawing.Point(43, 96);
		this.lblShoot.Name = "lblShoot";
		this.lblShoot.Size = new System.Drawing.Size(79, 20);
		this.lblShoot.TabIndex = 9;
		this.lblShoot.Text = "拍照标识：";
		this.numCamIndex.Location = new System.Drawing.Point(157, 34);
		this.numCamIndex.Maximum = 9999m;
		this.numCamIndex.Name = "numCamIndex";
		this.numCamIndex.Size = new System.Drawing.Size(120, 34);
		this.numCamIndex.TabIndex = 8;
		this.numCamIndex.Value = 1m;
		this.lblCamIndex.AutoSize = true;
		this.lblCamIndex.Location = new System.Drawing.Point(43, 40);
		this.lblCamIndex.Name = "lblCamIndex";
		this.lblCamIndex.Size = new System.Drawing.Size(79, 20);
		this.lblCamIndex.TabIndex = 7;
		this.lblCamIndex.Text = "相机序号：";
		this.tabSaveImage.Controls.Add(this.cmbxSaveType);
		this.tabSaveImage.Controls.Add(this.lblSaveType);
		this.tabSaveImage.Controls.Add(this.cmbxSaveMode);
		this.tabSaveImage.Controls.Add(this.lblSaveMode);
		this.tabSaveImage.Location = new System.Drawing.Point(4, 22);
		this.tabSaveImage.Name = "tabSaveImage";
		this.tabSaveImage.Size = new System.Drawing.Size(505, 411);
		this.tabSaveImage.TabIndex = 1;
		this.tabSaveImage.Text = "保存图片";
		this.cmbxSaveType.Location = new System.Drawing.Point(162, 96);
		this.cmbxSaveType.Name = "cmbxSaveType";
		this.cmbxSaveType.Size = new System.Drawing.Size(198, 34);
		this.cmbxSaveType.TabIndex = 11;
		this.lblSaveType.AutoSize = true;
		this.lblSaveType.Location = new System.Drawing.Point(44, 103);
		this.lblSaveType.Name = "lblSaveType";
		this.lblSaveType.Size = new System.Drawing.Size(79, 20);
		this.lblSaveType.TabIndex = 10;
		this.lblSaveType.Text = "保存类型：";
		this.cmbxSaveMode.Location = new System.Drawing.Point(162, 33);
		this.cmbxSaveMode.Name = "cmbxSaveMode";
		this.cmbxSaveMode.Size = new System.Drawing.Size(198, 34);
		this.cmbxSaveMode.TabIndex = 9;
		this.lblSaveMode.AutoSize = true;
		this.lblSaveMode.Location = new System.Drawing.Point(44, 40);
		this.lblSaveMode.Name = "lblSaveMode";
		this.lblSaveMode.Size = new System.Drawing.Size(79, 20);
		this.lblSaveMode.TabIndex = 8;
		this.lblSaveMode.Text = "保存模式：";
		this.panel1.Controls.Add(this.btnOK);
		this.panel1.Controls.Add(this.btnClose);
		this.panel1.Dock = System.Windows.Forms.DockStyle.Bottom;
		this.panel1.Location = new System.Drawing.Point(0, 437);
		this.panel1.Name = "panel1";
		this.panel1.Size = new System.Drawing.Size(513, 39);
		this.panel1.TabIndex = 1;
		this.btnOK.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.btnOK.Location = new System.Drawing.Point(343, 3);
		this.btnOK.Name = "btnOK";
		this.btnOK.Size = new System.Drawing.Size(75, 33);
		this.btnOK.TabIndex = 1;
		this.btnOK.Text = "Yes";
		this.btnOK.Click += new System.EventHandler(btnOK_Click);
		this.btnClose.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.btnClose.Location = new System.Drawing.Point(431, 3);
		this.btnClose.Name = "btnClose";
		this.btnClose.Size = new System.Drawing.Size(75, 33);
		this.btnClose.TabIndex = 0;
		this.btnClose.Text = "No";
		this.btnClose.Click += new System.EventHandler(btnClose_Click);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
		base.ClientSize = new System.Drawing.Size(513, 476);
		base.Controls.Add(this.tabControl1);
		base.Controls.Add(this.panel1);
		this.Font = new System.Drawing.Font("微软雅黑", 10.5f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 134);
		base.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		base.Name = "FrmViewSetting";
		base.ShowIcon = false;
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
		this.Text = "设置";
		this.panel1.ResumeLayout(false);
		base.ResumeLayout(false);
	}
}
