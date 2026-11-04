using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace WebHMI;

public class FormSetting : Form
{
	private IContainer components;

	private Label label1;

	private NumericUpDown numCount;

	private TabControl tabControl1;

	private TabPage tabPage1;

	private Button btnDisplayCreate;

	private TabPage tabPage2;

	private TabPage tabPage3;

	private Panel panel1;

	private Button btnAddTransform;

	private ComboBox cmbxTransformName;

	private Button btnDeleteTransform;

	private Label label3;

	private Button btnCleanPathOpen;

	private TextBox txtCleanPath;

	private Label label2;

	private NumericUpDown numCleanScanInterval;

	private Label label4;

	private CheckBox ckbxCleanIsEmptyFolder;

	private Label label5;

	private NumericUpDown numCleanHoldDay;

	private RadioButton rbtnCleanEnableHoldDay;

	private Label label6;

	private RadioButton rbtnCleanEnableRemaining;

	private Label label7;

	private NumericUpDown numCleanRemaining;

	private NumericUpDown numCleanInterval;

	private Label label8;

	private Button btnCleanApply;

	private Label label10;

	private Button btnWatchPathOpen;

	private TextBox txtWatchPath;

	private Label label9;

	private TableLayoutPanel tableLayoutPanel1;

	private GroupBox groupBox1;

	private Panel panel2;

	private NumericUpDown numFileNameIndex;

	private Label label12;

	private NumericUpDown numFolderIndex;

	private TextBox txtSourceSpliter;

	private Label label11;

	private Panel pnlDirPartSrcIndex;

	private Panel panel4;

	private Button btnDirPartClear;

	private Button btnDirPartDel;

	private Button btnDirPartAdd;

	private Panel panel3;

	private TextBox txtFolderSpliter;

	private Label label13;

	private Panel pnlDirPartFormat;

	private TextBox txtDirPathFormat;

	private Label label15;

	private NumericUpDown numDirPartSrcIndex;

	private Label label14;

	private DataGridView dgvDirPathItems;

	private GroupBox groupBox2;

	private DataGridView dgvFilePartItems;

	private Panel pnlFilePartFormat;

	private TextBox txtFilePartFormat;

	private Label label16;

	private Panel pnlFilePartSrcIndex;

	private NumericUpDown numFilePartSrcIndex;

	private Label label17;

	private Panel panel9;

	private Button btnFilePartClear;

	private Button btnFilePartDel;

	private Button btnFilePartAdd;

	private Panel panel10;

	private TextBox txtFileSpilter;

	private Label label18;

	private Button btnTransformSave;

	private DataGridViewTextBoxColumn dataGridViewTextBoxColumn1;

	private DataGridViewTextBoxColumn dataGridViewTextBoxColumn2;

	private DataGridViewTextBoxColumn ColumnIndex;

	private DataGridViewTextBoxColumn ColumnPartType;

	private NumericUpDown numDelay;

	private Label label19;

	private CheckBox checkBoxCleanEnable;

	public CleanConfig CleanConfig => ProjectMgr.Inst.CleanConfig;

	public TransformConfig CurrentTransformConfig => ProjectMgr.Inst.TransformConfigs.FirstOrDefault((TransformConfig t) => t.Name == cmbxTransformName.Text);

	public PartItem CurrentDirPartItem
	{
		get
		{
			try
			{
				return CurrentTransformConfig.FolderPartList[dgvDirPathItems.SelectedCells[0].RowIndex];
			}
			catch
			{
				return null;
			}
		}
	}

	public PartItem CurrentFilePartItem
	{
		get
		{
			try
			{
				return CurrentTransformConfig.FileNamePartList[dgvFilePartItems.SelectedCells[0].RowIndex];
			}
			catch
			{
				return null;
			}
		}
	}

	public FormSetting()
	{
		InitializeComponent();
	}

	private void FormSysSetting_Load(object sender, EventArgs e)
	{
		numCount.Value = ProjectMgr.Inst.CameraConfig.Count;
		numDelay.Value = ProjectMgr.Inst.CameraConfig.Delay;
		checkBoxCleanEnable.Checked = CleanConfig.IsEnable;
		txtCleanPath.Text = CleanConfig.FolderPath;
		numCleanScanInterval.Value = (decimal)CleanConfig.ScanInterval;
		ckbxCleanIsEmptyFolder.Checked = CleanConfig.IsCleanEmptyFolders;
		rbtnCleanEnableHoldDay.Checked = CleanConfig.IsEnableHoldDays;
		numCleanHoldDay.Value = CleanConfig.HoldDays;
		rbtnCleanEnableRemaining.Checked = CleanConfig.IsEnableRemainingSpace;
		numCleanRemaining.Value = (decimal)CleanConfig.RemainingSpace;
		numCleanInterval.Value = CleanConfig.DeleteInterval;
		cmbxTransformName.Items.Clear();
		foreach (TransformConfig transformConfig in ProjectMgr.Inst.TransformConfigs)
		{
			cmbxTransformName.Items.Add(transformConfig.Name);
		}
		if (cmbxTransformName.Items.Count > 0)
		{
			cmbxTransformName.SelectedIndex = 0;
		}
	}

	private void btnDisplayCreate_Click(object sender, EventArgs e)
	{
		ProjectMgr.Inst.CreateCameraConfig((int)numCount.Value);
		ProjectMgr.Inst.SaveCameraConfig();
	}

	private void numDelay_ValueChanged(object sender, EventArgs e)
	{
		ProjectMgr.Inst.CameraConfig.Delay = (int)numDelay.Value;
		ProjectMgr.Inst.SaveCameraConfig();
	}

	private void checkBoxCleanEnable_CheckedChanged(object sender, EventArgs e)
	{
		CleanConfig.IsEnable = checkBoxCleanEnable.Checked;
	}

	private void txtCleanPath_TextChanged(object sender, EventArgs e)
	{
		CleanConfig.FolderPath = txtCleanPath.Text;
	}

	private void btnCleanPathOpen_Click(object sender, EventArgs e)
	{
		FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
		folderBrowserDialog.RootFolder = Environment.SpecialFolder.MyComputer;
		if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
		{
			txtCleanPath.Text = folderBrowserDialog.SelectedPath;
		}
	}

	private void numCleanScanInterval_ValueChanged(object sender, EventArgs e)
	{
		CleanConfig.ScanInterval = (double)numCleanScanInterval.Value;
	}

	private void ckbxCleanIsEmptyFolder_CheckedChanged(object sender, EventArgs e)
	{
		CleanConfig.IsCleanEmptyFolders = ckbxCleanIsEmptyFolder.Checked;
	}

	private void rbtnCleanEnableHoldDay_CheckedChanged(object sender, EventArgs e)
	{
		CleanConfig.IsEnableHoldDays = rbtnCleanEnableHoldDay.Checked;
	}

	private void numCleanHoldDay_ValueChanged(object sender, EventArgs e)
	{
		CleanConfig.HoldDays = (int)numCleanHoldDay.Value;
	}

	private void rbtnCleanEnableRemaining_CheckedChanged(object sender, EventArgs e)
	{
		CleanConfig.IsEnableRemainingSpace = rbtnCleanEnableRemaining.Checked;
	}

	private void numCleanRemaining_ValueChanged(object sender, EventArgs e)
	{
		CleanConfig.RemainingSpace = (double)numCleanRemaining.Value;
	}

	private void numCleanInterval_ValueChanged(object sender, EventArgs e)
	{
		CleanConfig.DeleteInterval = (int)numCleanInterval.Value;
	}

	private void btnCleanApply_Click(object sender, EventArgs e)
	{
		ProjectMgr.Inst.Cleaner.StopClean();
		ProjectMgr.Inst.SaveCleanConfig();
		ProjectMgr.Inst.Cleaner.StartClean();
	}

