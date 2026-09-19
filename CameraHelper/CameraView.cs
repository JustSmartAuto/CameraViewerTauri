using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CameraHelper.Properties;

namespace CameraHelper;

public class CameraView : UserControl
{
	private object locker = new object();

	private const int NormalBorderSize = 2;

	private const int SelectBorderSize = 4;

	private int currentIndex = -1;

	private IContainer components;

	private ToolStrip toolStrip1;

	private ToolStripButton btnSetting;

	private ToolStripButton btnGrid;

	private ToolStripButton btnWeb;

	private ToolStripSeparator toolStripSeparator1;

	private ToolStripLabel lblCamName;

	private ToolStripSeparator toolStripSeparator2;

	private ToolStripLabel lblJobName;

	private ImageList imageListRecord;

	private PictureBox picMain;

	private ToolStripButton btnMaxi;

	private ToolStripButton btnHistory;

	private ToolStripLabel lblShotKey;

	private FlowLayoutPanel pnlRecord;

	private Button button2;

	private Button button1;

	private TableLayoutPanel tableLayoutPanel1;

	private Button btnNext;

	private Panel pnlBottom;

	private Button btnLast;

	public CameraViewConfig Config { get; private set; } = new CameraViewConfig();

	public CvsInSightExt Camera => ProjectMgr.Inst.Cameras.FirstOrDefault((CvsInSightExt t) => t.Index == Config.Index);

	private string CamreaName
	{
		get
		{
			if (ProjectMgr.Inst.SysConfig.Language != 0)
			{
				return "Camera:";
			}
			return "相机名：";
		}
	}

	private string ShotName
	{
		get
		{
			if (ProjectMgr.Inst.SysConfig.Language != 0)
			{
				return "Shoot:";
			}
			return "拍照：";
		}
	}

	public int ViewMode { get; private set; }

	public List<InSightRecord> Records { get; private set; } = new List<InSightRecord>();

	public bool IsEnableReview { get; set; }

	public event EventHandler<int> ViewModeChanged;

	public CameraView()
	{
		InitializeComponent();
		ThemeManager.ThemeChanged += ApplyTheme;
		ApplyTheme();
		pnlRecord.Controls.Clear();
	}

	private void ApplyTheme()
	{
		toolStrip1.BackColor = ThemeManager.Bg2;
		lblCamName.ForeColor = ThemeManager.Fg;
		lblShotKey.ForeColor = ThemeManager.Fg;
		lblJobName.ForeColor = ThemeManager.Fg;
		pnlRecord.BackColor = ThemeManager.Bg3;
		pnlBottom.BackColor = ThemeManager.Bg3;
		tableLayoutPanel1.BackColor = ThemeManager.Bg3;
		btnLast.BackColor = ThemeManager.Bg3;
		btnNext.BackColor = ThemeManager.Bg3;
	}

	public void SetConfig(CameraViewConfig config)
	{
		Config = config;
		UpdateConfigCtrls();
	}

	public void UpdateConfigCtrls()
	{
		try
		{
			if (Camera != null && Camera.InSight.Connected)
			{
				lblCamName.Text = CamreaName + Camera.InSight.CameraInfo.HostName;
				lblShotKey.Text = ShotName + Config.ShotKey;
				lblJobName.Text = string.Format("Job：{0}", Camera.InSight.JobInfo["name"]);
			}
			if (!pnlBottom.ClientRectangle.Contains(pnlBottom.PointToClient(Cursor.Position)))
			{
				IsEnableReview = false;
			}
		}
		catch
		{
		}
	}

	private Size GetImageSize(int baseSize)
	{
		if (baseSize < 16)
		{
			baseSize = 16;
		}
		if (baseSize > 256)
		{
			baseSize = 256;
		}
		double sw;
		double sh;
		try
		{
			int w = Camera.InSight.ViewPort.Width;
			int h = Camera.InSight.ViewPort.Height;
			if (h > w)
			{
				sw = (double)w * 1.0 / (double)h * (double)baseSize;
				sh = baseSize;
			}
			else
			{
				sw = baseSize;
				sh = (double)h * 1.0 / (double)w * (double)baseSize;
			}
		}
		catch
		{
			return new Size(baseSize, baseSize);
		}
		if (sw < 16.0)
		{
			sw = 16.0;
		}
		if (sw > 256.0)
		{
			sw = 256.0;
		}
		if (sh < 16.0)
		{
			sh = 16.0;
		}
		if (sh > 256.0)
		{
			sh = 256.0;
		}
		return new Size((int)sw, (int)sh);
	}

