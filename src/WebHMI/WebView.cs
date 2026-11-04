using System;
using System.ComponentModel;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using WebHMI.Properties;

namespace WebHMI;

public class WebView : UserControl
{
	private bool isMaxi;

	private bool isUpdating;

	private IContainer components;

	private Panel panel1;

	private TextBox txbxAddress;

	private WebView2 webViewer;

	private Label label1;

	private Button btnState;

	public int Index { get; set; }

	public CamConfigItem Config { get; set; } = new CamConfigItem();

	public event EventHandler<bool> MaxiEvent;

	public WebView()
	{
		InitializeComponent();
	}

	private async void WebView_Load(object sender, EventArgs e)
	{
		int delay = ProjectMgr.Inst.CameraConfig.Delay;
		delay = ((delay > 0) ? delay : 10);
		await Task.Delay(delay * 1000);
		await webViewer.EnsureCoreWebView2Async();
		ConnectHMI();
	}

	public void SetConfig(CamConfigItem config)
	{
		isUpdating = true;
		Config = config;
		txbxAddress.Text = Config.IP;
		isUpdating = false;
	}

	public void ConnectHMI()
	{
		if (!isUpdating && !string.IsNullOrEmpty(Config.IP) && webViewer != null && webViewer.CoreWebView2 != null)
		{
			webViewer.CoreWebView2.Navigate("http://" + Config.IP);
		}
	}

	private void txbxAddress_TextChanged(object sender, EventArgs e)
	{
		if (!string.IsNullOrEmpty(txbxAddress.Text) && txbxAddress.Text.Split('.').Length == 4)
		{
			Config.IP = txbxAddress.Text;
			ProjectMgr.Inst.SaveCameraConfig();
			ConnectHMI();
		}
	}

	private void btnState_Click(object sender, EventArgs e)
	{
		if (isMaxi)
		{
			btnState.BackgroundImage = Resources.Maxi;
			isMaxi = false;
		}
		else
		{
			btnState.BackgroundImage = Resources.Mini;
			isMaxi = true;
		}
		this.MaxiEvent?.Invoke(this, isMaxi);
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
		this.panel1 = new System.Windows.Forms.Panel();
		this.btnState = new System.Windows.Forms.Button();
		this.label1 = new System.Windows.Forms.Label();
		this.txbxAddress = new System.Windows.Forms.TextBox();
		this.webViewer = new Microsoft.Web.WebView2.WinForms.WebView2();
		this.panel1.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.webViewer).BeginInit();
		base.SuspendLayout();
		this.panel1.Controls.Add(this.btnState);
		this.panel1.Controls.Add(this.label1);
		this.panel1.Controls.Add(this.txbxAddress);
		this.panel1.Dock = System.Windows.Forms.DockStyle.Top;
		this.panel1.Location = new System.Drawing.Point(0, 0);
		this.panel1.Name = "panel1";
		this.panel1.Padding = new System.Windows.Forms.Padding(3);
		this.panel1.Size = new System.Drawing.Size(684, 40);
		this.panel1.TabIndex = 0;
		this.btnState.BackColor = System.Drawing.Color.White;
		this.btnState.BackgroundImage = WebHMI.Properties.Resources.Maxi;
		this.btnState.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
		this.btnState.Dock = System.Windows.Forms.DockStyle.Right;
		this.btnState.FlatAppearance.BorderSize = 0;
		this.btnState.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.btnState.Location = new System.Drawing.Point(646, 3);
		this.btnState.Name = "btnState";
		this.btnState.Size = new System.Drawing.Size(35, 34);
		this.btnState.TabIndex = 6;
		this.btnState.UseVisualStyleBackColor = false;
		this.btnState.Click += new System.EventHandler(btnState_Click);
		this.label1.AutoSize = true;
		this.label1.Location = new System.Drawing.Point(21, 13);
		this.label1.Name = "label1";
		this.label1.Size = new System.Drawing.Size(31, 16);
		this.label1.TabIndex = 2;
		this.label1.Text = "IP:";
		this.txbxAddress.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
		this.txbxAddress.Location = new System.Drawing.Point(68, 8);
		this.txbxAddress.Name = "txbxAddress";
		this.txbxAddress.Size = new System.Drawing.Size(557, 26);
		this.txbxAddress.TabIndex = 1;
		this.txbxAddress.TextChanged += new System.EventHandler(txbxAddress_TextChanged);
		this.webViewer.AllowExternalDrop = true;
		this.webViewer.CreationProperties = null;
		this.webViewer.DefaultBackgroundColor = System.Drawing.Color.White;
		this.webViewer.Dock = System.Windows.Forms.DockStyle.Fill;
		this.webViewer.Location = new System.Drawing.Point(0, 40);
		this.webViewer.Name = "webViewer";
		this.webViewer.Size = new System.Drawing.Size(684, 497);
		this.webViewer.TabIndex = 1;
		this.webViewer.ZoomFactor = 1.0;
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
		base.Controls.Add(this.webViewer);
		base.Controls.Add(this.panel1);
		this.Font = new System.Drawing.Font("宋体", 12f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 134);
		base.Name = "WebView";
		base.Size = new System.Drawing.Size(684, 537);
		base.Load += new System.EventHandler(WebView_Load);
		this.panel1.ResumeLayout(false);
		this.panel1.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.webViewer).EndInit();
		base.ResumeLayout(false);
	}
}
