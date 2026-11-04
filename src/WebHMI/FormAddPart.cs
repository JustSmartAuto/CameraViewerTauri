using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace WebHMI;

public class FormAddPart : Form
{
	private IContainer components;

	private Button btnOK;

	private ComboBox comboBox1;

	private Label label1;

	public bool IsOK { get; private set; }

	public EPartType PartType { get; private set; }

	public FormAddPart()
	{
		InitializeComponent();
		comboBox1.SelectedIndex = 0;
	}

	private void btnOK_Click(object sender, EventArgs e)
	{
		IsOK = true;
		PartType = ((comboBox1.SelectedIndex != 0) ? EPartType.DateTime : EPartType.Name);
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
		this.btnOK = new System.Windows.Forms.Button();
		this.comboBox1 = new System.Windows.Forms.ComboBox();
		this.label1 = new System.Windows.Forms.Label();
		base.SuspendLayout();
		this.btnOK.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
		this.btnOK.Location = new System.Drawing.Point(280, 90);
		this.btnOK.Margin = new System.Windows.Forms.Padding(4);
		this.btnOK.Name = "btnOK";
		this.btnOK.Size = new System.Drawing.Size(88, 27);
		this.btnOK.TabIndex = 5;
		this.btnOK.Text = "OK";
		this.btnOK.UseVisualStyleBackColor = true;
		this.btnOK.Click += new System.EventHandler(btnOK_Click);
		this.comboBox1.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
		this.comboBox1.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.comboBox1.FormattingEnabled = true;
		this.comboBox1.Items.AddRange(new object[2] { "名称", "PC时间" });
		this.comboBox1.Location = new System.Drawing.Point(80, 42);
		this.comboBox1.Name = "comboBox1";
		this.comboBox1.Size = new System.Drawing.Size(238, 22);
		this.comboBox1.TabIndex = 6;
		this.label1.AutoSize = true;
		this.label1.Location = new System.Drawing.Point(12, 45);
		this.label1.Name = "label1";
		this.label1.Size = new System.Drawing.Size(35, 14);
		this.label1.TabIndex = 7;
		this.label1.Text = "类型";
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
		base.ClientSize = new System.Drawing.Size(381, 130);
		base.Controls.Add(this.label1);
		base.Controls.Add(this.comboBox1);
		base.Controls.Add(this.btnOK);
		this.Font = new System.Drawing.Font("宋体", 10.5f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 134);
		base.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
		base.Margin = new System.Windows.Forms.Padding(4);
		base.Name = "FormAddPart";
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
		this.Text = "添加项";
		base.ResumeLayout(false);
		base.PerformLayout();
	}
}