	public void UpdateViewSize()
	{
		try
		{
			int height = ProjectMgr.Inst.GetScreenHeight();
			int minSize = 20;
			int maxSize = 100;
			int size = (int)((double)((height - 60) / ProjectMgr.Inst.MainForm.Rows) * 0.2);
			if (ViewMode == 1)
			{
				maxSize = 220;
				size = (int)((double)(height - 60) * 0.3);
			}
			size = ((size > maxSize) ? maxSize : size);
			size = ((size < minSize) ? minSize : size);
			pnlBottom.MaximumSize = new Size(0, maxSize);
			pnlBottom.Height = size;
			imageListRecord.Images.Clear();
			imageListRecord.ImageSize = GetImageSize(size);
			foreach (InSightRecord item in Records)
			{
				imageListRecord.Images.Add(item.DumpImage);
			}
			foreach (Control control in pnlRecord.Controls)
			{
				int num = (control.Height = size);
				control.Width = num;
			}
			btnMaxi.Image = ((ViewMode == 0) ? Resources.Maxi : Resources.Mini);
		}
		catch
		{
		}
	}

	public void AddRecord(InSightRecord record)
	{
		lock (locker)
		{
			if (!base.IsHandleCreated || base.IsDisposed)
			{
				return;
			}
			Invoke((Action)delegate
			{
				try
				{
					if (ProjectMgr.Inst.IsOnLine && record.DumpImage != null)
					{
						if (!IsEnableReview)
						{
							if (Config.DisplayMode == 0)
							{
								picMain.Image = record.DumpImage.Clone() as Bitmap;
							}
							else if (Config.DisplayMode == 1 && record.Result != 1)
							{
								picMain.Image = record.DumpImage.Clone() as Bitmap;
							}
						}
						Records.Add(record);
						if (Records.Count > ProjectMgr.Inst.SysConfig.ReviewMaxCount)
						{
							for (int i = ProjectMgr.Inst.SysConfig.ReviewMaxCount; i < Records.Count; i++)
							{
								Records.RemoveAt(0);
							}
						}
						imageListRecord.Images.Clear();
						imageListRecord.ImageSize = GetImageSize(pnlBottom.Height);
						foreach (InSightRecord current in Records)
						{
							imageListRecord.Images.Add(current.DumpImage);
						}
						if (pnlRecord.Controls.Count > Records.Count)
						{
							for (int j = pnlRecord.Controls.Count; j < Records.Count; j++)
							{
								pnlRecord.Controls.RemoveAt(0);
							}
						}
						else if (pnlRecord.Controls.Count < Records.Count)
						{
							pnlRecord.Controls.Add(CreateButton());
						}
						UpdateReviewBtns();
						foreach (Control control in pnlRecord.Controls)
						{
							control.Invalidate();
						}
						try
						{
							pnlRecord.ScrollControlIntoView(pnlRecord.Controls[pnlRecord.Controls.Count - 1]);
						}
						catch
						{
						}
						if (Config.SaveImageMode == 1 || (Config.SaveImageMode == 2 && record.Result != 1))
						{
							if (Config.SaveImageType == 0)
							{
								ProjectMgr.Inst.ActionQueueWorker.Enqueue(new SaveImageAction
								{
									SaveSrcImage = record.SourceImage,
									SaveSrcPath = record.SrcImagePath,
									SaveDumpImage = record.DumpImage,
									SaveDumpPath = record.DumpImagePath
								});
							}
							else if (Config.SaveImageType == 1)
							{
								ProjectMgr.Inst.ActionQueueWorker.Enqueue(new SaveImageAction
								{
									SaveDumpImage = record.DumpImage,
									SaveDumpPath = record.DumpImagePath
								});
							}
							record.SourceImage = null;
						}
					}
				}
				catch (Exception ex)
				{
					LogMgr.Log("显示图像异常：" + ex.Message + "\r\n" + ex.StackTrace);
				}
			});
		}
	}