	private void cmbxTransformName_SelectedIndexChanged(object sender, EventArgs e)
	{
		try
		{
			txtWatchPath.Text = CurrentTransformConfig.WatchPath;
			txtSourceSpliter.Text = CurrentTransformConfig.SourceSpliter;
			numFolderIndex.Value = CurrentTransformConfig.FolderIndex;
			numFileNameIndex.Value = CurrentTransformConfig.FileNameIndex;
			txtFolderSpliter.Text = CurrentTransformConfig.FolderSpliter;
			txtFileSpilter.Text = CurrentTransformConfig.FileNameSpliter;
			UpdateDgvDirParts();
			UpdateDgvFileParts();
		}
		catch
		{
		}
	}

	private void btnTransformSave_Click(object sender, EventArgs e)
	{
		ProjectMgr.Inst.SaveTransformConfig();
		ProjectMgr.Inst.CreateFileTransform();
	}

	private void btnAddTransform_Click(object sender, EventArgs e)
	{
		FormAddName formAddName = new FormAddName();
		formAddName.ShowDialog();
		if (formAddName.IsOK)
		{
			ProjectMgr.Inst.TransformConfigs.Add(new TransformConfig
			{
				Name = formAddName.Output
			});
			cmbxTransformName.Items.Add(formAddName.Output);
			cmbxTransformName.SelectedIndex = cmbxTransformName.Items.Count - 1;
		}
	}

	private void btnDeleteTransform_Click(object sender, EventArgs e)
	{
		TransformConfig currentTransformConfig = CurrentTransformConfig;
		if (currentTransformConfig == null)
		{
			return;
		}
		ProjectMgr.Inst.TransformConfigs.Remove(currentTransformConfig);
		ProjectMgr.Inst.SaveTransformConfig();
		cmbxTransformName.Items.Clear();
		foreach (TransformConfig transformConfig in ProjectMgr.Inst.TransformConfigs)
		{
			cmbxTransformName.Items.Add(transformConfig.Name);
		}
		if (cmbxTransformName.Items.Count > 0)
		{
			cmbxTransformName.SelectedIndex = 0;
		}
	}

	private void btnWatchPathOpen_Click(object sender, EventArgs e)
	{
		if (CurrentTransformConfig != null)
		{
			FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
			folderBrowserDialog.RootFolder = Environment.SpecialFolder.MyComputer;
			if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
			{
				txtWatchPath.Text = folderBrowserDialog.SelectedPath;
			}
		}
	}

	private void txtWatchPath_TextChanged(object sender, EventArgs e)
	{
		if (CurrentTransformConfig != null)
		{
			CurrentTransformConfig.WatchPath = txtWatchPath.Text;
		}
	}

	private void txtSourceSpliter_TextChanged(object sender, EventArgs e)
	{
		if (CurrentTransformConfig != null)
		{
			CurrentTransformConfig.SourceSpliter = txtSourceSpliter.Text;
		}
	}

	private void numFolderIndex_ValueChanged(object sender, EventArgs e)
	{
		if (CurrentTransformConfig != null)
		{
			CurrentTransformConfig.FolderIndex = (int)numFolderIndex.Value;
		}
	}

	private void numFileNameIndex_ValueChanged(object sender, EventArgs e)
	{
		if (CurrentTransformConfig != null)
		{
			CurrentTransformConfig.FileNameIndex = (int)numFileNameIndex.Value;
		}
	}

	private void txtFolderSpliter_TextChanged(object sender, EventArgs e)
	{
		if (CurrentTransformConfig != null)
		{
			CurrentTransformConfig.FolderSpliter = txtFolderSpliter.Text;
		}
	}

	private void UpdateDgvDirParts()
	{
		if (CurrentTransformConfig == null)
		{
			return;
		}
		dgvDirPathItems.Rows.Clear();
		int num = 0;
		foreach (PartItem folderPart in CurrentTransformConfig.FolderPartList)
		{
			DataGridViewRow dataGridViewRow = new DataGridViewRow();
			dataGridViewRow.Cells.Add(new DataGridViewTextBoxCell
			{
				Value = num
			});
			dataGridViewRow.Cells.Add(new DataGridViewTextBoxCell
			{
				Value = ((folderPart.PartType == EPartType.DateTime) ? "PC时间" : "名称")
			});
			dgvDirPathItems.Rows.Add(dataGridViewRow);
			num++;
		}
		dgvDirPathItems.ClearSelection();
		pnlDirPartSrcIndex.Visible = false;
		pnlDirPartFormat.Visible = false;
	}

	private void dgvDirPathItems_SelectionChanged(object sender, EventArgs e)
	{
		PartItem currentDirPartItem = CurrentDirPartItem;
		if (currentDirPartItem != null)
		{
			if (currentDirPartItem.PartType == EPartType.Name)
			{
				pnlDirPartSrcIndex.Visible = true;
				pnlDirPartFormat.Visible = false;
				numDirPartSrcIndex.Value = currentDirPartItem.SourceIndex;
			}
			else
			{
				pnlDirPartSrcIndex.Visible = false;
				pnlDirPartFormat.Visible = true;
				txtDirPathFormat.Text = currentDirPartItem.Format;
			}
		}
	}

	private void btnDirPartAdd_Click(object sender, EventArgs e)
	{
		if (CurrentTransformConfig != null)
		{
			FormAddPart formAddPart = new FormAddPart();
			formAddPart.ShowDialog();
			if (formAddPart.IsOK)
			{
				CurrentTransformConfig.FolderPartList.Add(new PartItem
				{
					PartType = formAddPart.PartType
				});
				UpdateDgvDirParts();
			}
		}
	}

	private void btnDirPartDel_Click(object sender, EventArgs e)
	{
		if (CurrentTransformConfig != null)
		{
			PartItem currentDirPartItem = CurrentDirPartItem;
			if (currentDirPartItem != null)
			{
				CurrentTransformConfig.FolderPartList.Remove(currentDirPartItem);
				UpdateDgvDirParts();
			}
		}
	}

	private void btnDirPartClear_Click(object sender, EventArgs e)
	{
		if (CurrentTransformConfig != null)
		{
			CurrentTransformConfig.FolderPartList.Clear();
			UpdateDgvDirParts();
			pnlDirPartSrcIndex.Visible = false;
			pnlDirPartFormat.Visible = false;
		}
	}

	private void txtDirPathFormat_TextChanged(object sender, EventArgs e)
	{
		if (CurrentDirPartItem != null)
		{
			CurrentDirPartItem.Format = txtDirPathFormat.Text;
		}
	}

	private void numDirPartSrcIndex_ValueChanged(object sender, EventArgs e)
	{
		if (CurrentDirPartItem != null)
		{
			CurrentDirPartItem.SourceIndex = (int)numDirPartSrcIndex.Value;
		}
	}

	private void UpdateDgvFileParts()
	{
		if (CurrentTransformConfig == null)
		{
			return;
		}
		dgvFilePartItems.Rows.Clear();
		int num = 0;
		foreach (PartItem fileNamePart in CurrentTransformConfig.FileNamePartList)
		{
			DataGridViewRow dataGridViewRow = new DataGridViewRow();
			dataGridViewRow.Cells.Add(new DataGridViewTextBoxCell
			{
				Value = num
			});
			dataGridViewRow.Cells.Add(new DataGridViewTextBoxCell
			{
				Value = ((fileNamePart.PartType == EPartType.DateTime) ? "PC时间" : "名称")
			});
			dgvFilePartItems.Rows.Add(dataGridViewRow);
			num++;
		}
		dgvFilePartItems.ClearSelection();
		pnlFilePartSrcIndex.Visible = false;
		pnlFilePartFormat.Visible = false;
	}

	private void txtFileSpilter_TextChanged(object sender, EventArgs e)
	{
		if (CurrentTransformConfig != null)
		{
			CurrentTransformConfig.FileNameSpliter = txtFileSpilter.Text;
		}
	}

