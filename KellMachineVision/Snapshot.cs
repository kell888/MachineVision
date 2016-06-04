using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using MVSDK;
using System.IO;

namespace KellMachineVision
{
    public partial class SnapshotDlg : Form
    {
        public SnapshotDlg()
        {
            InitializeComponent();
        }

        public void UpdateImage(Image img)
        {
            SnapshotBox.Width = img.Width;
            SnapshotBox.Height = img.Height;
            SnapshotBox.Image = img; 
            panel1.AutoScroll = true;
            //SnapshotBox.Layout = 
        }

        private void SnapshotDlg_FormClosing(object sender, FormClosingEventArgs e)
        {
            this.Hide();
            e.Cancel = true;
        }

        private void SnapshotBox_Click(object sender, EventArgs e)
        {

        }

    }
}