	public void ClearRecord()
	{
		lock (locker)
		{
			if (base.IsHandleCreated && !base.IsDisposed)
			{
				Invoke((Action)delegate
				{
					Records.Clear();
					imageListRecord.Images.Clear();
					pnlRecord.Controls.Clear();
				});
			}
		}
	}

	private Button CreateButton()
	{
		Button button = new Button();
		button.Text = string.Empty;
		button.FlatStyle = FlatStyle.Flat;
		button.ImageList = imageListRecord;
		button.ImageIndex = imageListRecord.Images.Count - 1;
		button.Width = pnlRecord.Height;
		button.Height = pnlRecord.Height;
		button.Margin = new Padding(0);
		button.FlatAppearance.BorderColor = Color.Green;
		button.FlatAppearance.BorderSize = 2;
		button.Click += Btn_Click;
		return button;
	}

	private void Btn_Click(object sender, EventArgs e)
	{
		try
		{
			int index = (int)(sender as Button).Tag;
			currentIndex = index;
			SelectBtn(currentIndex, needToScroll: false);
		}
		catch
		{
		}
	}

	private void SelectBtn(int index, bool needToScroll)
	{
		try
		{
			foreach (Control control in pnlRecord.Controls)
			{
				if (control is Button b)
				{
					b.FlatAppearance.BorderSize = 2;
				}
			}
			Button btn = (Button)pnlRecord.Controls[index];
			btn.FlatAppearance.BorderSize = 4;
			IsEnableReview = true;
			picMain.Image = Records[index].DumpImage.Clone() as Image;
			if (needToScroll)
			{
				pnlRecord.ScrollControlIntoView(btn);
			}
		}
		catch
		{
		}
	}

	private void UpdateReviewBtns()
	{
		for (int i = 0; i < pnlRecord.Controls.Count; i++)
		{
			try
			{
				Button btn = pnlRecord.Controls[i] as Button;
				InSightRecord rec = Records[i];
				btn.ImageIndex = i;
				btn.Tag = i;
				btn.FlatAppearance.BorderColor = ((rec.Result == 1) ? Color.Green : Color.Red);
				if (Config.ReviewMode == 1 && rec.Result == 1)
				{
					btn.Visible = true;
				}
				else if (Config.ReviewMode == 2 && rec.Result != 1)
				{
					btn.Visible = true;
				}
				else if (Config.ReviewMode == 0)
				{
					btn.Visible = true;
				}
				else
				{
					btn.Visible = false;
				}
			}
			catch
			{
			}
		}
	}

	private void btnSetting_Click(object sender, EventArgs e)
	{
		ProjectMgr.Inst.ShowFrmViewSetting(Config);
	}

	private void btnGrid_Click(object sender, EventArgs e)
	{
		ProjectMgr.Inst.ShowFrmGrid(Camera.InSight);
	}

	private void btnWeb_Click(object sender, EventArgs e)
	{
		ProjectMgr.Inst.ShowFrmHMI(Camera.Config);
	}

	private void btnHistory_Click(object sender, EventArgs e)
	{
		try
		{
			InSightRecord last = Records.LastOrDefault();
			if (last != null)
			{
				ProjectMgr.Inst.ShowFrmHistory(last.DumpImagePath);
			}
			else
			{
				ProjectMgr.Inst.ShowFrmHistory("");
			}
		}
		catch
		{
		}
	}

	private void btnMaxi_Click(object sender, EventArgs e)
	{
		try
		{
			ViewMode = ((ViewMode == 0) ? 1 : 0);
			this.ViewModeChanged?.Invoke(this, ViewMode);
		}
		catch
		{
		}
	}

