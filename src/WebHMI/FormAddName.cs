using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace WebHMI;

public class FormAddName : Form
{
	private IContainer components;

	private TextBox txtName;

	private Label label1;

	private Button btnOK;

	public bool IsOK { get; private set; }

	public string Output { get; private set; }

	public FormAddName()
	{
		InitializeComponent();
	}

	private void btnOK_Click(object sender, EventArgs e)
	{
		Output = txtName.Text;
		IsOK = true;
		Close();
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
		this.txtName = new System.Windows.Forms.TextBox();
		this.label1 = new System.Windows.Forms.Label();
		this.btnOK = new System.Windows.Forms.Button();
		base.SuspendLayout();
		this.txtName.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
		this.txtName.Location = new System.Drawing.Point(78, 40);
		this.txtName.Name = "txtName";
		this.txtName.Size = new System.Drawing.Size(254, 23);
		this.txtName.TabIndex = 0;
		this.label1.AutoSize = true;
		this.label1.Location = new System.Drawing.Point(21, 43);
		this.label1.Name = "label1";
		this.label1.Size = new System.Drawing.Size(35, 14);
		this.label1.TabIndex = 1;
		this.label1.Text = "名称";
		this.btnOK.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
		this.btnOK.Location = new System.Drawing.Point(297, 106);
		this.btnOK.Name = "btnOK";
		this.btnOK.Size = new System.Drawing.Size(75, 23);
		this.btnOK.TabIndex = 2;
		this.btnOK.Text = "OK";
		this.btnOK.UseVisualStyleBackColor = true;
		this.btnOK.Click += new System.EventHandler(btnOK_Click);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
		base.ClientSize = new System.Drawing.Size(384, 141);
		base.Controls.Add(this.btnOK);
		base.Controls.Add(this.label1);
		base.Controls.Add(this.txtName);
		this.Font = new System.Drawing.Font("宋体", 10.5f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 134);
		base.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
		base.Name = "FormAddName";
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
		this.Text = "添加";
		base.ResumeLayout(false);
		base.PerformLayout();
	}
}