	private void dgvFilePartItems_SelectionChanged(object sender, EventArgs e)
	{
		PartItem currentFilePartItem = CurrentFilePartItem;
		if (currentFilePartItem != null)
		{
			if (currentFilePartItem.PartType == EPartType.Name)
			{
				pnlFilePartSrcIndex.Visible = true;
				pnlFilePartFormat.Visible = false;
				numFilePartSrcIndex.Value = currentFilePartItem.SourceIndex;
			}
			else
			{
				pnlFilePartSrcIndex.Visible = false;
				pnlFilePartFormat.Visible = true;
				txtFilePartFormat.Text = currentFilePartItem.Format;
			}
		}
	}

	private void btnFilePartAdd_Click(object sender, EventArgs e)
	{
		if (CurrentTransformConfig != null)
		{
			FormAddPart formAddPart = new FormAddPart();
			formAddPart.ShowDialog();
			if (formAddPart.IsOK)
			{
				CurrentTransformConfig.FileNamePartList.Add(new PartItem
				{
					PartType = formAddPart.PartType
				});
				UpdateDgvFileParts();
			}
		}
	}

	private void btnFilePartDel_Click(object sender, EventArgs e)
	{
		if (CurrentTransformConfig != null)
		{
			PartItem currentFilePartItem = CurrentFilePartItem;
			if (currentFilePartItem != null)
			{
				CurrentTransformConfig.FileNamePartList.Remove(currentFilePartItem);
				UpdateDgvFileParts();
			}
		}
	}

	private void btnFilePartClear_Click(object sender, EventArgs e)
	{
		if (CurrentTransformConfig != null)
		{
			CurrentTransformConfig.FileNamePartList.Clear();
			UpdateDgvFileParts();
		}
	}

	private void txtFilePartFormat_TextChanged(object sender, EventArgs e)
	{
		if (CurrentFilePartItem != null)
		{
			CurrentFilePartItem.Format = txtFilePartFormat.Text;
		}
	}