	private void btnNext_Click(object sender, EventArgs e)
	{
		int max = Records.Count - 1;
		currentIndex = ((currentIndex > max) ? max : (currentIndex + 1));
		SelectBtn(currentIndex, needToScroll: true);
	}

	private void btnLast_Click(object sender, EventArgs e)
	{
		currentIndex = ((currentIndex >= 0) ? (currentIndex - 1) : 0);
		SelectBtn(currentIndex, needToScroll: true);
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
		this.components = new System.ComponentModel.Container();
		System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(CameraHelper.CameraView));
		this.toolStrip1 = new System.Windows.Forms.ToolStrip();
		this.btnMaxi = new System.Windows.Forms.ToolStripButton();
		this.btnSetting = new System.Windows.Forms.ToolStripButton();
		this.btnGrid = new System.Windows.Forms.ToolStripButton();
		this.btnWeb = new System.Windows.Forms.ToolStripButton();
		this.btnHistory = new System.Windows.Forms.ToolStripButton();
		this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
		this.lblCamName = new System.Windows.Forms.ToolStripLabel();
		this.lblShotKey = new System.Windows.Forms.ToolStripLabel();
		this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
		this.lblJobName = new System.Windows.Forms.ToolStripLabel();
		this.imageListRecord = new System.Windows.Forms.ImageList(this.components);
		this.pnlRecord = new System.Windows.Forms.FlowLayoutPanel();
		this.button2 = new System.Windows.Forms.Button();
		this.button1 = new System.Windows.Forms.Button();
		this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
		this.btnLast = new System.Windows.Forms.Button();
		this.btnNext = new System.Windows.Forms.Button();
		this.pnlBottom = new System.Windows.Forms.Panel();
		this.picMain = new System.Windows.Forms.PictureBox();
		this.toolStrip1.SuspendLayout();
		this.pnlRecord.SuspendLayout();
		this.tableLayoutPanel1.SuspendLayout();
		this.pnlBottom.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.picMain).BeginInit();
		base.SuspendLayout();
		this.toolStrip1.BackColor = System.Drawing.Color.Gold;
		this.toolStrip1.Font = new System.Drawing.Font("微软雅黑", 10.5f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 134);
		this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[10] { this.btnMaxi, this.btnSetting, this.btnGrid, this.btnWeb, this.btnHistory, this.toolStripSeparator1, this.lblCamName, this.lblShotKey, this.toolStripSeparator2, this.lblJobName });
		this.toolStrip1.Location = new System.Drawing.Point(0, 0);
		this.toolStrip1.Name = "toolStrip1";
		this.toolStrip1.Padding = new System.Windows.Forms.Padding(1);
		this.toolStrip1.Size = new System.Drawing.Size(680, 38);
		this.toolStrip1.TabIndex = 0;
		this.toolStrip1.Text = "toolStrip1";
		this.btnMaxi.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
		this.btnMaxi.Image = CameraHelper.Properties.Resources.Maxi;
		this.btnMaxi.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
		this.btnMaxi.ImageTransparentColor = System.Drawing.Color.Magenta;
		this.btnMaxi.Margin = new System.Windows.Forms.Padding(5, 0, 5, 0);
		this.btnMaxi.Name = "btnMaxi";
		this.btnMaxi.RightToLeft = System.Windows.Forms.RightToLeft.No;
		this.btnMaxi.Size = new System.Drawing.Size(36, 36);
		this.btnMaxi.Click += new System.EventHandler(btnMaxi_Click);
		this.btnSetting.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
		this.btnSetting.Image = CameraHelper.Properties.Resources.SettingM;
		this.btnSetting.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
		this.btnSetting.ImageTransparentColor = System.Drawing.Color.Magenta;
		this.btnSetting.Margin = new System.Windows.Forms.Padding(5, 0, 5, 0);
		this.btnSetting.Name = "btnSetting";
		this.btnSetting.Size = new System.Drawing.Size(36, 36);
		this.btnSetting.Click += new System.EventHandler(btnSetting_Click);
		this.btnGrid.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
		this.btnGrid.Image = CameraHelper.Properties.Resources.Grid2;
		this.btnGrid.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
		this.btnGrid.ImageTransparentColor = System.Drawing.Color.Magenta;
		this.btnGrid.Margin = new System.Windows.Forms.Padding(5, 0, 5, 0);
		this.btnGrid.Name = "btnGrid";
		this.btnGrid.Size = new System.Drawing.Size(36, 36);
		this.btnGrid.Click += new System.EventHandler(btnGrid_Click);
		this.btnWeb.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
		this.btnWeb.Image = CameraHelper.Properties.Resources.web;
		this.btnWeb.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
		this.btnWeb.ImageTransparentColor = System.Drawing.Color.Magenta;
		this.btnWeb.Margin = new System.Windows.Forms.Padding(5, 0, 5, 0);
		this.btnWeb.Name = "btnWeb";
		this.btnWeb.Size = new System.Drawing.Size(42, 36);
		this.btnWeb.Click += new System.EventHandler(btnWeb_Click);
		this.btnHistory.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
		this.btnHistory.Image = CameraHelper.Properties.Resources.Search;
		this.btnHistory.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
		this.btnHistory.ImageTransparentColor = System.Drawing.Color.Magenta;
		this.btnHistory.Margin = new System.Windows.Forms.Padding(5, 0, 5, 0);
		this.btnHistory.Name = "btnHistory";
		this.btnHistory.Size = new System.Drawing.Size(36, 36);
		this.btnHistory.Click += new System.EventHandler(btnHistory_Click);
		this.toolStripSeparator1.Name = "toolStripSeparator1";
		this.toolStripSeparator1.Size = new System.Drawing.Size(6, 36);
		this.lblCamName.Margin = new System.Windows.Forms.Padding(0, 1, 10, 2);
		this.lblCamName.Name = "lblCamName";
		this.lblCamName.Size = new System.Drawing.Size(25, 33);
		this.lblCamName.Text = "    ";
		this.lblShotKey.Name = "lblShotKey";
		this.lblShotKey.Size = new System.Drawing.Size(25, 33);
		this.lblShotKey.Text = "    ";
		this.toolStripSeparator2.Name = "toolStripSeparator2";
		this.toolStripSeparator2.Size = new System.Drawing.Size(6, 36);
		this.lblJobName.Name = "lblJobName";
		this.lblJobName.Size = new System.Drawing.Size(25, 33);
		this.lblJobName.Text = "    ";
		this.imageListRecord.ImageStream = (System.Windows.Forms.ImageListStreamer)resources.GetObject("imageListRecord.ImageStream");
		this.imageListRecord.TransparentColor = System.Drawing.Color.Transparent;
		this.imageListRecord.Images.SetKeyName(0, "web.png");
		this.imageListRecord.Images.SetKeyName(1, "ParamM.png");
		this.imageListRecord.Images.SetKeyName(2, "SettingM.png");
		this.pnlRecord.AutoScroll = true;
		this.pnlRecord.Controls.Add(this.button2);
		this.pnlRecord.Controls.Add(this.button1);
		this.pnlRecord.Dock = System.Windows.Forms.DockStyle.Fill;
		this.pnlRecord.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
		this.pnlRecord.Location = new System.Drawing.Point(35, 0);
		this.pnlRecord.Name = "pnlRecord";
		this.pnlRecord.Size = new System.Drawing.Size(645, 100);
		this.pnlRecord.TabIndex = 6;
		this.pnlRecord.WrapContents = false;
		this.button2.FlatAppearance.BorderColor = System.Drawing.Color.Red;
		this.button2.FlatAppearance.BorderSize = 3;
		this.button2.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.button2.ImageIndex = 0;
		this.button2.ImageList = this.imageListRecord;
		this.button2.Location = new System.Drawing.Point(545, 0);
		this.button2.Margin = new System.Windows.Forms.Padding(0);
		this.button2.Name = "button2";
		this.button2.Size = new System.Drawing.Size(100, 100);
		this.button2.TabIndex = 5;
		this.button2.UseVisualStyleBackColor = true;
		this.button1.FlatAppearance.BorderColor = System.Drawing.Color.Green;
		this.button1.FlatAppearance.BorderSize = 3;
		this.button1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.button1.ImageIndex = 0;
		this.button1.ImageList = this.imageListRecord;
		this.button1.Location = new System.Drawing.Point(445, 0);
		this.button1.Margin = new System.Windows.Forms.Padding(0);
		this.button1.Name = "button1";
		this.button1.Size = new System.Drawing.Size(100, 100);
		this.button1.TabIndex = 4;
		this.button1.UseVisualStyleBackColor = true;
		this.tableLayoutPanel1.ColumnCount = 1;
		this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50f));
		this.tableLayoutPanel1.Controls.Add(this.btnLast, 0, 1);
		this.tableLayoutPanel1.Controls.Add(this.btnNext, 0, 0);
		this.tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Left;
		this.tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
		this.tableLayoutPanel1.Name = "tableLayoutPanel1";
		this.tableLayoutPanel1.RowCount = 2;
		this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50f));
		this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50f));
		this.tableLayoutPanel1.Size = new System.Drawing.Size(35, 100);
		this.tableLayoutPanel1.TabIndex = 7;
		this.btnLast.Dock = System.Windows.Forms.DockStyle.Fill;
		this.btnLast.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.btnLast.Image = CameraHelper.Properties.Resources.you1;
		this.btnLast.Location = new System.Drawing.Point(1, 51);
		this.btnLast.Margin = new System.Windows.Forms.Padding(1);
		this.btnLast.Name = "btnLast";
		this.btnLast.Size = new System.Drawing.Size(33, 48);
		this.btnLast.TabIndex = 1;
		this.btnLast.UseVisualStyleBackColor = true;
		this.btnLast.Click += new System.EventHandler(btnLast_Click);
		this.btnNext.Dock = System.Windows.Forms.DockStyle.Fill;
		this.btnNext.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.btnNext.Image = CameraHelper.Properties.Resources.zuo1;
		this.btnNext.Location = new System.Drawing.Point(1, 1);
		this.btnNext.Margin = new System.Windows.Forms.Padding(1);
		this.btnNext.Name = "btnNext";
		this.btnNext.Size = new System.Drawing.Size(33, 48);
		this.btnNext.TabIndex = 0;
		this.btnNext.UseVisualStyleBackColor = true;
		this.btnNext.Click += new System.EventHandler(btnNext_Click);
		this.pnlBottom.Controls.Add(this.pnlRecord);
		this.pnlBottom.Controls.Add(this.tableLayoutPanel1);
		this.pnlBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
		this.pnlBottom.Location = new System.Drawing.Point(0, 398);
		this.pnlBottom.Name = "pnlBottom";
		this.pnlBottom.Size = new System.Drawing.Size(680, 100);
		this.pnlBottom.TabIndex = 8;
		this.picMain.BackColor = System.Drawing.Color.Black;
		this.picMain.Dock = System.Windows.Forms.DockStyle.Fill;
		this.picMain.Location = new System.Drawing.Point(0, 38);
		this.picMain.Name = "picMain";
		this.picMain.Size = new System.Drawing.Size(680, 360);
		this.picMain.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
		this.picMain.TabIndex = 3;
		this.picMain.TabStop = false;
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
		base.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
		base.Controls.Add(this.picMain);
		base.Controls.Add(this.pnlBottom);
		base.Controls.Add(this.toolStrip1);
		this.Font = new System.Drawing.Font("微软雅黑", 10.5f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 134);
		base.Margin = new System.Windows.Forms.Padding(1);
		base.Name = "CameraView";
		base.Size = new System.Drawing.Size(680, 498);
		this.toolStrip1.ResumeLayout(false);
		this.toolStrip1.PerformLayout();
		this.pnlRecord.ResumeLayout(false);
		this.tableLayoutPanel1.ResumeLayout(false);
		this.pnlBottom.ResumeLayout(false);
		((System.ComponentModel.ISupportInitialize)this.picMain).EndInit();
		base.ResumeLayout(false);
		base.PerformLayout();
	}
}
