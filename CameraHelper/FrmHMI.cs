using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace CameraHelper;

public class FrmHMI : AntdUI.Window
{
	private InSightConfig config;

	private IContainer components;

	private WebView2 webView21;

	public FrmHMI(InSightConfig cfg)
	{
		InitializeComponent();
		ThemeManager.ApplyIcon(this);
		config = cfg;
	}

	private async void FrmHMI_Load(object sender, EventArgs e)
	{
		await webView21.EnsureCoreWebView2Async();
		if (webView21 != null && webView21.CoreWebView2 != null)
		{
			webView21.CoreWebView2.Navigate("http://" + config.Address);
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
		this.webView21 = new Microsoft.Web.WebView2.WinForms.WebView2();
		((System.ComponentModel.ISupportInitialize)this.webView21).BeginInit();
		base.SuspendLayout();
		this.webView21.AllowExternalDrop = true;
		this.webView21.CreationProperties = null;
		this.webView21.DefaultBackgroundColor = System.Drawing.Color.White;
		this.webView21.Dock = System.Windows.Forms.DockStyle.Fill;
		this.webView21.Location = new System.Drawing.Point(0, 0);
		this.webView21.Name = "webView21";
		this.webView21.Size = new System.Drawing.Size(1026, 761);
		this.webView21.TabIndex = 0;
		this.webView21.ZoomFactor = 1.0;
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
		base.ClientSize = new System.Drawing.Size(1026, 761);
		base.Controls.Add(this.webView21);
		this.Font = new System.Drawing.Font("微软雅黑", 10.5f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 134);
		base.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
		base.MinimizeBox = false;
		base.Name = "FrmHMI";
		base.ShowIcon = false;
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
		this.Text = "HMI";
		base.Load += new System.EventHandler(FrmHMI_Load);
		((System.ComponentModel.ISupportInitialize)this.webView21).EndInit();
		base.ResumeLayout(false);
	}
}
