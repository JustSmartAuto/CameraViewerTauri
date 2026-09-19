using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CameraHelper;

public class FrmHistory : Form
{
	private IContainer components;

	private SplitContainer splitContainer1;

	private PictureBox picMain;

	private DataGridView dgvFileNames;

	private Panel panel1;

	private AntdUI.Input txtPath;

	private Label lblPath;

	private AntdUI.Button btnOpenPath;

	private AntdUI.Input txtSearch;

	private AntdUI.Button btnSearch;

	private AntdUI.Button btnRefresh;

	public FrmHistory(string path)
	{
		InitializeComponent();
		ThemeManager.ThemeChanged += ApplyTheme;
		ApplyTheme();
		bool isChinese = ProjectMgr.Inst.SysConfig.Language == 0;
		Text = (isChinese ? "图片回看" : "Image Review");
		lblPath.Text = (isChinese ? "路径：" : "Path: ");
		btnOpenPath.Text = (isChinese ? "打开" : "Open");
		btnRefresh.Text = (isChinese ? "刷新" : "Refresh");
		btnSearch.Text = (isChinese ? "筛选" : "Filter");
		dgvFileNames.Columns.Add(new DataGridViewTextBoxColumn
		{
			HeaderText = (isChinese ? "序号" : "Index"),
			Width = 80
		});
		dgvFileNames.Columns.Add(new DataGridViewTextBoxColumn
		{
			HeaderText = (isChinese ? "文件名" : "File Name"),
			AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
		});
		try
		{
			txtPath.Text = Path.GetDirectoryName(path);
		}
		catch
		{
		}
		UpdateDgv(GetFiles());
	}

	private void ApplyTheme()
	{
		ForeColor = ThemeManager.Fg;
		BackColor = ThemeManager.Bg2;
		panel1.BackColor = ThemeManager.Bg2;
		splitContainer1.BackColor = ThemeManager.Bg2;
		ThemeManager.StyleGrid(dgvFileNames);
		ThemeManager.StyleButtons(this);
	}

	private void UpdateDgv(string[] fileNames)
	{
		dgvFileNames.Rows.Clear();
		int index = 0;
		foreach (string item in fileNames)
		{
			DataGridViewRow row = new DataGridViewRow();
			row.Cells.Add(new DataGridViewTextBoxCell
			{
				Value = index
			});
			row.Cells.Add(new DataGridViewTextBoxCell
			{
				Value = item
			});
			dgvFileNames.Rows.Add(row);
			index++;
		}
	}

	private string[] GetFiles()
	{
		if (string.IsNullOrEmpty(txtPath.Text) || !Directory.Exists(txtPath.Text))
		{
			return new string[0];
		}
		SearchOption searchOption = SearchOption.AllDirectories;
		string pattern = "*";
		return Directory.GetFiles(txtPath.Text, pattern, searchOption);
	}

	private void dgvFileNames_SelectionChanged(object sender, EventArgs e)
	{
		try
		{
			if (dgvFileNames.SelectedRows != null && dgvFileNames.SelectedRows.Count > 0)
			{
				string fileName = dgvFileNames.SelectedRows[0].Cells[1].Value.ToString();
				picMain.Image = new Bitmap(fileName);
			}
		}
		catch
		{
		}
	}

	private void btnSearch_Click(object sender, EventArgs e)
	{
		string key = txtSearch.Text;
		IEnumerable<string> arr = from t in GetFiles()
			where t.Contains(key)
			select t;
		UpdateDgv(arr.ToArray());
	}

	private void btnRefresh_Click(object sender, EventArgs e)
	{
		UpdateDgv(GetFiles());
	}

