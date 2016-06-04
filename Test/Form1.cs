using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using KellMachineVision;
using MVSDK;
using System.IO;

namespace Test
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }
        KellMV mv;
        Point point;
        IDraw selected;

        private void button1_Click(object sender, EventArgs e)
        {
            emSdkRunMode status = mv.PlayOrPause(0);
            if (status == emSdkRunMode.RUNMODE_PLAY)
            {
                button1.Text = "Pause";
                button7.Enabled = true;
            }
            else if (status == emSdkRunMode.RUNMODE_PAUSE)
            {
                button1.Text = "Play";
                button7.Enabled = true;
            }
            else
            {
                //button1.Text = "Select";
                button7.Enabled = false;
            }
            textBox2.Text = mv.SN;
            textBox3.Text = mv.FriendlyName;
            Color color;
            if (!mv.GetCrossLineStatus(out color))
                button6.Text = "ShowCrossLine";
            else
                button6.Text = "HideCrossLine";
            //MessageBox.Show("用户数据区最大长度：" + mv.CustomDataMaxLen);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            LogoInfo li = new LogoInfo("KellMachineVision", new Size(80, 80), new Rectangle(0, 0, 800, 80), Color.Red);
            mv = new KellMV(this, panel1);//, li);//可以选择加上logo(水印)
            mv.ChangedStatus += new EventHandler<VideoArgs>(mv_ChangedStatus);
            mv.RefreshVideoInfo += new EventHandler<VideoInfoArgs>(mv_RefreshVideoInfo);
            mv.VideoInitError += new EventHandler<VideoInfoArgs>(mv_VideoInitError);
            mv.VideoPreviewError += new EventHandler<VideoInfoArgs>(mv_VideoPreviewError);
            mv.VideoSaveImageError += new EventHandler<VideoInfoArgs>(mv_VideoSaveImageError);
            //mv.CameraParameterChanged += new EventHandler<VideoInfoArgs>(mv_CameraParameterChanged);
        }

        void mv_CameraParameterChanged(object sender, VideoInfoArgs e)
        {
            MessageBox.Show("Camera Parameter [" + e.Info + "] has Changed!");
        }

        void mv_VideoSaveImageError(object sender, VideoInfoArgs e)
        {
            MessageBox.Show(e.Info);
        }

        void mv_VideoPreviewError(object sender, VideoInfoArgs e)
        {
            MessageBox.Show(e.Info);
        }

        void mv_VideoInitError(object sender, VideoInfoArgs e)
        {
            MessageBox.Show(e.Info);
        }

        void mv_RefreshVideoInfo(object sender, VideoInfoArgs e)
        {
            toolStripStatusLabel2.Text = e.Info;

            Color color;
            bool show = mv.GetCrossLineStatus(out color);
            if (!show)
                button6.Text = "ShowCrossLine";
            else
                button6.Text = "HideCrossLine";
        }

        void mv_ChangedStatus(object sender, VideoArgs e)
        {
            toolStripStatusLabel1.Text = Convert.ToString(e.Status);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            saveFileDialog1.Filter = "BMP图像文件(*.bmp)|*.bmp|JPG图像文件(*.jpg)|*.jpg|PNG图像文件(*.png)|*.png";
            if (saveFileDialog1.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                mv.SaveImage(saveFileDialog1.FileName);
            }
            saveFileDialog1.Dispose();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            mv.Dispose();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            Image img = mv.SnapshotImage;
            this.BackgroundImage = img;
        }

        private void button4_Click(object sender, EventArgs e)
        {
            Bitmap bmp;
            using (Graphics g = mv.GetDrawGraphicFromOverlay(out bmp))
            {
                try
                {
                    string txt = textBox1.Text;
                    Font font = new Font("宋体", 50);
                    Brush brush = new SolidBrush(Color.Red);
                    g.DrawString(txt, font, brush, point);
                    List<Point> ps = new List<Point>();
                    ps.Add(point);
                    List<object> ds = new List<object>();
                    ds.Add(txt);
                    ds.Add(font);
                    ds.Add(brush);
                    mv.DrawItems.Add(new DrawItem(DateTime.Now.ToString("yyyyMMddHHmmss"), new ItemParam(ps, ds, DrawType.DrawString, Color.Red)));
                }
                catch
                { }
                finally
                {
                    mv.EndDraw(bmp);
                }
            }
        }

        private void button5_Click(object sender, EventArgs e)
        {
            mv.ShowSettings();
        }

        private void button6_Click(object sender, EventArgs e)
        {
            Color color;
            bool hide = mv.GetCrossLineStatus(out color);
            mv.ShowOrHideCrossLine(Color.Blue, hide);
        }

        private void button7_Click(object sender, EventArgs e)
        {
            if (mv.Stop())
            {
                button7.Enabled = false;
            }
        }

        private void button8_Click(object sender, EventArgs e)
        {
            if (mv.Recording)
            {
                if (mv.StopCapture())
                    button8.Text = "Record";
            }
            else
            {
                if (mv.StartCapture())
                    button8.Text = "...";
            }
        }

        private void panel1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                point = new Point(e.X, e.Y);
                Bitmap bmp;
                using (Graphics g = mv.GetDrawGraphicFromOverlay(out bmp))
                {
                    try
                    {
                        g.DrawEllipse(Pens.Blue, point.X, point.Y, 200, 200);
                        List<Point> ps = new List<Point>();
                        ps.Add(point);
                        List<object> ds = new List<object>();
                        ds.Add(200);
                        ds.Add(200);
                        ItemValue value = new ItemValue("椭圆测量值", new EllipseValue(100, 200, 0));
                        mv.DrawItems.Add(new DrawItem(DateTime.Now.ToString("yyyyMMddHHmmss"), new ItemParam(ps, ds, DrawType.DrawEllipse, Color.Blue), value));
                    }
                    catch { }
                    finally
                    {
                        mv.EndDraw(bmp);
                    }
                }
            }
            else if (e.Button == System.Windows.Forms.MouseButtons.Right)//取消选择
            {
                if (selected != null)
                {
                    mv.RestoreItem(selected);
                    selected = null;
                    label3.Text = "";
                }
            }
        }

        private void button9_Click(object sender, EventArgs e)
        {
            bool scale = !mv.ScaleDisplay;
            if (mv.SetDisplayMode(scale))
            {
                if (scale)
                    button9.Text = "原图";
                else
                    button9.Text = "缩放";
            }
        }

        private void panel1_MouseMove(object sender, MouseEventArgs e)
        {
            foreach (IDraw item in mv.DrawItems)
            {
                if (mv.IsSelectRange(e.Location, item))
                {
                    selected = item;
                    mv.HighlightItem(selected);
                    label3.Text = mv.GetMeasureValue(selected);
                    break;
                }
            }
            if (selected != null)
            {
                foreach (IDraw item in mv.DrawItems)
                {
                    if (selected.ID != item.ID && !mv.IsSelectRange(e.Location, item))
                    {
                        mv.RestoreItem(item);
                    }
                }
            }
        }

        private void button10_Click(object sender, EventArgs e)
        {
            if (mv.SelectDeviceToPlay())
            {
                button1.Text = "Pause";
                button7.Enabled = true;
            }
            textBox2.Text = mv.SN;
            textBox3.Text = mv.FriendlyName;
            Color color;
            if (!mv.GetCrossLineStatus(out color))
                button6.Text = "ShowCrossLine";
            else
                button6.Text = "HideCrossLine";
            //MessageBox.Show("用户数据区最大长度：" + mv.CustomDataMaxLen);
        }

        private void button12_Click(object sender, EventArgs e)
        {
            mv.FriendlyName = textBox3.Text;
        }

        private void button11_Click(object sender, EventArgs e)
        {
            mv.SN = textBox2.Text;
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            mv.Monochrome = checkBox1.Checked;
        }

        private void button13_Click(object sender, EventArgs e)
        {
            openFileDialog1.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            openFileDialog1.Filter = "图形文件(*.grap)|*.grap";
            openFileDialog1.Title = "选择要载入的图形文件";
            if (openFileDialog1.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                bool flag = mv.LoadDrawItems(openFileDialog1.FileName);
                //if (flag)
                //    MessageBox.Show("载入成功！");
                //else
                //    MessageBox.Show("载入失败！");
            }
            openFileDialog1.Dispose();
        }

        private void button14_Click(object sender, EventArgs e)
        {
            saveFileDialog1.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            saveFileDialog1.Filter = "图形文件(*.grap)|*.grap";
            saveFileDialog1.Title = "保存图形文件到...";
            if (saveFileDialog1.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                bool flag = mv.SaveDrawItems(saveFileDialog1.FileName);
                if (flag)
                    MessageBox.Show("保存成功！");
                else
                    MessageBox.Show("保存失败！");
            }
            saveFileDialog1.Dispose();
        }

        private void button15_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("确定要清空全部图形么？", "清空提醒", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == System.Windows.Forms.DialogResult.OK)
            {
                selected = null;
                label3.Text = "";
                mv.ClearDraws();
            }
        }

        private void button16_Click(object sender, EventArgs e)
        {
            string name = textBox4.Text.Trim();
            if (name != "")
            {
                mv.RestoreItem(selected);
                List<IDraw> items = mv.GetDrawItems(a => a.Name.Contains(name));
                foreach (IDraw item in items)
                {
                    mv.HighlightItem(item);
                }
            }
        }

        private void button17_Click(object sender, EventArgs e)
        {
            saveFileDialog1.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            saveFileDialog1.Filter = "相机参数文档(*.Config)|*.Config";
            saveFileDialog1.Title = "保存相机参数文档到...";
            if (saveFileDialog1.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                bool flag = mv.SaveCameraParameter(saveFileDialog1.FileName);
                if (flag)
                    MessageBox.Show("保存成功！");
                else
                    MessageBox.Show("保存失败！");
            }
            saveFileDialog1.Dispose();
        }

        private void button18_Click(object sender, EventArgs e)
        {
            openFileDialog1.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            openFileDialog1.Filter = "相机参数文档(*.Config)|*.Config";
            openFileDialog1.Title = "选择要载入的相机参数文档";
            if (openFileDialog1.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                bool flag = mv.LoadCameraParameter(openFileDialog1.FileName);
                //if (flag)
                //    MessageBox.Show("载入成功！");
                //else
                //    MessageBox.Show("载入失败！");
            }
            openFileDialog1.Dispose();
        }

        private void button19_Click(object sender, EventArgs e)
        {
            mv.LoadDefaultParameter();
        }
    }
}
