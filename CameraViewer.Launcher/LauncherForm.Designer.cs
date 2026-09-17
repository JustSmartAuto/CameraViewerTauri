namespace CameraViewer.Launcher
{
    partial class LauncherForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.lblStep = new System.Windows.Forms.Label();
            this.lblDetail = new System.Windows.Forms.Label();
            this.progress = new System.Windows.Forms.ProgressBar();
            this.SuspendLayout();
            //
            // lblStep
            //
            this.lblStep.Font = new System.Drawing.Font("Microsoft YaHei", 12F, System.Drawing.FontStyle.Bold);
            this.lblStep.Location = new System.Drawing.Point(24, 24);
            this.lblStep.Size = new System.Drawing.Size(352, 28);
            this.lblStep.Text = "CameraViewer";
            //
            // lblDetail
            //
            this.lblDetail.Font = new System.Drawing.Font("Microsoft YaHei", 9F);
            this.lblDetail.ForeColor = System.Drawing.Color.DimGray;
            this.lblDetail.Location = new System.Drawing.Point(24, 60);
            this.lblDetail.Size = new System.Drawing.Size(352, 24);
            this.lblDetail.Text = "";
            //
            // progress
            //
            this.progress.Location = new System.Drawing.Point(24, 92);
            this.progress.Size = new System.Drawing.Size(352, 10);
            this.progress.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
            //
            // LauncherForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(400, 130);
            this.Controls.Add(this.lblStep);
            this.Controls.Add(this.lblDetail);
            this.Controls.Add(this.progress);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "LauncherForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "CameraViewer";
            this.ResumeLayout(false);
        }

        public System.Windows.Forms.Label lblStep;
        public System.Windows.Forms.Label lblDetail;
        private System.Windows.Forms.ProgressBar progress;
    }
}