	private void btnOpenPath_Click(object sender, EventArgs e)
	{
		FolderBrowserDialog dlg = new FolderBrowserDialog();
		if (dlg.ShowDialog() == DialogResult.OK)
		{
			txtPath.Text = dlg.SelectedPath;
			string key = txtSearch.Text;
			IEnumerable<string> arr = from t in GetFiles()
				where t.Contains(key)
				select t;
			UpdateDgv(arr.ToArray());
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
		this.splitContainer1 = new System.Windows.Forms.SplitContainer();
		this.dgvFileNames = new System.Windows.Forms.DataGridView();
		this.picMain = new System.Windows.Forms.PictureBox();
		this.panel1 = new System.Windows.Forms.Panel();
		this.txtPath = new AntdUI.Input();
		this.lblPath = new System.Windows.Forms.Label();
		this.btnOpenPath = new AntdUI.Button();
		this.txtSearch = new AntdUI.Input();
		this.btnSearch = new AntdUI.Button();
		this.btnRefresh = new AntdUI.Button();
		((System.ComponentModel.ISupportInitialize)this.splitContainer1).BeginInit();
		this.splitContainer1.Panel1.SuspendLayout();
		this.splitContainer1.Panel2.SuspendLayout();
		this.splitContainer1.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.dgvFileNames).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.picMain).BeginInit();
		this.panel1.SuspendLayout();
		base.SuspendLayout();
		this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
		this.splitContainer1.Location = new System.Drawing.Point(0, 45);
		this.splitContainer1.Name = "splitContainer1";
		this.splitContainer1.Panel1.Controls.Add(this.dgvFileNames);
		this.splitContainer1.Panel2.Controls.Add(this.picMain);
		this.splitContainer1.Size = new System.Drawing.Size(1066, 719);
		this.splitContainer1.SplitterDistance = 363;
		this.splitContainer1.SplitterWidth = 5;
		this.splitContainer1.TabIndex = 0;
		this.dgvFileNames.AllowUserToAddRows = false;
		this.dgvFileNames.AllowUserToDeleteRows = false;
		this.dgvFileNames.AllowUserToResizeColumns = false;
		this.dgvFileNames.AllowUserToResizeRows = false;
		this.dgvFileNames.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
		this.dgvFileNames.Dock = System.Windows.Forms.DockStyle.Fill;
		this.dgvFileNames.Location = new System.Drawing.Point(0, 0);
		this.dgvFileNames.MultiSelect = false;
		this.dgvFileNames.Name = "dgvFileNames";
		this.dgvFileNames.RowHeadersVisible = false;
		this.dgvFileNames.RowTemplate.Height = 23;
		this.dgvFileNames.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
		this.dgvFileNames.Size = new System.Drawing.Size(363, 719);
		this.dgvFileNames.TabIndex = 1;
		this.dgvFileNames.SelectionChanged += new System.EventHandler(dgvFileNames_SelectionChanged);
		this.picMain.Dock = System.Windows.Forms.DockStyle.Fill;
		this.picMain.Location = new System.Drawing.Point(0, 0);
		this.picMain.Name = "picMain";
		this.picMain.Size = new System.Drawing.Size(698, 719);
		this.picMain.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
		this.picMain.TabIndex = 0;
		this.picMain.TabStop = false;
		this.panel1.Controls.Add(this.txtPath);
		this.panel1.Controls.Add(this.lblPath);
		this.panel1.Controls.Add(this.btnOpenPath);
		this.panel1.Controls.Add(this.txtSearch);
		this.panel1.Controls.Add(this.btnSearch);
		this.panel1.Controls.Add(this.btnRefresh);
		this.panel1.Dock = System.Windows.Forms.DockStyle.Top;
		this.panel1.Location = new System.Drawing.Point(0, 0);
		this.panel1.Name = "panel1";
		this.panel1.Size = new System.Drawing.Size(1066, 45);
		this.panel1.TabIndex = 1;
		this.txtPath.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
		this.txtPath.Location = new System.Drawing.Point(61, 5);
		this.txtPath.Name = "txtPath";
		this.txtPath.Size = new System.Drawing.Size(496, 34);
		this.txtPath.TabIndex = 5;
		this.lblPath.AutoSize = true;
		this.lblPath.Location = new System.Drawing.Point(4, 13);
		this.lblPath.Name = "lblPath";
		this.lblPath.Size = new System.Drawing.Size(51, 20);
		this.lblPath.TabIndex = 4;
		this.lblPath.Text = "路径：";
		this.btnOpenPath.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.btnOpenPath.Location = new System.Drawing.Point(563, 5);
		this.btnOpenPath.Name = "btnOpenPath";
		this.btnOpenPath.Size = new System.Drawing.Size(75, 33);
		this.btnOpenPath.TabIndex = 3;
		this.btnOpenPath.Text = "打开";
		this.btnOpenPath.Click += new System.EventHandler(btnOpenPath_Click);
		this.txtSearch.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.txtSearch.Location = new System.Drawing.Point(806, 5);
		this.txtSearch.Name = "txtSearch";
		this.txtSearch.Size = new System.Drawing.Size(238, 34);
		this.txtSearch.TabIndex = 2;
		this.btnSearch.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.btnSearch.Location = new System.Drawing.Point(725, 5);
		this.btnSearch.Name = "btnSearch";
		this.btnSearch.Size = new System.Drawing.Size(75, 33);
		this.btnSearch.TabIndex = 1;
		this.btnSearch.Text = "筛选";
		this.btnSearch.Click += new System.EventHandler(btnSearch_Click);
		this.btnRefresh.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.btnRefresh.Location = new System.Drawing.Point(644, 5);
		this.btnRefresh.Name = "btnRefresh";
		this.btnRefresh.Size = new System.Drawing.Size(75, 33);
		this.btnRefresh.TabIndex = 0;
		this.btnRefresh.Text = "刷新";
		this.btnRefresh.Click += new System.EventHandler(btnRefresh_Click);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
		base.ClientSize = new System.Drawing.Size(1066, 764);
		base.Controls.Add(this.splitContainer1);
		base.Controls.Add(this.panel1);
		this.Font = new System.Drawing.Font("微软雅黑", 10.5f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 134);
		base.MinimizeBox = false;
		base.Name = "FrmHistory";
		base.ShowIcon = false;
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
		this.Text = "图片回看";
		this.splitContainer1.Panel1.ResumeLayout(false);
		this.splitContainer1.Panel2.ResumeLayout(false);
		((System.ComponentModel.ISupportInitialize)this.splitContainer1).EndInit();
		this.splitContainer1.ResumeLayout(false);
		((System.ComponentModel.ISupportInitialize)this.dgvFileNames).EndInit();
		((System.ComponentModel.ISupportInitialize)this.picMain).EndInit();
		this.panel1.ResumeLayout(false);
		this.panel1.PerformLayout();
		base.ResumeLayout(false);
	}
}
