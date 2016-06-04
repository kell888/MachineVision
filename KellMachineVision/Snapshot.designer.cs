//BIG5 TRANS ALLOWED
namespace KellMachineVision
{
    partial class SnapshotDlg
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.SnapshotBox = new System.Windows.Forms.PictureBox();
            this.panel1 = new System.Windows.Forms.Panel();
            ((System.ComponentModel.ISupportInitialize)(this.SnapshotBox)).BeginInit();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // SnapshotBox
            // 
            this.SnapshotBox.Location = new System.Drawing.Point(0, 0);
            this.SnapshotBox.Name = "SnapshotBox";
            this.SnapshotBox.Size = new System.Drawing.Size(614, 540);
            this.SnapshotBox.TabIndex = 0;
            this.SnapshotBox.TabStop = false;
            this.SnapshotBox.Click += new System.EventHandler(this.SnapshotBox_Click);
            // 
            // panel1
            // 
            this.panel1.AutoScroll = true;
            this.panel1.AutoSize = true;
            this.panel1.Controls.Add(this.SnapshotBox);
            this.panel1.Location = new System.Drawing.Point(12, 3);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(617, 543);
            this.panel1.TabIndex = 1;
            // 
            // SnapshotDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoScroll = true;
            this.ClientSize = new System.Drawing.Size(641, 558);
            this.Controls.Add(this.panel1);
            this.Name = "SnapshotDlg";
            this.Text = "Snapshot";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.SnapshotDlg_FormClosing);
            ((System.ComponentModel.ISupportInitialize)(this.SnapshotBox)).EndInit();
            this.panel1.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.PictureBox SnapshotBox;
        private System.Windows.Forms.Panel panel1;
    }
}