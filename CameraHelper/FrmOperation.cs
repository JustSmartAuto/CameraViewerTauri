using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace CameraHelper;

public class FrmOperation : Form
{
	private IContainer components;

	private AntdUI.Button btnReset;

	private GroupBox gpBackup;

	private AntdUI.Button btnBrowser;

	private AntdUI.Input txtBackup;

	private AntdUI.Button btnBackup;

	private GroupBox gpReset;

	public FrmOperation()
	{
		InitializeComponent();
		ThemeManager.ApplyIcon(this);
		ThemeManager.ThemeChanged += ApplyTheme;
		ApplyTheme();
		bool isCh = ProjectMgr.Inst.SysConfig.Language == 0;
		gpReset.Text = (isCh ? "一键重启" : "One-Click Reset");
		btnReset.Text = (isCh ? "重启" : "Reset");
		gpBackup.Text = (isCh ? "一键备份" : "One-Click Backup");
		btnBrowser.Text = (isCh ? "浏览" : "Browse");
		btnBackup.Text = (isCh ? "备份" : "Backup");
	}

	private void ApplyTheme()
	{
		ForeColor = ThemeManager.Fg;
		BackColor = ThemeManager.Bg3;
		ThemeManager.StyleButtons(this);
	}

	private void btnReset_Click(object sender, EventArgs e)
	{
		try
		{
			foreach (InSightConfig cameraConfig in ProjectMgr.Inst.CameraConfigs)
			{
				NativeMode.Reset(cameraConfig);
			}
		}
		catch
		{
		}
	}

	private void btnBrowser_Click(object sender, EventArgs e)
	{
		try
		{
			FolderBrowserDialog dlg = new FolderBrowserDialog();
			if (dlg.ShowDialog() == DialogResult.OK)
			{
				txtBackup.Text = dlg.SelectedPath;
			}
		}
		catch
		{
		}
	}

	private async void btnBackup_Click(object sender, EventArgs e)
	{
		string dir = txtBackup.Text;
		if (string.IsNullOrEmpty(dir))
		{
			return;
		}
		bool isCh = ProjectMgr.Inst.SysConfig.Language == 0;
		try
		{
			if (!Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}
			foreach (CvsInSightExt cam in ProjectMgr.Inst.Cameras)
			{
				if (cam.IsConnected)
				{
					string job = await cam.InSight.GetJobName();
					string path = Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd_HH-mm"), cam.InSight.CameraInfo.HostName, job.Replace("/", ""));
					string curDir = Path.GetDirectoryName(path);
					if (!Directory.Exists(curDir))
					{
						Directory.CreateDirectory(curDir);
					}
					string jobNameResp = await cam.InSight.GetJobName();
					JObject resp = (JObject)(await cam.InSight.SaveJobData(jobNameResp).ConfigureAwait(continueOnCapturedContext: false));
					if (resp == null)
					{
						return;
					}
					byte[] byteArr = Convert.FromBase64String(resp["base64"].Value<string>());
					File.WriteAllBytes(path, byteArr);
				}
			}
			MessageBox.Show(isCh ? "备份成功" : "Backup Successfully");
		}
		catch
		{
			MessageBox.Show(isCh ? "备份失败" : "Backup Failed");
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
		this.btnReset = new AntdUI.Button();
		this.gpBackup = new System.Windows.Forms.GroupBox();
		this.btnBackup = new AntdUI.Button();
		this.btnBrowser = new AntdUI.Button();
		this.txtBackup = new AntdUI.Input();
		this.gpReset = new System.Windows.Forms.GroupBox();
		this.gpBackup.SuspendLayout();
		this.gpReset.SuspendLayout();
		base.SuspendLayout();
		this.btnReset.Location = new System.Drawing.Point(25, 32);
		this.btnReset.Margin = new System.Windows.Forms.Padding(4);
		this.btnReset.Name = "btnReset";
		this.btnReset.Size = new System.Drawing.Size(210, 40);
		this.btnReset.TabIndex = 0;
		this.btnReset.Text = "重启";
		this.btnReset.Click += new System.EventHandler(btnReset_Click);
		this.gpBackup.Controls.Add(this.btnBackup);
		this.gpBackup.Controls.Add(this.btnBrowser);
		this.gpBackup.Controls.Add(this.txtBackup);
		this.gpBackup.Location = new System.Drawing.Point(42, 168);
		this.gpBackup.Margin = new System.Windows.Forms.Padding(4);
		this.gpBackup.Name = "gpBackup";
		this.gpBackup.Padding = new System.Windows.Forms.Padding(4);
		this.gpBackup.Size = new System.Drawing.Size(552, 168);
		this.gpBackup.TabIndex = 1;
		this.gpBackup.TabStop = false;
		this.gpBackup.Text = "一键备份";
		this.btnBackup.Location = new System.Drawing.Point(25, 84);
		this.btnBackup.Margin = new System.Windows.Forms.Padding(4);
		this.btnBackup.Name = "btnBackup";
		this.btnBackup.Size = new System.Drawing.Size(210, 40);
		this.btnBackup.TabIndex = 2;
		this.btnBackup.Text = "备份";
		this.btnBackup.Click += new System.EventHandler(btnBackup_Click);
		this.btnBrowser.Location = new System.Drawing.Point(453, 33);
		this.btnBrowser.Name = "btnBrowser";
		this.btnBrowser.Size = new System.Drawing.Size(75, 33);
		this.btnBrowser.TabIndex = 1;
		this.btnBrowser.Text = "浏览";
		this.btnBrowser.Click += new System.EventHandler(btnBrowser_Click);
		this.txtBackup.Location = new System.Drawing.Point(25, 33);
		this.txtBackup.Name = "txtBackup";
		this.txtBackup.Size = new System.Drawing.Size(422, 34);
		this.txtBackup.TabIndex = 0;
		this.gpReset.Controls.Add(this.btnReset);
		this.gpReset.Location = new System.Drawing.Point(42, 30);
		this.gpReset.Name = "gpReset";
		this.gpReset.Size = new System.Drawing.Size(552, 100);
		this.gpReset.TabIndex = 2;
		this.gpReset.TabStop = false;
		this.gpReset.Text = "一键重启";
		base.AutoScaleDimensions = new System.Drawing.SizeF(7f, 14f);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
		base.ClientSize = new System.Drawing.Size(643, 494);
		base.Controls.Add(this.gpReset);
		base.Controls.Add(this.gpBackup);
		this.Font = new System.Drawing.Font("宋体", 10.5f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 134);
		base.FormBorderStyle = System.Windows.Forms.FormBorderStyle.SizableToolWindow;
		base.Margin = new System.Windows.Forms.Padding(4);
		base.Name = "FrmOperation";
		this.Text = "Operation";
		this.gpBackup.ResumeLayout(false);
		this.gpBackup.PerformLayout();
		this.gpReset.ResumeLayout(false);
		base.ResumeLayout(false);
	}
}
