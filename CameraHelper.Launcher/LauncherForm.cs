using System;
using System.Drawing;
using System.Windows.Forms;

namespace CameraHelper.Launcher;

public class LauncherForm : Form
{
	private Label lblStep;

	private Label lblDetail;

	private ProgressBar progressBar;

	public LauncherForm()
	{
		InitializeComponent();
	}

	public void SetStep(string text)
	{
		lblStep.Text = text;
	}

	public void SetDetail(string text)
	{
		lblDetail.Text = text;
	}

	private void InitializeComponent()
	{
		base.SuspendLayout();
		base.BackColor = SystemColors.Window;
		base.ClientSize = new Size(400, 130);
		base.ControlBox = false;
		base.DoubleBuffered = true;
		base.FormBorderStyle = FormBorderStyle.FixedSingle;
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		base.Name = "LauncherForm";
		base.ShowIcon = true;
		base.ShowInTaskbar = false;
		base.StartPosition = FormStartPosition.CenterScreen;
		base.TopMost = true;
		base.Text = "CameraHelperJimJack";
		lblStep = new Label();
		lblStep.AutoSize = true;
		lblStep.Font = new Font("微软雅黑", 11f, FontStyle.Bold);
		lblStep.ForeColor = Color.FromArgb(51, 51, 51);
		lblStep.Location = new Point(16, 18);
		lblStep.Name = "lblStep";
		lblStep.Size = new Size(150, 24);
		lblStep.Text = "正在启动...";
		lblDetail = new Label();
		lblDetail.AutoSize = false;
		lblDetail.Font = new Font("微软雅黑", 9f);
		lblDetail.ForeColor = Color.FromArgb(119, 119, 119);
		lblDetail.Location = new Point(18, 52);
		lblDetail.Name = "lblDetail";
		lblDetail.Size = new Size(364, 20);
		lblDetail.Text = "";
		progressBar = new ProgressBar();
		progressBar.Location = new Point(16, 88);
		progressBar.Name = "progressBar";
		progressBar.Size = new Size(368, 8);
		progressBar.Style = ProgressBarStyle.Marquee;
		progressBar.MarqueeAnimationSpeed = 30;
		base.Controls.Add(lblStep);
		base.Controls.Add(lblDetail);
		base.Controls.Add(progressBar);
		base.ResumeLayout(false);
		base.PerformLayout();
	}
}