	private void numFilePartSrcIndex_ValueChanged(object sender, EventArgs e)
	{
		if (CurrentFilePartItem != null)
		{
			CurrentFilePartItem.SourceIndex = (int)numFilePartSrcIndex.Value;
		}
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
		this.label1 = new System.Windows.Forms.Label();
		this.numCount = new System.Windows.Forms.NumericUpDown();
		this.tabControl1 = new System.Windows.Forms.TabControl();
		this.tabPage1 = new System.Windows.Forms.TabPage();
		this.numDelay = new System.Windows.Forms.NumericUpDown();
		this.label19 = new System.Windows.Forms.Label();
		this.btnDisplayCreate = new System.Windows.Forms.Button();
		this.tabPage2 = new System.Windows.Forms.TabPage();
		this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
		this.groupBox2 = new System.Windows.Forms.GroupBox();
		this.dgvFilePartItems = new System.Windows.Forms.DataGridView();
		this.dataGridViewTextBoxColumn1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.dataGridViewTextBoxColumn2 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.pnlFilePartFormat = new System.Windows.Forms.Panel();
		this.txtFilePartFormat = new System.Windows.Forms.TextBox();
		this.label16 = new System.Windows.Forms.Label();
		this.pnlFilePartSrcIndex = new System.Windows.Forms.Panel();
		this.numFilePartSrcIndex = new System.Windows.Forms.NumericUpDown();
		this.label17 = new System.Windows.Forms.Label();
		this.panel9 = new System.Windows.Forms.Panel();
		this.btnFilePartClear = new System.Windows.Forms.Button();
		this.btnFilePartDel = new System.Windows.Forms.Button();
		this.btnFilePartAdd = new System.Windows.Forms.Button();
		this.panel10 = new System.Windows.Forms.Panel();
		this.txtFileSpilter = new System.Windows.Forms.TextBox();
		this.label18 = new System.Windows.Forms.Label();
		this.groupBox1 = new System.Windows.Forms.GroupBox();
		this.dgvDirPathItems = new System.Windows.Forms.DataGridView();
		this.ColumnIndex = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.ColumnPartType = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.pnlDirPartFormat = new System.Windows.Forms.Panel();
		this.txtDirPathFormat = new System.Windows.Forms.TextBox();
		this.label15 = new System.Windows.Forms.Label();
		this.pnlDirPartSrcIndex = new System.Windows.Forms.Panel();
		this.numDirPartSrcIndex = new System.Windows.Forms.NumericUpDown();
		this.label14 = new System.Windows.Forms.Label();
		this.panel4 = new System.Windows.Forms.Panel();
		this.btnDirPartClear = new System.Windows.Forms.Button();
		this.btnDirPartDel = new System.Windows.Forms.Button();
		this.btnDirPartAdd = new System.Windows.Forms.Button();
		this.panel3 = new System.Windows.Forms.Panel();
		this.txtFolderSpliter = new System.Windows.Forms.TextBox();
		this.label13 = new System.Windows.Forms.Label();
		this.panel2 = new System.Windows.Forms.Panel();
		this.txtWatchPath = new System.Windows.Forms.TextBox();
		this.label9 = new System.Windows.Forms.Label();
		this.numFileNameIndex = new System.Windows.Forms.NumericUpDown();
		this.btnWatchPathOpen = new System.Windows.Forms.Button();
		this.label12 = new System.Windows.Forms.Label();
		this.label10 = new System.Windows.Forms.Label();
		this.numFolderIndex = new System.Windows.Forms.NumericUpDown();
		this.txtSourceSpliter = new System.Windows.Forms.TextBox();
		this.label11 = new System.Windows.Forms.Label();
		this.panel1 = new System.Windows.Forms.Panel();
		this.btnTransformSave = new System.Windows.Forms.Button();
		this.label4 = new System.Windows.Forms.Label();
		this.btnDeleteTransform = new System.Windows.Forms.Button();
		this.btnAddTransform = new System.Windows.Forms.Button();
		this.cmbxTransformName = new System.Windows.Forms.ComboBox();
		this.tabPage3 = new System.Windows.Forms.TabPage();
		this.btnCleanApply = new System.Windows.Forms.Button();
		this.numCleanInterval = new System.Windows.Forms.NumericUpDown();
		this.label8 = new System.Windows.Forms.Label();
		this.numCleanRemaining = new System.Windows.Forms.NumericUpDown();
		this.rbtnCleanEnableRemaining = new System.Windows.Forms.RadioButton();
		this.label7 = new System.Windows.Forms.Label();
		this.numCleanHoldDay = new System.Windows.Forms.NumericUpDown();
		this.rbtnCleanEnableHoldDay = new System.Windows.Forms.RadioButton();
		this.label6 = new System.Windows.Forms.Label();
		this.ckbxCleanIsEmptyFolder = new System.Windows.Forms.CheckBox();
		this.label5 = new System.Windows.Forms.Label();
		this.numCleanScanInterval = new System.Windows.Forms.NumericUpDown();
		this.label3 = new System.Windows.Forms.Label();
		this.btnCleanPathOpen = new System.Windows.Forms.Button();
		this.txtCleanPath = new System.Windows.Forms.TextBox();
		this.label2 = new System.Windows.Forms.Label();
		this.checkBoxCleanEnable = new System.Windows.Forms.CheckBox();
		((System.ComponentModel.ISupportInitialize)this.numCount).BeginInit();
		this.tabControl1.SuspendLayout();
		this.tabPage1.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.numDelay).BeginInit();
		this.tabPage2.SuspendLayout();
		this.tableLayoutPanel1.SuspendLayout();
		this.groupBox2.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.dgvFilePartItems).BeginInit();
		this.pnlFilePartFormat.SuspendLayout();
		this.pnlFilePartSrcIndex.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.numFilePartSrcIndex).BeginInit();
		this.panel9.SuspendLayout();
		this.panel10.SuspendLayout();
		this.groupBox1.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.dgvDirPathItems).BeginInit();
		this.pnlDirPartFormat.SuspendLayout();
		this.pnlDirPartSrcIndex.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.numDirPartSrcIndex).BeginInit();
		this.panel4.SuspendLayout();
		this.panel3.SuspendLayout();
		this.panel2.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.numFileNameIndex).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numFolderIndex).BeginInit();
		this.panel1.SuspendLayout();
		this.tabPage3.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.numCleanInterval).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numCleanRemaining).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numCleanHoldDay).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numCleanScanInterval).BeginInit();
		base.SuspendLayout();
		this.label1.AutoSize = true;
		this.label1.Location = new System.Drawing.Point(75, 39);
		this.label1.Name = "label1";
		this.label1.Size = new System.Drawing.Size(49, 14);
		this.label1.TabIndex = 2;
		this.label1.Text = "数量：";
		this.numCount.Location = new System.Drawing.Point(130, 37);
		this.numCount.Maximum = new decimal(new int[4] { 16, 0, 0, 0 });
		this.numCount.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.numCount.Name = "numCount";
		this.numCount.Size = new System.Drawing.Size(120, 23);
		this.numCount.TabIndex = 3;
		this.numCount.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.tabControl1.Controls.Add(this.tabPage1);
		this.tabControl1.Controls.Add(this.tabPage2);
		this.tabControl1.Controls.Add(this.tabPage3);
		this.tabControl1.Dock = System.Windows.Forms.DockStyle.Fill;
		this.tabControl1.Font = new System.Drawing.Font("宋体", 10.5f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 134);
		this.tabControl1.Location = new System.Drawing.Point(0, 0);
		this.tabControl1.Name = "tabControl1";
		this.tabControl1.SelectedIndex = 0;
		this.tabControl1.Size = new System.Drawing.Size(886, 656);
		this.tabControl1.TabIndex = 4;
		this.tabPage1.Controls.Add(this.numDelay);
		this.tabPage1.Controls.Add(this.label19);
		this.tabPage1.Controls.Add(this.btnDisplayCreate);
		this.tabPage1.Controls.Add(this.numCount);
		this.tabPage1.Controls.Add(this.label1);
		this.tabPage1.Location = new System.Drawing.Point(4, 24);
		this.tabPage1.Name = "tabPage1";
		this.tabPage1.Padding = new System.Windows.Forms.Padding(3);
		this.tabPage1.Size = new System.Drawing.Size(878, 628);
		this.tabPage1.TabIndex = 0;
		this.tabPage1.Text = "相机显示";
		this.tabPage1.UseVisualStyleBackColor = true;
		this.numDelay.Location = new System.Drawing.Point(130, 99);
		this.numDelay.Maximum = new decimal(new int[4] { 1215752191, 23, 0, 0 });
		this.numDelay.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.numDelay.Name = "numDelay";
		this.numDelay.Size = new System.Drawing.Size(120, 23);
		this.numDelay.TabIndex = 6;
		this.numDelay.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.numDelay.ValueChanged += new System.EventHandler(numDelay_ValueChanged);
		this.label19.AutoSize = true;
		this.label19.Location = new System.Drawing.Point(33, 101);
		this.label19.Name = "label19";
		this.label19.Size = new System.Drawing.Size(84, 14);
		this.label19.TabIndex = 5;
		this.label19.Text = "延时（s）：";
		this.btnDisplayCreate.Location = new System.Drawing.Point(353, 35);
		this.btnDisplayCreate.Name = "btnDisplayCreate";
		this.btnDisplayCreate.Size = new System.Drawing.Size(121, 23);
		this.btnDisplayCreate.TabIndex = 4;
		this.btnDisplayCreate.Text = "保存应用";
		this.btnDisplayCreate.UseVisualStyleBackColor = true;
		this.btnDisplayCreate.Click += new System.EventHandler(btnDisplayCreate_Click);
		this.tabPage2.Controls.Add(this.tableLayoutPanel1);
		this.tabPage2.Controls.Add(this.panel2);
		this.tabPage2.Controls.Add(this.panel1);
		this.tabPage2.Location = new System.Drawing.Point(4, 24);
		this.tabPage2.Name = "tabPage2";
		this.tabPage2.Padding = new System.Windows.Forms.Padding(3);
		this.tabPage2.Size = new System.Drawing.Size(878, 628);
		this.tabPage2.TabIndex = 1;
		this.tabPage2.Text = "文件转存";
		this.tabPage2.UseVisualStyleBackColor = true;
		this.tableLayoutPanel1.ColumnCount = 2;
		this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50f));
		this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50f));
		this.tableLayoutPanel1.Controls.Add(this.groupBox2, 0, 0);
		this.tableLayoutPanel1.Controls.Add(this.groupBox1, 0, 0);
		this.tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
		this.tableLayoutPanel1.Location = new System.Drawing.Point(3, 167);
		this.tableLayoutPanel1.Name = "tableLayoutPanel1";
		this.tableLayoutPanel1.RowCount = 1;
		this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50f));
		this.tableLayoutPanel1.Size = new System.Drawing.Size(872, 458);
		this.tableLayoutPanel1.TabIndex = 13;
		this.groupBox2.Controls.Add(this.dgvFilePartItems);
		this.groupBox2.Controls.Add(this.pnlFilePartFormat);
		this.groupBox2.Controls.Add(this.pnlFilePartSrcIndex);
		this.groupBox2.Controls.Add(this.panel9);
		this.groupBox2.Controls.Add(this.panel10);
		this.groupBox2.Dock = System.Windows.Forms.DockStyle.Fill;
		this.groupBox2.Location = new System.Drawing.Point(439, 3);
		this.groupBox2.Name = "groupBox2";
		this.groupBox2.Size = new System.Drawing.Size(430, 452);
		this.groupBox2.TabIndex = 1;
		this.groupBox2.TabStop = false;
		this.groupBox2.Text = "文件名称";
		this.dgvFilePartItems.AllowUserToAddRows = false;
		this.dgvFilePartItems.AllowUserToDeleteRows = false;
		this.dgvFilePartItems.AllowUserToResizeColumns = false;
		this.dgvFilePartItems.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
		this.dgvFilePartItems.Columns.AddRange(this.dataGridViewTextBoxColumn1, this.dataGridViewTextBoxColumn2);
		this.dgvFilePartItems.Dock = System.Windows.Forms.DockStyle.Fill;
		this.dgvFilePartItems.Location = new System.Drawing.Point(3, 99);
		this.dgvFilePartItems.Name = "dgvFilePartItems";
		this.dgvFilePartItems.RowHeadersVisible = false;
		this.dgvFilePartItems.RowTemplate.Height = 23;
		this.dgvFilePartItems.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
		this.dgvFilePartItems.Size = new System.Drawing.Size(424, 270);
		this.dgvFilePartItems.TabIndex = 14;
		this.dgvFilePartItems.SelectionChanged += new System.EventHandler(dgvFilePartItems_SelectionChanged);
		this.dataGridViewTextBoxColumn1.HeaderText = "序号";
		this.dataGridViewTextBoxColumn1.Name = "dataGridViewTextBoxColumn1";
		this.dataGridViewTextBoxColumn1.ReadOnly = true;
		this.dataGridViewTextBoxColumn2.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
		this.dataGridViewTextBoxColumn2.HeaderText = "类型";
		this.dataGridViewTextBoxColumn2.Name = "dataGridViewTextBoxColumn2";
		this.dataGridViewTextBoxColumn2.ReadOnly = true;
		this.pnlFilePartFormat.Controls.Add(this.txtFilePartFormat);
		this.pnlFilePartFormat.Controls.Add(this.label16);
		this.pnlFilePartFormat.Dock = System.Windows.Forms.DockStyle.Bottom;
		this.pnlFilePartFormat.Location = new System.Drawing.Point(3, 369);
		this.pnlFilePartFormat.Name = "pnlFilePartFormat";
		this.pnlFilePartFormat.Size = new System.Drawing.Size(424, 40);
		this.pnlFilePartFormat.TabIndex = 13;
		this.pnlFilePartFormat.Visible = false;
		this.txtFilePartFormat.Location = new System.Drawing.Point(117, 9);
		this.txtFilePartFormat.Name = "txtFilePartFormat";
		this.txtFilePartFormat.Size = new System.Drawing.Size(151, 23);
		this.txtFilePartFormat.TabIndex = 11;
		this.txtFilePartFormat.TextChanged += new System.EventHandler(txtFilePartFormat_TextChanged);
		this.label16.AutoSize = true;
		this.label16.Location = new System.Drawing.Point(43, 12);
		this.label16.Name = "label16";
		this.label16.Size = new System.Drawing.Size(49, 14);
		this.label16.TabIndex = 10;
		this.label16.Text = "格式化";
		this.pnlFilePartSrcIndex.Controls.Add(this.numFilePartSrcIndex);
		this.pnlFilePartSrcIndex.Controls.Add(this.label17);
		this.pnlFilePartSrcIndex.Dock = System.Windows.Forms.DockStyle.Bottom;
		this.pnlFilePartSrcIndex.Location = new System.Drawing.Point(3, 409);
		this.pnlFilePartSrcIndex.Name = "pnlFilePartSrcIndex";
		this.pnlFilePartSrcIndex.Size = new System.Drawing.Size(424, 40);
		this.pnlFilePartSrcIndex.TabIndex = 12;
		this.pnlFilePartSrcIndex.Visible = false;
		this.numFilePartSrcIndex.Location = new System.Drawing.Point(117, 10);
		this.numFilePartSrcIndex.Name = "numFilePartSrcIndex";
		this.numFilePartSrcIndex.Size = new System.Drawing.Size(79, 23);
		this.numFilePartSrcIndex.TabIndex = 11;
		this.numFilePartSrcIndex.ValueChanged += new System.EventHandler(numFilePartSrcIndex_ValueChanged);
		this.label17.AutoSize = true;
		this.label17.Location = new System.Drawing.Point(21, 12);
		this.label17.Name = "label17";
		this.label17.Size = new System.Drawing.Size(77, 14);
		this.label17.TabIndex = 10;
		this.label17.Text = "原路径序号";
		this.panel9.Controls.Add(this.btnFilePartClear);
		this.panel9.Controls.Add(this.btnFilePartDel);
		this.panel9.Controls.Add(this.btnFilePartAdd);
		this.panel9.Dock = System.Windows.Forms.DockStyle.Top;
		this.panel9.Location = new System.Drawing.Point(3, 59);
		this.panel9.Name = "panel9";
		this.panel9.Size = new System.Drawing.Size(424, 40);
		this.panel9.TabIndex = 11;
		this.btnFilePartClear.Location = new System.Drawing.Point(216, 7);
		this.btnFilePartClear.Name = "btnFilePartClear";
		this.btnFilePartClear.Size = new System.Drawing.Size(75, 23);
		this.btnFilePartClear.TabIndex = 14;
		this.btnFilePartClear.Text = "清空";
		this.btnFilePartClear.UseVisualStyleBackColor = true;
		this.btnFilePartClear.Click += new System.EventHandler(btnFilePartClear_Click);
		this.btnFilePartDel.Location = new System.Drawing.Point(117, 7);
		this.btnFilePartDel.Name = "btnFilePartDel";
		this.btnFilePartDel.Size = new System.Drawing.Size(75, 23);
		this.btnFilePartDel.TabIndex = 13;
		this.btnFilePartDel.Text = "删除";
		this.btnFilePartDel.UseVisualStyleBackColor = true;
		this.btnFilePartDel.Click += new System.EventHandler(btnFilePartDel_Click);
		this.btnFilePartAdd.Location = new System.Drawing.Point(18, 7);
		this.btnFilePartAdd.Name = "btnFilePartAdd";
		this.btnFilePartAdd.Size = new System.Drawing.Size(75, 23);
		this.btnFilePartAdd.TabIndex = 12;
		this.btnFilePartAdd.Text = "添加";
		this.btnFilePartAdd.UseVisualStyleBackColor = true;
		this.btnFilePartAdd.Click += new System.EventHandler(btnFilePartAdd_Click);
		this.panel10.Controls.Add(this.txtFileSpilter);
		this.panel10.Controls.Add(this.label18);
		this.panel10.Dock = System.Windows.Forms.DockStyle.Top;
		this.panel10.Location = new System.Drawing.Point(3, 19);
		this.panel10.Name = "panel10";
		this.panel10.Size = new System.Drawing.Size(424, 40);
		this.panel10.TabIndex = 10;
		this.txtFileSpilter.Location = new System.Drawing.Point(112, 9);
		this.txtFileSpilter.Name = "txtFileSpilter";
		this.txtFileSpilter.Size = new System.Drawing.Size(80, 23);
		this.txtFileSpilter.TabIndex = 9;
		this.txtFileSpilter.TextChanged += new System.EventHandler(txtFileSpilter_TextChanged);
		this.label18.AutoSize = true;
		this.label18.Location = new System.Drawing.Point(15, 12);
		this.label18.Name = "label18";
		this.label18.Size = new System.Drawing.Size(91, 14);
		this.label18.TabIndex = 8;
		this.label18.Text = "文件名分隔符";
		this.groupBox1.Controls.Add(this.dgvDirPathItems);
		this.groupBox1.Controls.Add(this.pnlDirPartFormat);
		this.groupBox1.Controls.Add(this.pnlDirPartSrcIndex);
		this.groupBox1.Controls.Add(this.panel4);
		this.groupBox1.Controls.Add(this.panel3);
		this.groupBox1.Dock = System.Windows.Forms.DockStyle.Fill;
		this.groupBox1.Location = new System.Drawing.Point(3, 3);
		this.groupBox1.Name = "groupBox1";
		this.groupBox1.Size = new System.Drawing.Size(430, 452);
		this.groupBox1.TabIndex = 0;
		this.groupBox1.TabStop = false;
		this.groupBox1.Text = "文件夹";
		this.dgvDirPathItems.AllowUserToAddRows = false;
		this.dgvDirPathItems.AllowUserToDeleteRows = false;
		this.dgvDirPathItems.AllowUserToResizeColumns = false;
		this.dgvDirPathItems.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
		this.dgvDirPathItems.Columns.AddRange(this.ColumnIndex, this.ColumnPartType);
		this.dgvDirPathItems.Dock = System.Windows.Forms.DockStyle.Fill;
		this.dgvDirPathItems.Location = new System.Drawing.Point(3, 99);
		this.dgvDirPathItems.Name = "dgvDirPathItems";
		this.dgvDirPathItems.RowHeadersVisible = false;
		this.dgvDirPathItems.RowTemplate.Height = 23;
		this.dgvDirPathItems.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
		this.dgvDirPathItems.Size = new System.Drawing.Size(424, 270);
		this.dgvDirPathItems.TabIndex = 14;
		this.dgvDirPathItems.SelectionChanged += new System.EventHandler(dgvDirPathItems_SelectionChanged);
		this.ColumnIndex.HeaderText = "序号";
		this.ColumnIndex.Name = "ColumnIndex";
		this.ColumnIndex.ReadOnly = true;
		this.ColumnIndex.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
		this.ColumnPartType.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
		this.ColumnPartType.HeaderText = "类型";
		this.ColumnPartType.Name = "ColumnPartType";
		this.ColumnPartType.ReadOnly = true;
		this.ColumnPartType.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
		this.pnlDirPartFormat.Controls.Add(this.txtDirPathFormat);
		this.pnlDirPartFormat.Controls.Add(this.label15);
		this.pnlDirPartFormat.Dock = System.Windows.Forms.DockStyle.Bottom;
		this.pnlDirPartFormat.Location = new System.Drawing.Point(3, 369);
		this.pnlDirPartFormat.Name = "pnlDirPartFormat";
		this.pnlDirPartFormat.Size = new System.Drawing.Size(424, 40);
		this.pnlDirPartFormat.TabIndex = 13;
		this.pnlDirPartFormat.Visible = false;
		this.txtDirPathFormat.Location = new System.Drawing.Point(117, 9);
		this.txtDirPathFormat.Name = "txtDirPathFormat";
		this.txtDirPathFormat.Size = new System.Drawing.Size(151, 23);
		this.txtDirPathFormat.TabIndex = 11;
		this.txtDirPathFormat.TextChanged += new System.EventHandler(txtDirPathFormat_TextChanged);
		this.label15.AutoSize = true;
		this.label15.Location = new System.Drawing.Point(43, 12);
		this.label15.Name = "label15";
		this.label15.Size = new System.Drawing.Size(49, 14);
		this.label15.TabIndex = 10;
		this.label15.Text = "格式化";
		this.pnlDirPartSrcIndex.Controls.Add(this.numDirPartSrcIndex);
		this.pnlDirPartSrcIndex.Controls.Add(this.label14);
		this.pnlDirPartSrcIndex.Dock = System.Windows.Forms.DockStyle.Bottom;
		this.pnlDirPartSrcIndex.Location = new System.Drawing.Point(3, 409);
		this.pnlDirPartSrcIndex.Name = "pnlDirPartSrcIndex";
		this.pnlDirPartSrcIndex.Size = new System.Drawing.Size(424, 40);
		this.pnlDirPartSrcIndex.TabIndex = 12;
		this.pnlDirPartSrcIndex.Visible = false;
		this.numDirPartSrcIndex.Location = new System.Drawing.Point(117, 10);
		this.numDirPartSrcIndex.Name = "numDirPartSrcIndex";
		this.numDirPartSrcIndex.Size = new System.Drawing.Size(79, 23);
		this.numDirPartSrcIndex.TabIndex = 11;
		this.numDirPartSrcIndex.ValueChanged += new System.EventHandler(numDirPartSrcIndex_ValueChanged);
		this.label14.AutoSize = true;
		this.label14.Location = new System.Drawing.Point(21, 12);
		this.label14.Name = "label14";
		this.label14.Size = new System.Drawing.Size(77, 14);
		this.label14.TabIndex = 10;
		this.label14.Text = "原路径序号";
		this.panel4.Controls.Add(this.btnDirPartClear);
		this.panel4.Controls.Add(this.btnDirPartDel);
		this.panel4.Controls.Add(this.btnDirPartAdd);
		this.panel4.Dock = System.Windows.Forms.DockStyle.Top;
		this.panel4.Location = new System.Drawing.Point(3, 59);
		this.panel4.Name = "panel4";
		this.panel4.Size = new System.Drawing.Size(424, 40);
		this.panel4.TabIndex = 11;
		this.btnDirPartClear.Location = new System.Drawing.Point(216, 7);
		this.btnDirPartClear.Name = "btnDirPartClear";
		this.btnDirPartClear.Size = new System.Drawing.Size(75, 23);
		this.btnDirPartClear.TabIndex = 14;
		this.btnDirPartClear.Text = "清空";
		this.btnDirPartClear.UseVisualStyleBackColor = true;
		this.btnDirPartClear.Click += new System.EventHandler(btnDirPartClear_Click);
		this.btnDirPartDel.Location = new System.Drawing.Point(117, 7);
		this.btnDirPartDel.Name = "btnDirPartDel";
		this.btnDirPartDel.Size = new System.Drawing.Size(75, 23);
		this.btnDirPartDel.TabIndex = 13;
		this.btnDirPartDel.Text = "删除";
		this.btnDirPartDel.UseVisualStyleBackColor = true;
		this.btnDirPartDel.Click += new System.EventHandler(btnDirPartDel_Click);
		this.btnDirPartAdd.Location = new System.Drawing.Point(18, 7);
		this.btnDirPartAdd.Name = "btnDirPartAdd";
		this.btnDirPartAdd.Size = new System.Drawing.Size(75, 23);
		this.btnDirPartAdd.TabIndex = 12;
		this.btnDirPartAdd.Text = "添加";
		this.btnDirPartAdd.UseVisualStyleBackColor = true;
		this.btnDirPartAdd.Click += new System.EventHandler(btnDirPartAdd_Click);
		this.panel3.Controls.Add(this.txtFolderSpliter);
		this.panel3.Controls.Add(this.label13);
		this.panel3.Dock = System.Windows.Forms.DockStyle.Top;
		this.panel3.Location = new System.Drawing.Point(3, 19);
		this.panel3.Name = "panel3";
		this.panel3.Size = new System.Drawing.Size(424, 40);
		this.panel3.TabIndex = 10;
		this.txtFolderSpliter.Location = new System.Drawing.Point(98, 9);
		this.txtFolderSpliter.Name = "txtFolderSpliter";
		this.txtFolderSpliter.Size = new System.Drawing.Size(80, 23);
		this.txtFolderSpliter.TabIndex = 9;
		this.txtFolderSpliter.TextChanged += new System.EventHandler(txtFolderSpliter_TextChanged);
		this.label13.AutoSize = true;
		this.label13.Location = new System.Drawing.Point(15, 12);
		this.label13.Name = "label13";
		this.label13.Size = new System.Drawing.Size(77, 14);
		this.label13.TabIndex = 8;
		this.label13.Text = "路径分隔符";
		this.panel2.Controls.Add(this.txtWatchPath);
		this.panel2.Controls.Add(this.label9);
		this.panel2.Controls.Add(this.numFileNameIndex);
		this.panel2.Controls.Add(this.btnWatchPathOpen);
		this.panel2.Controls.Add(this.label12);
		this.panel2.Controls.Add(this.label10);
		this.panel2.Controls.Add(this.numFolderIndex);
		this.panel2.Controls.Add(this.txtSourceSpliter);
		this.panel2.Controls.Add(this.label11);
		this.panel2.Dock = System.Windows.Forms.DockStyle.Top;
		this.panel2.Location = new System.Drawing.Point(3, 55);
		this.panel2.Name = "panel2";
		this.panel2.Size = new System.Drawing.Size(872, 112);
		this.panel2.TabIndex = 12;
		this.txtWatchPath.Location = new System.Drawing.Point(58, 28);
		this.txtWatchPath.Name = "txtWatchPath";
		this.txtWatchPath.Size = new System.Drawing.Size(649, 23);
		this.txtWatchPath.TabIndex = 4;
		this.txtWatchPath.TextChanged += new System.EventHandler(txtWatchPath_TextChanged);
		this.label9.AutoSize = true;
		this.label9.Location = new System.Drawing.Point(17, 32);
		this.label9.Name = "label9";
		this.label9.Size = new System.Drawing.Size(35, 14);
		this.label9.TabIndex = 3;
		this.label9.Text = "路径";
		this.numFileNameIndex.Location = new System.Drawing.Point(582, 71);
		this.numFileNameIndex.Name = "numFileNameIndex";
		this.numFileNameIndex.Size = new System.Drawing.Size(79, 23);
		this.numFileNameIndex.TabIndex = 11;
		this.numFileNameIndex.ValueChanged += new System.EventHandler(numFileNameIndex_ValueChanged);
		this.btnWatchPathOpen.Location = new System.Drawing.Point(726, 28);
		this.btnWatchPathOpen.Name = "btnWatchPathOpen";
		this.btnWatchPathOpen.Size = new System.Drawing.Size(75, 23);
		this.btnWatchPathOpen.TabIndex = 5;
		this.btnWatchPathOpen.Text = "打开";
		this.btnWatchPathOpen.UseVisualStyleBackColor = true;
		this.btnWatchPathOpen.Click += new System.EventHandler(btnWatchPathOpen_Click);
		this.label12.AutoSize = true;
		this.label12.Location = new System.Drawing.Point(471, 73);
		this.label12.Name = "label12";
		this.label12.Size = new System.Drawing.Size(105, 14);
		this.label12.TabIndex = 10;
		this.label12.Text = "文件名内容序号";
		this.label10.AutoSize = true;
		this.label10.Location = new System.Drawing.Point(55, 73);
		this.label10.Name = "label10";
		this.label10.Size = new System.Drawing.Size(77, 14);
		this.label10.TabIndex = 6;
		this.label10.Text = "内容分隔符";
		this.numFolderIndex.Location = new System.Drawing.Point(358, 71);
		this.numFolderIndex.Name = "numFolderIndex";
		this.numFolderIndex.Size = new System.Drawing.Size(79, 23);
		this.numFolderIndex.TabIndex = 9;
		this.numFolderIndex.ValueChanged += new System.EventHandler(numFolderIndex_ValueChanged);
		this.txtSourceSpliter.Location = new System.Drawing.Point(138, 70);
		this.txtSourceSpliter.Name = "txtSourceSpliter";
		this.txtSourceSpliter.Size = new System.Drawing.Size(80, 23);
		this.txtSourceSpliter.TabIndex = 7;
		this.txtSourceSpliter.TextChanged += new System.EventHandler(txtSourceSpliter_TextChanged);
		this.label11.AutoSize = true;
		this.label11.Location = new System.Drawing.Point(247, 73);
		this.label11.Name = "label11";
		this.label11.Size = new System.Drawing.Size(105, 14);
		this.label11.TabIndex = 8;
		this.label11.Text = "文件夹内容序号";
		this.panel1.Controls.Add(this.btnTransformSave);
		this.panel1.Controls.Add(this.label4);
		this.panel1.Controls.Add(this.btnDeleteTransform);
		this.panel1.Controls.Add(this.btnAddTransform);
		this.panel1.Controls.Add(this.cmbxTransformName);
		this.panel1.Dock = System.Windows.Forms.DockStyle.Top;
		this.panel1.Location = new System.Drawing.Point(3, 3);
		this.panel1.Name = "panel1";
		this.panel1.Size = new System.Drawing.Size(872, 52);
		this.panel1.TabIndex = 0;
		this.btnTransformSave.Location = new System.Drawing.Point(678, 8);
		this.btnTransformSave.Name = "btnTransformSave";
		this.btnTransformSave.Size = new System.Drawing.Size(179, 34);
		this.btnTransformSave.TabIndex = 4;
		this.btnTransformSave.Text = "保存应用";
		this.btnTransformSave.UseVisualStyleBackColor = true;
		this.btnTransformSave.Click += new System.EventHandler(btnTransformSave_Click);
		this.label4.AutoSize = true;
		this.label4.Location = new System.Drawing.Point(20, 18);
		this.label4.Name = "label4";
		this.label4.Size = new System.Drawing.Size(35, 14);
		this.label4.TabIndex = 3;
		this.label4.Text = "名称";
		this.btnDeleteTransform.Location = new System.Drawing.Point(458, 14);
		this.btnDeleteTransform.Name = "btnDeleteTransform";
		this.btnDeleteTransform.Size = new System.Drawing.Size(75, 23);
		this.btnDeleteTransform.TabIndex = 2;
		this.btnDeleteTransform.Text = "删除";
		this.btnDeleteTransform.UseVisualStyleBackColor = true;
		this.btnDeleteTransform.Click += new System.EventHandler(btnDeleteTransform_Click);
		this.btnAddTransform.Location = new System.Drawing.Point(365, 14);
		this.btnAddTransform.Name = "btnAddTransform";
		this.btnAddTransform.Size = new System.Drawing.Size(75, 23);
		this.btnAddTransform.TabIndex = 1;
		this.btnAddTransform.Text = "添加";
		this.btnAddTransform.UseVisualStyleBackColor = true;
		this.btnAddTransform.Click += new System.EventHandler(btnAddTransform_Click);
		this.cmbxTransformName.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.cmbxTransformName.FormattingEnabled = true;
		this.cmbxTransformName.Location = new System.Drawing.Point(61, 15);
		this.cmbxTransformName.Name = "cmbxTransformName";
		this.cmbxTransformName.Size = new System.Drawing.Size(279, 22);
		this.cmbxTransformName.TabIndex = 0;
		this.cmbxTransformName.SelectedIndexChanged += new System.EventHandler(cmbxTransformName_SelectedIndexChanged);
		this.tabPage3.Controls.Add(this.checkBoxCleanEnable);
		this.tabPage3.Controls.Add(this.btnCleanApply);
		this.tabPage3.Controls.Add(this.numCleanInterval);
		this.tabPage3.Controls.Add(this.label8);
		this.tabPage3.Controls.Add(this.numCleanRemaining);
		this.tabPage3.Controls.Add(this.rbtnCleanEnableRemaining);
		this.tabPage3.Controls.Add(this.label7);
		this.tabPage3.Controls.Add(this.numCleanHoldDay);
		this.tabPage3.Controls.Add(this.rbtnCleanEnableHoldDay);
		this.tabPage3.Controls.Add(this.label6);
		this.tabPage3.Controls.Add(this.ckbxCleanIsEmptyFolder);
		this.tabPage3.Controls.Add(this.label5);
		this.tabPage3.Controls.Add(this.numCleanScanInterval);
		this.tabPage3.Controls.Add(this.label3);
		this.tabPage3.Controls.Add(this.btnCleanPathOpen);
		this.tabPage3.Controls.Add(this.txtCleanPath);
		this.tabPage3.Controls.Add(this.label2);
		this.tabPage3.Location = new System.Drawing.Point(4, 24);
		this.tabPage3.Name = "tabPage3";
		this.tabPage3.Padding = new System.Windows.Forms.Padding(3);
		this.tabPage3.Size = new System.Drawing.Size(878, 628);
		this.tabPage3.TabIndex = 2;
		this.tabPage3.Text = "文件清理";
		this.tabPage3.UseVisualStyleBackColor = true;
		this.btnCleanApply.Location = new System.Drawing.Point(226, 463);
		this.btnCleanApply.Name = "btnCleanApply";
		this.btnCleanApply.Size = new System.Drawing.Size(375, 57);
		this.btnCleanApply.TabIndex = 15;
		this.btnCleanApply.Text = "保存应用";
		this.btnCleanApply.UseVisualStyleBackColor = true;
		this.btnCleanApply.Click += new System.EventHandler(btnCleanApply_Click);
		this.numCleanInterval.Location = new System.Drawing.Point(192, 379);
		this.numCleanInterval.Maximum = new decimal(new int[4] { 9999, 0, 0, 0 });
		this.numCleanInterval.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.numCleanInterval.Name = "numCleanInterval";
		this.numCleanInterval.Size = new System.Drawing.Size(140, 23);
		this.numCleanInterval.TabIndex = 14;
		this.numCleanInterval.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.numCleanInterval.ValueChanged += new System.EventHandler(numCleanInterval_ValueChanged);
		this.label8.AutoSize = true;
		this.label8.Location = new System.Drawing.Point(38, 381);
		this.label8.Name = "label8";
		this.label8.Size = new System.Drawing.Size(133, 14);
		this.label8.TabIndex = 13;
		this.label8.Text = "删除文件间隔（ms）";
		this.numCleanRemaining.Location = new System.Drawing.Point(251, 316);
		this.numCleanRemaining.Maximum = new decimal(new int[4] { 9999, 0, 0, 0 });
		this.numCleanRemaining.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.numCleanRemaining.Name = "numCleanRemaining";
		this.numCleanRemaining.Size = new System.Drawing.Size(140, 23);
		this.numCleanRemaining.TabIndex = 12;
		this.numCleanRemaining.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.numCleanRemaining.ValueChanged += new System.EventHandler(numCleanRemaining_ValueChanged);
		this.rbtnCleanEnableRemaining.AutoSize = true;
		this.rbtnCleanEnableRemaining.Location = new System.Drawing.Point(192, 316);
		this.rbtnCleanEnableRemaining.Name = "rbtnCleanEnableRemaining";
		this.rbtnCleanEnableRemaining.Size = new System.Drawing.Size(53, 18);
		this.rbtnCleanEnableRemaining.TabIndex = 11;
		this.rbtnCleanEnableRemaining.TabStop = true;
		this.rbtnCleanEnableRemaining.Text = "启用";
		this.rbtnCleanEnableRemaining.UseVisualStyleBackColor = true;
		this.rbtnCleanEnableRemaining.CheckedChanged += new System.EventHandler(rbtnCleanEnableRemaining_CheckedChanged);
		this.label7.AutoSize = true;
		this.label7.Location = new System.Drawing.Point(38, 318);
		this.label7.Name = "label7";
		this.label7.Size = new System.Drawing.Size(133, 14);
		this.label7.TabIndex = 10;
		this.label7.Text = "磁盘剩余空间（GB）";
		this.numCleanHoldDay.Location = new System.Drawing.Point(251, 253);
		this.numCleanHoldDay.Maximum = new decimal(new int[4] { 9999, 0, 0, 0 });
		this.numCleanHoldDay.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.numCleanHoldDay.Name = "numCleanHoldDay";
		this.numCleanHoldDay.Size = new System.Drawing.Size(140, 23);
		this.numCleanHoldDay.TabIndex = 9;
		this.numCleanHoldDay.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.numCleanHoldDay.ValueChanged += new System.EventHandler(numCleanHoldDay_ValueChanged);
		this.rbtnCleanEnableHoldDay.AutoSize = true;
		this.rbtnCleanEnableHoldDay.Location = new System.Drawing.Point(192, 253);
		this.rbtnCleanEnableHoldDay.Name = "rbtnCleanEnableHoldDay";
		this.rbtnCleanEnableHoldDay.Size = new System.Drawing.Size(53, 18);
		this.rbtnCleanEnableHoldDay.TabIndex = 8;
		this.rbtnCleanEnableHoldDay.TabStop = true;
		this.rbtnCleanEnableHoldDay.Text = "启用";
		this.rbtnCleanEnableHoldDay.UseVisualStyleBackColor = true;
		this.rbtnCleanEnableHoldDay.CheckedChanged += new System.EventHandler(rbtnCleanEnableHoldDay_CheckedChanged);
		this.label6.AutoSize = true;
		this.label6.Location = new System.Drawing.Point(80, 255);
		this.label6.Name = "label6";
		this.label6.Size = new System.Drawing.Size(91, 14);
		this.label6.TabIndex = 7;
		this.label6.Text = "文件保留天数";
		this.ckbxCleanIsEmptyFolder.AutoSize = true;
		this.ckbxCleanIsEmptyFolder.Location = new System.Drawing.Point(192, 191);
		this.ckbxCleanIsEmptyFolder.Name = "ckbxCleanIsEmptyFolder";
		this.ckbxCleanIsEmptyFolder.Size = new System.Drawing.Size(40, 18);
		this.ckbxCleanIsEmptyFolder.TabIndex = 6;
		this.ckbxCleanIsEmptyFolder.Text = "是";
		this.ckbxCleanIsEmptyFolder.UseVisualStyleBackColor = true;
		this.ckbxCleanIsEmptyFolder.CheckedChanged += new System.EventHandler(ckbxCleanIsEmptyFolder_CheckedChanged);
		this.label5.AutoSize = true;
		this.label5.Location = new System.Drawing.Point(80, 192);
		this.label5.Name = "label5";
		this.label5.Size = new System.Drawing.Size(91, 14);
		this.label5.TabIndex = 5;
		this.label5.Text = "清理空文件夹";
		this.numCleanScanInterval.DecimalPlaces = 2;
		this.numCleanScanInterval.Location = new System.Drawing.Point(192, 127);
		this.numCleanScanInterval.Minimum = new decimal(new int[4] { 1, 0, 0, 131072 });
		this.numCleanScanInterval.Name = "numCleanScanInterval";
		this.numCleanScanInterval.Size = new System.Drawing.Size(140, 23);
		this.numCleanScanInterval.TabIndex = 4;
		this.numCleanScanInterval.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.numCleanScanInterval.ValueChanged += new System.EventHandler(numCleanScanInterval_ValueChanged);
		this.label3.AutoSize = true;
		this.label3.Location = new System.Drawing.Point(52, 129);
		this.label3.Name = "label3";
		this.label3.Size = new System.Drawing.Size(119, 14);
		this.label3.TabIndex = 3;
		this.label3.Text = "扫描间隔（小时）";
		this.btnCleanPathOpen.Location = new System.Drawing.Point(700, 63);
		this.btnCleanPathOpen.Name = "btnCleanPathOpen";
		this.btnCleanPathOpen.Size = new System.Drawing.Size(75, 23);
		this.btnCleanPathOpen.TabIndex = 2;
		this.btnCleanPathOpen.Text = "打开";
		this.btnCleanPathOpen.UseVisualStyleBackColor = true;
		this.btnCleanPathOpen.Click += new System.EventHandler(btnCleanPathOpen_Click);
		this.txtCleanPath.Location = new System.Drawing.Point(192, 63);
		this.txtCleanPath.Name = "txtCleanPath";
		this.txtCleanPath.Size = new System.Drawing.Size(482, 23);
		this.txtCleanPath.TabIndex = 1;
		this.txtCleanPath.TextChanged += new System.EventHandler(txtCleanPath_TextChanged);
		this.label2.AutoSize = true;
		this.label2.Location = new System.Drawing.Point(136, 66);
		this.label2.Name = "label2";
		this.label2.Size = new System.Drawing.Size(35, 14);
		this.label2.TabIndex = 0;
		this.label2.Text = "路径";
		this.checkBoxCleanEnable.AutoSize = true;
		this.checkBoxCleanEnable.Location = new System.Drawing.Point(192, 20);
		this.checkBoxCleanEnable.Name = "checkBoxCleanEnable";
		this.checkBoxCleanEnable.Size = new System.Drawing.Size(54, 18);
		this.checkBoxCleanEnable.TabIndex = 16;
		this.checkBoxCleanEnable.Text = "启用";
		this.checkBoxCleanEnable.UseVisualStyleBackColor = true;
		this.checkBoxCleanEnable.CheckedChanged += new System.EventHandler(checkBoxCleanEnable_CheckedChanged);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
		base.ClientSize = new System.Drawing.Size(886, 656);
		base.Controls.Add(this.tabControl1);
		base.FormBorderStyle = System.Windows.Forms.FormBorderStyle.SizableToolWindow;
		base.Name = "FormSetting";
		base.ShowInTaskbar = false;
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
		this.Text = "设置";
		base.Load += new System.EventHandler(FormSysSetting_Load);
		((System.ComponentModel.ISupportInitialize)this.numCount).EndInit();
		this.tabControl1.ResumeLayout(false);
		this.tabPage1.ResumeLayout(false);
		this.tabPage1.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.numDelay).EndInit();
		this.tabPage2.ResumeLayout(false);
		this.tableLayoutPanel1.ResumeLayout(false);
		this.groupBox2.ResumeLayout(false);
		((System.ComponentModel.ISupportInitialize)this.dgvFilePartItems).EndInit();
		this.pnlFilePartFormat.ResumeLayout(false);
		this.pnlFilePartFormat.PerformLayout();
		this.pnlFilePartSrcIndex.ResumeLayout(false);
		this.pnlFilePartSrcIndex.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.numFilePartSrcIndex).EndInit();
		this.panel9.ResumeLayout(false);
		this.panel10.ResumeLayout(false);
		this.panel10.PerformLayout();
		this.groupBox1.ResumeLayout(false);
		((System.ComponentModel.ISupportInitialize)this.dgvDirPathItems).EndInit();
		this.pnlDirPartFormat.ResumeLayout(false);
		this.pnlDirPartFormat.PerformLayout();
		this.pnlDirPartSrcIndex.ResumeLayout(false);
		this.pnlDirPartSrcIndex.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.numDirPartSrcIndex).EndInit();
		this.panel4.ResumeLayout(false);
		this.panel3.ResumeLayout(false);
		this.panel3.PerformLayout();
		this.panel2.ResumeLayout(false);
		this.panel2.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.numFileNameIndex).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numFolderIndex).EndInit();
		this.panel1.ResumeLayout(false);
		this.panel1.PerformLayout();
		this.tabPage3.ResumeLayout(false);
		this.tabPage3.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.numCleanInterval).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numCleanRemaining).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numCleanHoldDay).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numCleanScanInterval).EndInit();
		base.ResumeLayout(false);
	}
}
