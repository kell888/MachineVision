//#define USE_CALL_BACK
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Threading;
using MVSDK;//使用MindVision .net SDK接口
using CameraHandle = System.Int32;
using MvApi = MVSDK.MvApi;
using System.IO;
using System.Drawing.Drawing2D;
using System.Runtime.Serialization.Formatters.Binary;

namespace KellMachineVision
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BITMAPFILEHEADER
    {
        public ushort bfType;
        public uint bfSize;
        public ushort bfReserved1;
        public ushort bfReserved2;
        public uint bfOffBits;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
        public const int BI_RGB = 0;
    }

    public class VideoArgs : EventArgs
    {
        Control canvas;

        public Control Canvas
        {
            get { return canvas; }
        }
        emSdkRunMode status = emSdkRunMode.RUNMODE_STOP;

        public emSdkRunMode Status
        {
            get { return status; }
        }

        public VideoArgs(Control canvas, emSdkRunMode status)
        {
            this.canvas = canvas;
            this.status = status;
        }
    }

    public class VideoInfoArgs : EventArgs
    {
        string info;

        public string Info
        {
            get { return info; }
        }

        public VideoInfoArgs(string info)
        {
            this.info = info;
        }
    }

    public class KellMV : IDisposable
    {
        #region variable
        protected CameraHandle m_hCamera = 0;              // 相机句柄
        protected IntPtr m_ImageBuffer;                    // 预览通道RGB图像指针
        protected Image m_ImageSnapshot;                   // 抓拍通道RGB图像
        protected Bitmap m_ImageOverlay;                   // Overlay通道RGB图像
        protected tSdkCameraCapbility tCameraCapability;   // 相机特性描述
        protected tSdkFrameHead m_tFrameHead;
        protected SnapshotDlg m_DlgSnapshot = new SnapshotDlg();//显示抓拍图像的窗口
        public bool m_bSnapshotImage = false;
        protected emSdkRunMode status;
        protected bool recording;
        private string recordPath;
        protected Form parent;
        protected Control PreviewBox;
        protected Size snapshotSize;
        protected string lastSaveFilePath;
        protected tSdkCameraDevInfo[] tCameraDevInfoList;
        protected System.Windows.Forms.Timer timer1;
        //protected bool m_bEraseBk = false;
        //protected System.Windows.Forms.Timer timer2;
        public event EventHandler<VideoArgs> ChangedStatus;
        public event EventHandler<VideoInfoArgs> RefreshVideoInfo;
        public event EventHandler<VideoInfoArgs> VideoInitError;
        public event EventHandler<VideoInfoArgs> VideoPreviewError;
        public event EventHandler<VideoInfoArgs> VideoSaveImageError;
        public event EventHandler<VideoInfoArgs> CameraParameterChanged;
        protected pfnCameraGrabberFrameListener m_FrameListener;
        protected pfnCameraGrabberSaveImageComplete m_SaveImageComplete;
        protected Mutex m_OverlayMutex = new Mutex();
        protected int selectRange = 2;
        protected List<IDraw> drawItems;
        private byte[] pText;
        private byte[] FontFilePath;
        private CameraHandle left;
        private CameraHandle top;
        private uint width;
        private uint height;
        private uint textColor;
        private uint uFlags;
        private uint fontWidth;
        private uint fontHeight;
        private bool scaleDisplay = true;
        private int deviceCount;
        private int customDataMaxLen;
        private bool loadOver;
        #endregion

        public KellMV(Form parent, Control PreviewBox, LogoInfo logo = null)
        {
            //MvApi.CameraSdkInit(1);//0为英文，1为中文，默认为中文
            drawItems = new List<IDraw>();
            recordPath = AppDomain.CurrentDomain.BaseDirectory + "Records";
            int iCameraCounts = 12;
            tCameraDevInfoList = new tSdkCameraDevInfo[iCameraCounts];
            //如果有多个相机时，表示最大只获取最多iCameraCounts个相机的信息列表。该变量必须初始化，并且大于1
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(new tSdkCameraDevInfo()) * iCameraCounts);
            if (MvApi.CameraEnumerateDevice(ptr, ref iCameraCounts) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
            {
                deviceCount = iCameraCounts;
                for (int i = 0; i < iCameraCounts; i++)
                {
                    tCameraDevInfoList[i] = (tSdkCameraDevInfo)Marshal.PtrToStructure((IntPtr)((int)ptr + i * Marshal.SizeOf(new tSdkCameraDevInfo())), typeof(tSdkCameraDevInfo));
                }
            }
            Marshal.FreeHGlobal(ptr);

            this.parent = parent;
            this.PreviewBox = PreviewBox;

            m_FrameListener = new pfnCameraGrabberFrameListener(CameraGrabberFrameListener);
            m_SaveImageComplete = new pfnCameraGrabberSaveImageComplete(CameraGrabberSaveImageComplete);

            if (logo != null)
                DrawLogo(logo.LogoText, logo.TextSize, logo.ShowRect, logo.TextColor);

            this.timer1 = new System.Windows.Forms.Timer();
            this.timer1.Enabled = true;
            this.timer1.Interval = 1000;
            this.timer1.Tick += new System.EventHandler(this.timer1_Tick);
        }

        // 每当Grabber捕获到一帧图像时，会分3个阶段来依次调用FrameListener
        // 如果FrameListener返回0，Grabber将会丢弃此帧并结束针对此帧的所有后续处理阶段
        // 阶段0: RAW数据处理，pFrameBuffer=Raw数据
        // 阶段1: 截图前处理，pFrameBuffer=RGB数据
        // 阶段2: 显示前处理，pFrameBuffer=RGB数据
        private int CameraGrabberFrameListener(IntPtr Grabber, int Phase, IntPtr pFrameBuffer, ref tSdkFrameHead pFrameHead, IntPtr Context)
        {
            try
            {
                if (loadOver)
                {
                    string parameter;
                    if (HasChanged(pFrameHead, out parameter))
                    {
                        snapshotSize = new Size(pFrameHead.iWidth, pFrameHead.iHeight);
                        OnCameraParameterChanged(new VideoInfoArgs(parameter));
                    }
                }
                m_tFrameHead = pFrameHead;
                if (Phase == 0)
                {
                    // RAW数据处理，pFrameBuffer=Raw数据
                }
                else if (Phase == 1)
                {
                    // 截图前处理，pFrameBuffer=RGB数据
                    if (m_bSnapshotImage)
                    {
                        m_ImageSnapshot = Image.FromStream(GetImageStream(ref pFrameHead, pFrameBuffer));
                        m_bSnapshotImage = false;
                    }
                    MvApi.CameraDrawText(pFrameBuffer, ref pFrameHead, FontFilePath, fontWidth, fontHeight, pText, left, top, width, height, textColor, uFlags);
                    if (recording)
                        MvApi.CameraPushFrame(m_hCamera, pFrameBuffer, ref pFrameHead);
                }
                else if (Phase == 2)
                {
                    // 显示前处理，pFrameBuffer=RGB数据
                    
                    MvApi.CameraImageOverlay(m_hCamera, pFrameBuffer, ref pFrameHead);

                    DrawToFrameBuffer(pFrameBuffer, pFrameHead, m_ImageOverlay);
                }
            }
            catch (Exception e)
            {
                OnVideoPreviewError(new VideoInfoArgs(e.Message));
            }
            return 1;
        }

        private bool HasChanged(tSdkFrameHead pFrameHead, out string parameter)
        {
            parameter = string.Empty;
            if (m_tFrameHead.bIsTrigger != pFrameHead.bIsTrigger) { parameter = "bIsTrigger"; return true; }
            //if (m_tFrameHead.fAnalogGain != pFrameHead.fAnalogGain) { parameter = "fAnalogGain"; return true; }
            //if (m_tFrameHead.fBgain != pFrameHead.fBgain) { parameter = "fBgain"; return true; }
            //if (m_tFrameHead.fGgain != pFrameHead.fGgain) { parameter = "fGgain"; return true; }
            //if (m_tFrameHead.fRgain != pFrameHead.fRgain) { parameter = "fRgain"; return true; }
            //if (m_tFrameHead.iContrast != pFrameHead.iContrast) { parameter = "iContrast"; return true; }
            //if (m_tFrameHead.iGamma != pFrameHead.iGamma) { parameter = "iGamma"; return true; }
            if (m_tFrameHead.iHeight != pFrameHead.iHeight) { parameter = "iHeight"; return true; }
            if (m_tFrameHead.iHeightZoomSw != pFrameHead.iHeightZoomSw) { parameter = "iHeightZoomSw"; return true; }
            //if (m_tFrameHead.iSaturation != pFrameHead.iSaturation) { parameter = "iSaturation"; return true; }
            if (m_tFrameHead.iWidth != pFrameHead.iWidth) { parameter = "iWidth"; return true; }
            if (m_tFrameHead.iWidthZoomSw != pFrameHead.iWidthZoomSw) { parameter = "iWidthZoomSw"; return true; }
            //if (m_tFrameHead.uBytes != pFrameHead.uBytes) { parameter = "uBytes"; return true; }
            if (m_tFrameHead.uiExpTime != pFrameHead.uiExpTime) { parameter = "uiExpTime"; return true; }
            //if (m_tFrameHead.uiMediaType != pFrameHead.uiMediaType) { parameter = "uiMediaType"; return true; }
            //if (m_tFrameHead.uiTimeStamp != pFrameHead.uiTimeStamp) { parameter = "uiTimeStamp"; return true; }
            return false;
        }
        // 需要调用CameraImage_Destroy释放
        private void CameraGrabberSaveImageComplete(IntPtr Grabber, IntPtr Image, CameraSdkStatus Status, IntPtr Context)
        {
            try
            {
                if (Image != IntPtr.Zero)
                {
                    if (MvApi.CameraGrabber_SaveImage(m_ImageBuffer, out Image, 2000) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                    {
                        byte[] file_path_bytes = Encoding.Default.GetBytes(lastSaveFilePath);
                        MvApi.CameraImage_SaveAsBmp(Image, file_path_bytes);
                    }
                }
            }
            catch (Exception e)
            {
                OnVideoSaveImageError(new VideoInfoArgs(e.Message));
            }
            finally
            {
                MvApi.CameraImage_Destroy(Image);
            }
        }

        public float GetDispFrameSpeed(out float capFrameSpeed)
        {
            capFrameSpeed = 0;
            float frameRate = 0;
            if (m_hCamera > 0)
            {
                tSdkGrabberStat stat;
                if (MvApi.CameraGrabber_GetStat(m_ImageBuffer, out stat) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                {
                    frameRate = stat.DispFps;
                    capFrameSpeed = stat.CapFps;
                }
            }
            return frameRate;
        }

        /// <summary>
        /// 设置第iLine个十字线
        /// </summary>
        /// <param name="color"></param>
        /// <param name="hide"></param>
        /// <param name="iLine"></param>
        public void ShowOrHideCrossLine(Color color, bool hide = false, int iLine = 0)
        {
            if (m_hCamera > 0)
            {
                if (hide)
                {
                    MvApi.CameraSetCrossLine(m_hCamera, iLine, snapshotSize.Width / 2, snapshotSize.Height / 2, (uint)color.ToArgb(), 0);
                }
                else
                {
                    MvApi.CameraSetCrossLine(m_hCamera, iLine, snapshotSize.Width / 2, snapshotSize.Height / 2, (uint)color.ToArgb(), 1);
                }
            }
        }
        /// <summary>
        /// 获取第iLine个十字线
        /// </summary>
        /// <param name="color"></param>
        /// <param name="iLine"></param>
        /// <returns></returns>
        public bool GetCrossLineStatus(out Color color, int iLine = 0)
        {
            int x = 0, y = 0;
            uint c = 0;
            uint showCrossLine = 0;
            MvApi.CameraGetCrossLine(m_hCamera, iLine, ref x, ref y, ref c, ref showCrossLine);
            color = Color.FromArgb((int)c);
            return showCrossLine == 1;
        }

        public int SelectRange
        {
            get { return selectRange; }
            set { selectRange = value; }
        }

        public bool ScaleDisplay
        {
            get
            {
                return scaleDisplay; 
            }
        }

        public bool Recording
        {
            get { return recording; }
        }

        public string RecordPath
        {
            get { return recordPath; }
        }

        public void DrawLogo(string logoTxt, Size fontSize, Rectangle rect, Color color, emCameraDrawTextFlags flags = emCameraDrawTextFlags.Default, string fontPath = @"C:\Windows\Fonts\simsun.ttc")
        {
            pText = Encoding.Default.GetBytes(logoTxt);
            FontFilePath = Encoding.Default.GetBytes(fontPath);
            fontWidth = (uint)fontSize.Width;
            fontHeight = (uint)fontSize.Height;
            left = rect.Left;
            top = rect.Top;
            width = (uint)rect.Width;
            height = (uint)rect.Height;
            textColor = (uint)color.ToArgb();
            uFlags = (uint)flags;      
        }

        public List<IDraw> GetDrawItems(Predicate<IDraw> match)
        {
            return drawItems.FindAll(match);
        }

        public bool IsSelectRange(Point point, IDraw item)
        {
            if (item != null)
            {
                GraphicsPath path;
                byte[] pointTypes;
                List<Rectangle> rects;
                Region r;
                Matrix matrix = new Matrix();
                switch (item.Param.DrawType)
                {
                    case DrawType.DrawArc:
                        using (path = new GraphicsPath())
                        {
                            path.AddArc(new Rectangle(item.Param.Points[0].X, item.Param.Points[0].Y, item.Param.Points[1].X, item.Param.Points[1].Y), (float)item.Param.Param[0], (float)item.Param.Param[1]);
                            path.Transform(matrix);
                            return path.IsVisible(point);
                        }
                    case DrawType.DrawCircle:
                        using (path = new GraphicsPath())
                        {
                            path.AddEllipse(new Rectangle(item.Param.Points[0].X, item.Param.Points[0].Y, (int)item.Param.Param[0], (int)item.Param.Param[0]));
                            path.Transform(matrix);
                            return path.IsVisible(point);
                        }
                    case DrawType.DrawClosedCurve:
                        using (path = new GraphicsPath())
                        {
                            path.AddClosedCurve(item.Param.Points.ToArray(), (float)item.Param.Param[0]);
                            path.Transform(matrix);
                            return path.IsVisible(point);
                        }
                    case DrawType.DrawCurve:
                        using (path = new GraphicsPath())
                        {
                            path.AddCurve(item.Param.Points.ToArray(), (float)item.Param.Param[0]);
                            path.Transform(matrix);
                            return path.IsVisible(point);
                        }
                    case DrawType.DrawEllipse:
                        using (path = new GraphicsPath())
                        {
                            path.AddEllipse(new Rectangle(item.Param.Points[0].X, item.Param.Points[0].Y, (int)item.Param.Param[0], (int)item.Param.Param[1]));
                            path.Transform(matrix);
                            return path.IsVisible(point);
                        }
                    case DrawType.DrawImage:
                        if (item.Param.Param[0] != null)
                        {
                            Image img = item.Param.Param[0] as Image;
                            if (img != null)
                            {
                                using (path = new GraphicsPath())
                                {
                                    GraphicsUnit gu = GraphicsUnit.Pixel;
                                    path.AddRectangle(img.GetBounds(ref gu));
                                    return path.IsVisible(point);
                                }
                            }
                        }
                        break;
                    case DrawType.DrawLine:
                        using (path = new GraphicsPath())
                        {
                            path.AddLine(item.Param.Points[0], item.Param.Points[1]);
                            path.Transform(matrix);
                            return path.IsVisible(point);
                        }
                    case DrawType.DrawLines:
                        using (path = new GraphicsPath())
                        {
                            path.AddLines(item.Param.Points.ToArray());
                            path.Transform(matrix);
                            return path.IsVisible(point);
                        }
                    case DrawType.DrawPath:
                        using (path = new GraphicsPath())
                        {
                            pointTypes = new byte[item.Param.Points.Count];
                            for (int i = 0; i < pointTypes.Length; i++)
                            {
                                pointTypes[i] = 0;
                            }
                            path.AddPath(new GraphicsPath(item.Param.Points.ToArray(), pointTypes), false);
                            path.Transform(matrix);
                            return path.IsVisible(point);
                        }
                    case DrawType.DrawPie:
                        using (path = new GraphicsPath())
                        {
                            path.AddPie(new Rectangle(item.Param.Points[0].X, item.Param.Points[0].Y, item.Param.Points[1].X, item.Param.Points[1].Y), (float)item.Param.Param[0], (float)item.Param.Param[1]);
                            path.Transform(matrix);
                            return path.IsVisible(point);
                        }
                    case DrawType.DrawPoint:
                        using (path = new GraphicsPath())
                        {
                            path.AddEllipse(new Rectangle(item.Param.Points[0].X - 1, item.Param.Points[0].Y - 1, 2, 2));
                            path.Transform(matrix);
                            return path.IsVisible(point);
                        }
                    case DrawType.DrawPolygon:
                        using (path = new GraphicsPath())
                        {
                            path.AddPolygon(item.Param.Points.ToArray());
                            path.Transform(matrix);
                            return path.IsVisible(point);
                        }
                    case DrawType.DrawRectangle:
                        using (path = new GraphicsPath())
                        {
                            path.AddRectangle(new Rectangle(item.Param.Points[0].X, item.Param.Points[0].Y, item.Param.Points[1].X, item.Param.Points[1].Y));
                            path.Transform(matrix);
                            return path.IsVisible(point);
                        }
                    case DrawType.DrawRectangles:
                        using (path = new GraphicsPath())
                        {
                            rects = new List<Rectangle>();
                            for (int i = 0; i < item.Param.Points.Count / 2; i++)
                            {
                                rects.Add(new Rectangle(item.Param.Points[i * 2].X, item.Param.Points[i * 2].Y, item.Param.Points[i * 2 + 1].X, item.Param.Points[i * 2 + 1].Y));
                            }
                            path.AddRectangles(rects.ToArray());
                            path.Transform(matrix);
                            return path.IsVisible(point);
                        }
                    case DrawType.DrawString:
                        if (item.Param.Param[0] != null && !string.IsNullOrEmpty(item.Param.Param[0].ToString()))
                        {
                            using (path = new GraphicsPath())
                            {
                                Bitmap bmp = new Bitmap(snapshotSize.Width, snapshotSize.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                                Graphics g = Graphics.FromImage(bmp);
                                SizeF size = g.MeasureString(item.Param.Param[0].ToString(), (Font)item.Param.Param[1], snapshotSize.Width);
                                path.AddRectangle(new Rectangle(item.Param.Points[0], size.ToSize()));
                                return path.IsVisible(point);
                            }
                        }
                        break;
                    case DrawType.FillClosedCurve:
                        using (path = new GraphicsPath())
                        {
                            path.AddClosedCurve(item.Param.Points.ToArray(), (float)item.Param.Param[0]);
                            using (r = new Region(path))
                            {
                                r.Transform(matrix);
                                return r.IsVisible(point);
                            }
                        }
                    case DrawType.FillEllipse:
                        using (path = new GraphicsPath())
                        {
                            path.AddEllipse(new Rectangle(item.Param.Points[0].X, item.Param.Points[0].Y, (int)item.Param.Param[0], (int)item.Param.Param[0]));
                            using (r = new Region(path))
                            {
                                r.Transform(matrix);
                                return r.IsVisible(point);
                            }
                        }
                    case DrawType.FillPath:
                        using (path = new GraphicsPath())
                        {
                            pointTypes = new byte[item.Param.Points.Count];
                            for (int i = 0; i < pointTypes.Length; i++)
                            {
                                pointTypes[i] = 0;
                            }
                            path.AddPath(new GraphicsPath(item.Param.Points.ToArray(), pointTypes), false);
                            using (r = new Region(path))
                            {
                                r.Transform(matrix);
                                return r.IsVisible(point);
                            }
                        }
                    case DrawType.FillPie:
                        using (path = new GraphicsPath())
                        {
                            path.AddPie(new Rectangle(item.Param.Points[0].X, item.Param.Points[0].Y, item.Param.Points[1].X, item.Param.Points[1].Y), (float)item.Param.Param[0], (float)item.Param.Param[1]);
                            using (r = new Region(path))
                            {
                                r.Transform(matrix);
                                return r.IsVisible(point);
                            }
                        }
                    case DrawType.FillPolygon:
                        using (path = new GraphicsPath())
                        {
                            path.AddPolygon(item.Param.Points.ToArray());
                            using (r = new Region(path))
                            {
                                r.Transform(matrix);
                                return r.IsVisible(point);
                            }
                        }
                    case DrawType.FillRectangle:
                        using (path = new GraphicsPath())
                        {
                            path.AddRectangle(new Rectangle(item.Param.Points[0].X, item.Param.Points[0].Y, item.Param.Points[1].X, item.Param.Points[1].Y));
                            using (r = new Region(path))
                            {
                                r.Transform(matrix);
                                return r.IsVisible(point);
                            }
                        }
                    case DrawType.FillRectangles:
                        using (path = new GraphicsPath())
                        {
                            rects = new List<Rectangle>();
                            for (int i = 0; i < item.Param.Points.Count / 2; i++)
                            {
                                rects.Add(new Rectangle(item.Param.Points[i * 2].X, item.Param.Points[i * 2].Y, item.Param.Points[i * 2 + 1].X, item.Param.Points[i * 2 + 1].Y));
                            }
                            path.AddRectangles(rects.ToArray());
                            using (r = new Region(path))
                            {
                                r.Transform(matrix);
                                return r.IsVisible(point);
                            }
                        }
                    case DrawType.FillRegion:
                        using (path = new GraphicsPath())
                        {
                            pointTypes = new byte[item.Param.Points.Count];
                            for (int i = 0; i < pointTypes.Length; i++)
                            {
                                pointTypes[i] = 0;
                            }
                            path.AddPath(new GraphicsPath(item.Param.Points.ToArray(), pointTypes), false);
                            using (r = new Region(path))
                            {
                                r.Transform(matrix);
                                return r.IsVisible(point);
                            }
                        }
                }
            }
            return false;
        }

        public void DrawItem(IDraw item, int colorArgb = 0)
        {
            if (item != null)
            {
                Color c = item.Param.Color;
                if (item.Param.DrawType == DrawType.DrawString)
                    c = ((SolidBrush)item.Param.Param[2]).Color;
                if (colorArgb != 0)
                    c = Color.FromArgb(colorArgb);
                Bitmap bmp = new Bitmap(snapshotSize.Width, snapshotSize.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                Graphics g = Graphics.FromImage(bmp);
                byte[] pointTypes;
                List<Rectangle> rects;
                switch (item.Param.DrawType)
                {
                    case DrawType.DrawArc:
                        g.DrawArc(new Pen(c), item.Param.Points[0].X, item.Param.Points[0].Y, item.Param.Points[1].X, item.Param.Points[1].Y, (int)item.Param.Param[0], (int)item.Param.Param[1]);
                        break;
                    case DrawType.DrawCircle:
                        g.DrawEllipse(new Pen(c), item.Param.Points[0].X, item.Param.Points[0].Y, (int)item.Param.Param[0], (int)item.Param.Param[0]);
                        break;
                    case DrawType.DrawClosedCurve:
                        g.DrawClosedCurve(new Pen(c), item.Param.Points.ToArray());
                        break;
                    case DrawType.DrawCurve:
                        g.DrawCurve(new Pen(c), item.Param.Points.ToArray());
                        break;
                    case DrawType.DrawEllipse:
                        g.DrawEllipse(new Pen(c), item.Param.Points[0].X, item.Param.Points[0].Y, (int)item.Param.Param[0], (int)item.Param.Param[1]);
                        break;
                    case DrawType.DrawImage:
                        if (item.Param.Param[0] != null)
                        {
                            Image img = item.Param.Param[0] as Image;
                            if (img != null)
                            {
                                g.DrawImage(img, item.Param.Points[0]);
                            }
                        }
                        break;
                    case DrawType.DrawLine:
                        g.DrawLine(new Pen(c), item.Param.Points[0], item.Param.Points[1]);
                        break;
                    case DrawType.DrawLines:
                        g.DrawLines(new Pen(c), item.Param.Points.ToArray());
                        break;
                    case DrawType.DrawPath:                        
                        pointTypes = new byte[item.Param.Points.Count];
                        for (int i = 0; i < pointTypes.Length; i++)
                        {
                            pointTypes[i] = 0;
                        }
                        g.DrawPath(new Pen(c), new GraphicsPath(item.Param.Points.ToArray(), pointTypes));
                        break;
                    case DrawType.DrawPie:
                        g.DrawPie(new Pen(c), item.Param.Points[0].X, item.Param.Points[0].Y, item.Param.Points[1].X, item.Param.Points[1].Y, (int)item.Param.Param[0], (int)item.Param.Param[1]);
                        break;
                    case DrawType.DrawPoint:
                        g.DrawEllipse(new Pen(c), item.Param.Points[0].X-1, item.Param.Points[0].Y-1, 2, 2);
                        break;
                    case DrawType.DrawPolygon:
                        g.DrawPolygon(new Pen(c), item.Param.Points.ToArray());
                        break;
                    case DrawType.DrawRectangle:
                        g.DrawRectangle(new Pen(c), new Rectangle(item.Param.Points[0].X, item.Param.Points[0].Y, item.Param.Points[1].X, item.Param.Points[1].Y));
                        break;
                    case DrawType.DrawRectangles:
                        rects = new List<Rectangle>();
                        for (int i = 0; i < item.Param.Points.Count / 2; i++)
                        {
                            rects.Add(new Rectangle(item.Param.Points[i * 2].X, item.Param.Points[i * 2].Y, item.Param.Points[i * 2 + 1].X, item.Param.Points[i * 2 + 1].Y));
                        }
                        g.DrawRectangles(new Pen(c), rects.ToArray());
                        break;
                    case DrawType.DrawString:
                        if (item.Param.Param[0] != null && !string.IsNullOrEmpty(item.Param.Param[0].ToString()))
                        {
                            g.DrawString(item.Param.Param[0].ToString(), (Font)item.Param.Param[1], new SolidBrush(c), item.Param.Points[0]);
                        }
                        break;
                    case DrawType.FillClosedCurve:
                        g.FillClosedCurve(new SolidBrush(c), item.Param.Points.ToArray());
                        break;
                    case DrawType.FillEllipse:
                        g.FillEllipse(new SolidBrush(c), new Rectangle(item.Param.Points[0].X, item.Param.Points[0].Y, item.Param.Points[1].X, item.Param.Points[1].Y));
                        break;
                    case DrawType.FillPath:
                        pointTypes = new byte[item.Param.Points.Count];
                        for (int i = 0; i < pointTypes.Length; i++)
                        {
                            pointTypes[i] = 0;
                        }
                        g.FillPath(new SolidBrush(c), new GraphicsPath(item.Param.Points.ToArray(), pointTypes));
                        break;
                    case DrawType.FillPie:
                        g.FillPie(new SolidBrush(c), new Rectangle(item.Param.Points[0].X, item.Param.Points[0].Y, item.Param.Points[1].X, item.Param.Points[1].Y), (float)item.Param.Param[0], (float)item.Param.Param[1]);
                        break;
                    case DrawType.FillPolygon:
                        g.FillPolygon(new SolidBrush(c), item.Param.Points.ToArray());
                        break;
                    case DrawType.FillRectangle:
                        g.FillRectangle(new SolidBrush(c), new Rectangle(item.Param.Points[0].X, item.Param.Points[0].Y, item.Param.Points[1].X, item.Param.Points[1].Y));
                        break;
                    case DrawType.FillRectangles:
                        rects = new List<Rectangle>();
                        for (int i = 0; i < item.Param.Points.Count / 2; i++)
                        {
                            rects.Add(new Rectangle(item.Param.Points[i * 2].X, item.Param.Points[i * 2].Y, item.Param.Points[i * 2 + 1].X, item.Param.Points[i * 2 + 1].Y));
                        }
                        g.FillRectangles(new SolidBrush(c), rects.ToArray());
                        break;
                    case DrawType.FillRegion:
                        pointTypes = new byte[item.Param.Points.Count];
                        for (int i = 0; i < pointTypes.Length; i++)
                        {
                            pointTypes[i] = 0;
                        }
                        g.FillRegion(new SolidBrush(c), new Region(new GraphicsPath(item.Param.Points.ToArray(), pointTypes)));
                        break;
                }
                DrawToFrameBuffer(bmp);
            }
        }

        public string GetMeasureValue(IDraw item)
        {
            StringBuilder sb = new StringBuilder();
            if (item != null)
            {
                ItemValue iv;
                switch (item.Param.DrawType)
                {
                    case DrawType.DrawArc:
                        break;
                    case DrawType.DrawCircle:
                        break;
                    case DrawType.DrawClosedCurve:
                        break;
                    case DrawType.DrawCurve:
                        break;
                    case DrawType.DrawEllipse:
                        iv = item.Value;
                        sb.AppendLine(iv.Name);
                        EllipseValue ev = iv.Value as EllipseValue;
                        sb.AppendLine("Area:" + ev.Area);
                        sb.AppendLine("Perimeter:" + ev.Perimeter);
                        sb.AppendLine("Angle:" + ev.Angle);
                        break;
                    case DrawType.DrawImage:
                        break;
                    case DrawType.DrawLine:
                        iv = item.Value;
                        sb.AppendLine(iv.Name);
                        LineValue lv = iv.Value as LineValue;
                        sb.AppendLine("Length:" + lv.Length);
                        sb.AppendLine("Slope:" + lv.Slope);
                        break;
                    case DrawType.DrawLines:
                        break;
                    case DrawType.DrawPath:
                        break;
                    case DrawType.DrawPie:
                        break;
                    case DrawType.DrawPoint:
                        break;
                    case DrawType.DrawPolygon:
                        break;
                    case DrawType.DrawRectangle:
                        break;
                    case DrawType.DrawRectangles:
                        break;
                    case DrawType.DrawString:
                        break;
                    case DrawType.FillClosedCurve:
                        break;
                    case DrawType.FillEllipse:
                        break;
                    case DrawType.FillPath:
                        break;
                    case DrawType.FillPie:
                        break;
                    case DrawType.FillPolygon:
                        break;
                    case DrawType.FillRectangle:
                        break;
                    case DrawType.FillRectangles:
                        break;
                    case DrawType.FillRegion:
                        break;
                }
            }
            return sb.ToString();
        }

        public void HighlightItem(IDraw item)
        {
            Color color = SystemColors.Highlight;
            DrawItem(item, color.ToArgb());
        }

        public void RestoreItem(IDraw item)
        {
            DrawItem(item);
        }

        public void ClearDraws()
        {
            m_OverlayMutex.WaitOne();
            try
            {
                drawItems.Clear();
                m_ImageOverlay = new Bitmap(snapshotSize.Width, snapshotSize.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            }
            catch { }
            m_OverlayMutex.ReleaseMutex();
        }

        internal void DrawToFrameBuffer(Image image)
        {
            m_OverlayMutex.WaitOne();
            try
            {
                Graphics.FromImage(m_ImageOverlay).DrawImage(image, Point.Empty);
            }
            catch { }
            finally
            {
                image.Dispose();
            }
            m_OverlayMutex.ReleaseMutex();
        }

        private void DrawToFrameBuffer(IntPtr pFrameBuffer, tSdkFrameHead pFrameHead, Bitmap Image)
        {
            m_OverlayMutex.WaitOne();
            try
            {
                System.Drawing.Imaging.BitmapData Data = Image.LockBits(new Rectangle(0, 0, pFrameHead.iWidth, pFrameHead.iHeight), System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                unsafe
                {
                    int w = pFrameHead.iWidth;
                    int h = pFrameHead.iHeight;
                    byte* dst;
                    byte* src;
                    for (int row = 0; row < h; ++row)
                    {
                        src = (byte*)Data.Scan0 + row * Data.Stride;
                        dst = (byte*)pFrameBuffer + w * (h - 1 - row) * 3;
                        for (int col = 0; col < w; ++col)
                        {
                            if (src[3] != 0)
                            {
                                dst[0] = src[0];
                                dst[1] = src[1];
                                dst[2] = src[2];
                            }
                            dst += 3;
                            src += 4;
                        }
                    }
                }
                Image.UnlockBits(Data);
            }
            catch { }
            m_OverlayMutex.ReleaseMutex();
        }

        [Obsolete("尽量不要用旧方法了", false)]
        public Graphics GetDrawGraphicFromOverlay_Old()
        {
            return Graphics.FromImage(m_ImageOverlay);
        }
        /// <summary>
        /// 记得最后画完后要调用EndDraw()来结束，不然无法显示到Overlay上
        /// </summary>
        /// <returns></returns>
        public Graphics GetDrawGraphicFromOverlay(out Bitmap bmp)
        {
            bmp = null;
            if (!snapshotSize.IsEmpty)
            {
                m_OverlayMutex.WaitOne();
                bmp = new Bitmap(snapshotSize.Width, snapshotSize.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                Graphics g = Graphics.FromImage(bmp);
                return g;
            }
            return null;
        }

        public void EndDraw(Bitmap bmp)
        {
            if (bmp != null)
            {
                DrawToFrameBuffer(bmp);
                m_OverlayMutex.ReleaseMutex();
            }
        }

        /// <summary>
        /// 获取当前视频的帧信息时间间隔（单位：毫秒，默认为1000）
        /// </summary>
        public int RefreshInterval
        {
            get { return this.timer1.Interval; }
            set
            {
                this.timer1.Interval = value;
            }
        }

        public List<IDraw> DrawItems
        {
            get { return drawItems; }
        }

        public Size SnapshotSize
        {
            get { return snapshotSize; }
        }

        public Image SnapshotImage
        {
            get
            {
                m_bSnapshotImage = true;
                while (m_bSnapshotImage) Thread.Sleep(10);
                return (Image)m_ImageSnapshot.Clone();
            }
        }

        internal static MemoryStream GetImageStream(ref tSdkFrameHead tFrameHead, IntPtr pRgbBuffer)
        {
            BITMAPINFOHEADER bmi;
            BITMAPFILEHEADER bmfi;

            bmfi.bfType = ((int)'M' << 8) | ((int)'B');
            bmfi.bfOffBits = 54;
            bmfi.bfSize = (uint)(54 + tFrameHead.iWidth * tFrameHead.iHeight * 3);
            bmfi.bfReserved1 = 0;
            bmfi.bfReserved2 = 0;

            bmi.biBitCount = 24;
            bmi.biClrImportant = 0;
            bmi.biClrUsed = 0;
            bmi.biCompression = 0;
            bmi.biPlanes = 1;
            bmi.biSize = 40;
            bmi.biHeight = tFrameHead.iHeight;
            bmi.biWidth = tFrameHead.iWidth;
            bmi.biXPelsPerMeter = 0;
            bmi.biYPelsPerMeter = 0;
            bmi.biSizeImage = 0;

            MemoryStream stream = new MemoryStream();
            BinaryWriter bw = new BinaryWriter(stream);
            byte[] data = new byte[14];
            IntPtr ptr = Marshal.AllocHGlobal(54);
            Marshal.StructureToPtr((object)bmfi, ptr, false);
            Marshal.Copy(ptr, data, 0, data.Length);
            bw.Write(data);
            data = new byte[40];
            Marshal.StructureToPtr((object)bmi, ptr, false);
            Marshal.Copy(ptr, data, 0, data.Length);
            bw.Write(data);
            data = new byte[tFrameHead.iWidth * tFrameHead.iHeight * 3];
            Marshal.Copy(pRgbBuffer, data, 0, data.Length);
            bw.Write(data);
            Marshal.FreeHGlobal(ptr);

            return stream;
        }

        public emSdkRunMode Status
        {
            get { return status; }
        }

        private void OnCameraParameterChanged(VideoInfoArgs e)
        {
            if (CameraParameterChanged != null)
                CameraParameterChanged(this, e);
        }

        private void OnChangedStatus(VideoArgs e)
        {
            if (ChangedStatus != null)
                ChangedStatus(this, e);
        }

        private void OnRefreshVideoInfo(VideoInfoArgs e)
        {
            if (RefreshVideoInfo != null)
                RefreshVideoInfo(this, e);
        }

        private void OnVideoInitError(VideoInfoArgs e)
        {
            if (VideoInitError != null)
                VideoInitError(this, e);
        }

        private void OnVideoPreviewError(VideoInfoArgs e)
        {
            if (VideoPreviewError != null)
                VideoPreviewError(this, e);
        }

        private void OnVideoSaveImageError(VideoInfoArgs e)
        {
            if (VideoSaveImageError != null)
                VideoSaveImageError(this, e);
        }

        public tSdkCameraDevInfo GetDevice(int index)
        {
            if (tCameraDevInfoList != null)
                return tCameraDevInfoList[index];
            return default(tSdkCameraDevInfo);
        }

        public tSdkCameraDevInfo GetDevice(Predicate<tSdkCameraDevInfo> match)
        {
            if (tCameraDevInfoList != null)
            {
                List<tSdkCameraDevInfo> devices = new List<tSdkCameraDevInfo>(tCameraDevInfoList).FindAll(match);
                if (devices != null && devices.Count > 0)
                {
                    return devices[0];
                }
            }
            return default(tSdkCameraDevInfo);
        }

        public int CustomDataMaxLen
        {
            get
            {
                return customDataMaxLen;
            }
        }

        public int DeviceCount
        {
            get
            {
                return deviceCount;
            }
        }

        private bool InitCamera(int index = 0)
        {
            if (m_hCamera > 0)
            {
                return true;
            }
            if (DeviceCount > 0 && index > -1 && index < DeviceCount)
            {
                if (MvApi.CameraGrabber_Create(out m_ImageBuffer, ref tCameraDevInfoList[index]) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                {
                    MvApi.CameraGrabber_GetCameraHandle(m_ImageBuffer, out m_hCamera);
                    //获得相机特性描述
                    IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(new tSdkCameraCapbility()));
                    MvApi.CameraGetCapability(m_hCamera, ptr);
                    tCameraCapability = (tSdkCameraCapbility)Marshal.PtrToStructure(ptr, typeof(tSdkCameraCapbility));
                    Marshal.FreeHGlobal(ptr);

                    customDataMaxLen = tCameraCapability.iUserDataMaxLen;
                    snapshotSize = new Size(tCameraCapability.sResolutionRange.iWidthMax, tCameraCapability.sResolutionRange.iHeightMax);
                    m_ImageOverlay = new Bitmap(snapshotSize.Width, snapshotSize.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                    //设置抓拍通道的分辨率。
                    tSdkImageResolution tResolution;
                    tResolution.uSkipMode = 0;
                    tResolution.uBinAverageMode = 0;
                    tResolution.uBinSumMode = 0;
                    tResolution.uResampleMask = 0;
                    tResolution.iVOffsetFOV = 0;
                    tResolution.iHOffsetFOV = 0;
                    tResolution.iWidthFOV = tCameraCapability.sResolutionRange.iWidthMax;
                    tResolution.iHeightFOV = tCameraCapability.sResolutionRange.iHeightMax;
                    tResolution.iWidth = tResolution.iWidthFOV;
                    tResolution.iHeight = tResolution.iHeightFOV;
                    //tResolution.iIndex = 0xff;表示自定义分辨率,如果tResolution.iWidth和tResolution.iHeight
                    //定义为0，则表示跟随预览通道的分辨率进行抓拍。抓拍通道的分辨率可以动态更改。
                    //本例中将抓拍分辨率固定为最大分辨率。
                    tResolution.iIndex = 0xff;
                    tResolution.acDescription = new byte[32];//描述信息可以不设置
                    tResolution.iWidthZoomHd = 0;
                    tResolution.iHeightZoomHd = 0;
                    tResolution.iWidthZoomSw = 0;
                    tResolution.iHeightZoomSw = 0;

                    MvApi.CameraSetResolutionForSnap(m_hCamera, ref tResolution);

                    //让SDK来根据相机的型号动态创建该相机的配置窗口。
                    MvApi.CameraCreateSettingPage(m_hCamera, PreviewBox.Handle, tCameraDevInfoList[index].acFriendlyName,/*spm_CallBack*/null,/*m_iSettingPageMsgCallbackCtx*/(IntPtr)null, 0);
                    MvApi.CameraGrabber_SetFrameListener(m_ImageBuffer, m_FrameListener, IntPtr.Zero);
                    MvApi.CameraGrabber_SetSaveImageCompleteCallback(m_ImageBuffer, m_SaveImageComplete, IntPtr.Zero);
                    MvApi.CameraGrabber_SetHWnd(m_ImageBuffer, PreviewBox.Handle);

                    return true;
                }
                else
                {
                    m_hCamera = 0;
                    OnVideoInitError(new VideoInfoArgs("初始化错误"));
                }
            }
            else
            {
                OnVideoInitError(new VideoInfoArgs("没有找到对应的相机，参数index=" + index + "有误，当前在线的最大相机数为" + deviceCount));
            }
            return false;
        }

        private bool InitCamera(Predicate<tSdkCameraDevInfo> match)
        {
            if (m_hCamera > 0)
            {
                return true;
            }
            List<tSdkCameraDevInfo> devices = new List<tSdkCameraDevInfo>(tCameraDevInfoList).FindAll(match);
            if (devices != null && devices.Count > 0)
            {
                tSdkCameraDevInfo device = devices[0]; 
                if (MvApi.CameraGrabber_Create(out m_ImageBuffer, ref device) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                {
                    MvApi.CameraGrabber_GetCameraHandle(m_ImageBuffer, out m_hCamera);
                    //获得相机特性描述
                    IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(new tSdkCameraCapbility()));
                    MvApi.CameraGetCapability(m_hCamera, ptr);
                    tCameraCapability = (tSdkCameraCapbility)Marshal.PtrToStructure(ptr, typeof(tSdkCameraCapbility));
                    Marshal.FreeHGlobal(ptr);

                    customDataMaxLen = tCameraCapability.iUserDataMaxLen;
                    snapshotSize = new Size(tCameraCapability.sResolutionRange.iWidthMax, tCameraCapability.sResolutionRange.iHeightMax);
                    m_ImageOverlay = new Bitmap(snapshotSize.Width, snapshotSize.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                    //设置抓拍通道的分辨率。
                    tSdkImageResolution tResolution;
                    tResolution.uSkipMode = 0;
                    tResolution.uBinAverageMode = 0;
                    tResolution.uBinSumMode = 0;
                    tResolution.uResampleMask = 0;
                    tResolution.iVOffsetFOV = 0;
                    tResolution.iHOffsetFOV = 0;
                    tResolution.iWidthFOV = tCameraCapability.sResolutionRange.iWidthMax;
                    tResolution.iHeightFOV = tCameraCapability.sResolutionRange.iHeightMax;
                    tResolution.iWidth = tResolution.iWidthFOV;
                    tResolution.iHeight = tResolution.iHeightFOV;
                    //tResolution.iIndex = 0xff;表示自定义分辨率,如果tResolution.iWidth和tResolution.iHeight
                    //定义为0，则表示跟随预览通道的分辨率进行抓拍。抓拍通道的分辨率可以动态更改。
                    //本例中将抓拍分辨率固定为最大分辨率。
                    tResolution.iIndex = 0xff;
                    tResolution.acDescription = new byte[32];//描述信息可以不设置
                    tResolution.iWidthZoomHd = 0;
                    tResolution.iHeightZoomHd = 0;
                    tResolution.iWidthZoomSw = 0;
                    tResolution.iHeightZoomSw = 0;

                    MvApi.CameraSetResolutionForSnap(m_hCamera, ref tResolution);

                    //让SDK来根据相机的型号动态创建该相机的配置窗口。
                    MvApi.CameraCreateSettingPage(m_hCamera, PreviewBox.Handle, device.acFriendlyName,/*spm_CallBack*/null,/*m_iSettingPageMsgCallbackCtx*/(IntPtr)null, 0);
                    MvApi.CameraGrabber_SetFrameListener(m_ImageBuffer, m_FrameListener, IntPtr.Zero);
                    MvApi.CameraGrabber_SetSaveImageCompleteCallback(m_ImageBuffer, m_SaveImageComplete, IntPtr.Zero);
                    MvApi.CameraGrabber_SetHWnd(m_ImageBuffer, PreviewBox.Handle);

                    return true;
                }
                else
                {
                    m_hCamera = 0;
                    OnVideoInitError(new VideoInfoArgs("初始化错误"));
                }
            }
            else
            {
                OnVideoInitError(new VideoInfoArgs("没有找到对应的相机"));
            }
            return false;
        }
        /// <summary>
        /// 获取或设置是否输出黑色图像的开关
        /// </summary>
        public bool Monochrome
        {
            get
            {
                uint enabled = 0;
                MvApi.CameraGetMonochrome(m_hCamera, ref enabled);
                return enabled == 1;
            }
            set
            {
                uint enabled = (uint)(value ? 1 : 0);
                MvApi.CameraSetMonochrome(m_hCamera, enabled);
            }
        }
        /// <summary>
        /// 相机昵称区最大长度为32个字节
        /// </summary>
        public string FriendlyName
        {
            get
            {
                string name = string.Empty;
                byte[] nm = new byte[32];
                if (MvApi.CameraGetFriendlyName(m_hCamera, nm) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                    name = Encoding.Default.GetString(nm);
                return name;
            }
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    byte[] nm = Encoding.Default.GetBytes(value);
                    if (nm.Length <= 32)
                        MvApi.CameraSetFriendlyName(m_hCamera, nm);
                }
            }
        }
        /// <summary>
        /// 相机序列号区最大长度为32个字节
        /// </summary>
        public string SN
        {
            get
            {
                byte[] sn_data = new byte[32];
                MvApi.CameraReadSN(m_hCamera, sn_data, 1);
                if (sn_data != null)
                    return Encoding.Default.GetString(sn_data);
                return string.Empty;
            }
            set
            {
                byte[] sn_data = Encoding.Default.GetBytes(value);
                if (sn_data.Length <= 32)
                    MvApi.CameraWriteSN(m_hCamera, sn_data, 1);
            }
        }

        public bool LoadDrawItems(string filepath)
        {
            if (!string.IsNullOrEmpty(filepath) && File.Exists(filepath))
            {
                try
                {
                    BinaryFormatter bf = new BinaryFormatter();
                    using (FileStream fs = new FileStream(filepath, FileMode.Open, FileAccess.Read))
                    {
                        drawItems = (List<IDraw>)bf.Deserialize(fs);
                    }
                    foreach (IDraw item in drawItems)
                    {
                        DrawItem(item);
                    }
                    return true;
                }
                catch { return false; }
            }
            return false;
        }

        public bool SaveDrawItems(string filepath)
        {
            try
            {
                BinaryFormatter bf = new BinaryFormatter();
                using (FileStream fs = new FileStream(filepath, FileMode.Create, FileAccess.ReadWrite))
                {
                    bf.Serialize(fs, drawItems);
                }
                return true;
            }
            catch { return false; }
        }

        public bool LoadDefaultParameter()
        {
            return MvApi.CameraLoadParameter(m_hCamera, (int)emSdkParameterTeam.PARAMETER_TEAM_DEFAULT) == CameraSdkStatus.CAMERA_STATUS_SUCCESS;
        }

        public bool LoadCameraParameter(string filepath, emSdkParameterMode mode = emSdkParameterMode.PARAM_MODE_BY_MODEL, emSdkParameterTeam team = emSdkParameterTeam.PARAMETER_TEAM_A)
        {
            if (!string.IsNullOrEmpty(filepath) && File.Exists(filepath))
            {
                byte[] fp = Encoding.Default.GetBytes(filepath);
                if (MvApi.CameraReadParameterFromFile(m_hCamera, fp) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                {
                    if (MvApi.CameraSetParameterMode(m_hCamera, (int)mode) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                        return MvApi.CameraLoadParameter(m_hCamera, (int)team) == CameraSdkStatus.CAMERA_STATUS_SUCCESS;
                }
            }
            return false;
        }

        public bool SaveCameraParameter(string filepath, emSdkParameterTeam team = emSdkParameterTeam.PARAMETER_TEAM_A)
        {
            byte[] fp = Encoding.Default.GetBytes(filepath);
            if (MvApi.CameraSaveParameter(m_hCamera, (int)team) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                return MvApi.CameraSaveParameterToFile(m_hCamera, fp) == CameraSdkStatus.CAMERA_STATUS_SUCCESS;
            return false;
        }

        public bool SaveCustomData(byte[] data, uint startAddress)
        {
            if (data != null)
            {
                if (data.Length + startAddress <= CustomDataMaxLen)
                {
                    return MvApi.CameraSaveUserData(m_hCamera, startAddress, data, data.Length) == CameraSdkStatus.CAMERA_STATUS_SUCCESS;
                }
            }
            return false;
        }

        public byte[] GetCustomData(uint startAddress, int len)
        {
            if (len + startAddress <= CustomDataMaxLen)
            {
                byte[] data = new byte[len];
                if (MvApi.CameraLoadUserData(m_hCamera, startAddress, data, len) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                    return data;
            }
            return null;
        }

        public bool SelectDeviceToPlay()
        {
            CameraSdkStatus s = MvApi.CameraGrabber_CreateFromDevicePage(out m_ImageBuffer);
            if (s == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
            {
                MvApi.CameraGrabber_GetCameraHandle(m_ImageBuffer, out m_hCamera);

                IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(new tSdkCameraCapbility()));
                MvApi.CameraGetCapability(m_hCamera, ptr);
                tCameraCapability = (tSdkCameraCapbility)Marshal.PtrToStructure(ptr, typeof(tSdkCameraCapbility));
                Marshal.FreeHGlobal(ptr);

                customDataMaxLen = tCameraCapability.iUserDataMaxLen;
                snapshotSize = new Size(tCameraCapability.sResolutionRange.iWidthMax, tCameraCapability.sResolutionRange.iHeightMax);
                m_ImageOverlay = new Bitmap(snapshotSize.Width, snapshotSize.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                MvApi.CameraCreateSettingPage(m_hCamera, parent.Handle, tCameraDevInfoList[0].acFriendlyName, null, (IntPtr)0, 0);
                MvApi.CameraGrabber_SetHWnd(m_ImageBuffer, this.PreviewBox.Handle);

                MvApi.CameraGrabber_SetFrameListener(m_ImageBuffer, m_FrameListener, IntPtr.Zero);

                MvApi.CameraGrabber_SetSaveImageCompleteCallback(m_ImageBuffer, m_SaveImageComplete, IntPtr.Zero);

                MvApi.CameraGrabber_StartLive(m_ImageBuffer);

                loadOver = true;

                return true;
            }
            else if (s == CameraSdkStatus.CAMERA_STATUS_ACCESS_DENY)
            {
                MvApi.CameraGrabber_StartLive(m_ImageBuffer);
                return true;
            }
            return false;
        }

        public emSdkRunMode PlayOrPause(int index)
        {
            if (m_hCamera < 1 || status == emSdkRunMode.RUNMODE_STOP)//还未初始化相机
            {
                if (InitCamera(index))
                {
                    if (MvApi.CameraGrabber_StartLive(m_ImageBuffer) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                    {
                        loadOver = true;
                        status = emSdkRunMode.RUNMODE_PLAY;
                        OnChangedStatus(new VideoArgs(PreviewBox, status));
                    }
                }
                else
                {
                    status = emSdkRunMode.RUNMODE_STOP;
                    OnChangedStatus(new VideoArgs(PreviewBox, status));
                    PreviewBox.Refresh();
                }
            }
            else//已经初始化
            {
                if (status == emSdkRunMode.RUNMODE_PAUSE)
                {
                    if (MvApi.CameraGrabber_StartLive(m_ImageBuffer) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                    {
                        status = emSdkRunMode.RUNMODE_PLAY;
                        OnChangedStatus(new VideoArgs(PreviewBox, status));
                    }
                }
                else
                {
                    if (MvApi.CameraGrabber_StopLive(m_ImageBuffer) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                    {
                        status = emSdkRunMode.RUNMODE_PAUSE;
                        OnChangedStatus(new VideoArgs(PreviewBox, status));
                    }
                }
            }
            return status;
        }

        public emSdkRunMode PlayOrPause(Predicate<tSdkCameraDevInfo> match)
        {
            if (m_hCamera < 1 || status == emSdkRunMode.RUNMODE_STOP)//还未初始化相机
            {
                if (InitCamera(match))
                {
                    if (MvApi.CameraGrabber_StartLive(m_ImageBuffer) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                    {
                        loadOver = true;
                        status = emSdkRunMode.RUNMODE_PLAY;
                        OnChangedStatus(new VideoArgs(PreviewBox, status));
                    }
                }
                else
                {
                    status = emSdkRunMode.RUNMODE_STOP;
                    OnChangedStatus(new VideoArgs(PreviewBox, status));
                    PreviewBox.Refresh();
                }
            }
            else//已经初始化
            {
                if (status == emSdkRunMode.RUNMODE_PAUSE)
                {
                    if (MvApi.CameraGrabber_StartLive(m_ImageBuffer) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                    {
                        status = emSdkRunMode.RUNMODE_PLAY;
                        OnChangedStatus(new VideoArgs(PreviewBox, status));
                    }
                }
                else
                {
                    if (MvApi.CameraGrabber_StopLive(m_ImageBuffer) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                    {
                        status = emSdkRunMode.RUNMODE_PAUSE;
                        OnChangedStatus(new VideoArgs(PreviewBox, status));
                    }
                }
            }
            return status;
        }

        public bool Stop()
        {
            if (MvApi.CameraGrabber_StopLive(m_ImageBuffer) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
            {
                status = emSdkRunMode.RUNMODE_STOP;
                OnChangedStatus(new VideoArgs(PreviewBox, status));
                PreviewBox.Refresh();
                return true;
            }
            return false;
        }

        public bool SetDisplayMode(bool scale = false)
        {
            int iMode = scale ? 0 : 1;
            if (MvApi.CameraSetDisplayMode(m_hCamera, iMode) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
            {
                PreviewBox.Refresh();
                scaleDisplay = scale;
                return true;
            }
            return false;
        }

        public bool StartCapture(string filepath = null)
        {
            string file = string.Empty;
            if (!string.IsNullOrEmpty(filepath) && File.Exists(filepath))
            {
                string path = Path.GetDirectoryName(filepath);
                recordPath = path;
                file = filepath;
            }
            else
            {
                if (!Directory.Exists(recordPath))
                {
                    Directory.CreateDirectory(recordPath);
                }
                file = recordPath + "\\"+ DateTime.Now.ToString("yyyy-MM-dd HH_mm_ss.avi");
            }
            byte[] pcSavePath = Encoding.Default.GetBytes(file);
            if (MvApi.CameraInitRecord(m_hCamera, 1, pcSavePath, 1, 80, 15) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
            {
                recording = true;
                return true;
            }
            return false;
        }

        public bool StopCapture()
        {
            if (MvApi.CameraStopRecord(m_hCamera) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
            {
                recording = false;
                return true;
            }
            return false;
        }

        public bool ShowSettings()
        {
            if (m_hCamera > 0)
            {
                return MvApi.CameraShowSettingPage(m_hCamera, 1) == CameraSdkStatus.CAMERA_STATUS_SUCCESS;
                //1 show ; 0 hide
            }
            return false;
        }

        //默认1秒更新一次视频信息
        private void timer1_Tick(object sender, EventArgs e)
        {
            if (m_ImageBuffer != IntPtr.Zero)
            {
                tSdkGrabberStat stat;
                //获得SDK中图像帧统计信息，捕获帧、错误帧等。
                if (MvApi.CameraGrabber_GetStat(m_ImageBuffer, out stat) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                {
                    string dispFR = Math.Round(stat.DispFps, 2).ToString() + " fps";
                    string capFR = Math.Round(stat.CapFps, 2).ToString() + " fps";
                    string sFrameInfomation = String.Format("| Resolution:{0}x{1} | DispFrameRate:{2} | CapFrameRate:{3} | Lost Frames:{4} | Errors:{5} |", stat.Width, stat.Height, dispFR, capFR, stat.Lost, stat.Error);
                    OnRefreshVideoInfo(new VideoInfoArgs(sFrameInfomation));
                }
            }
            else
            {
                OnRefreshVideoInfo(new VideoInfoArgs(string.Empty));
            }
        }

        //用于分辨率切换时，刷新背景绘图
        //private void timer2_Tick(object sender, EventArgs e)
        //{
        //    //切换分辨率后，擦除一次背景
        //    if (m_bEraseBk == true)
        //    {
        //        m_bEraseBk = false;
        //        PreviewBox.Refresh();
        //    }
        //}

        public void ShowSnapshot()
        {
            if (m_hCamera <= 0)
            {
                return;//相机还未初始化，句柄无效
            }
            if (SnapshotImage != null)
            {
                //更新抓拍显示窗口。
                m_DlgSnapshot.UpdateImage(SnapshotImage);
                m_DlgSnapshot.Show();
            }
        }

        public void SaveImage(string file_path)
        {
            lastSaveFilePath = file_path;
            if (m_ImageBuffer != IntPtr.Zero)
                MvApi.CameraGrabber_SaveImageAsync(m_ImageBuffer);
            //m_bSaveImage = true;//通知预览线程，保存一张图片。也可以参考BtnSnapshot_Click 中抓图方式，重新抓一张图片，然后调用 MvApi.CameraSaveImage 进行图片保存。      
        }

        public void Dispose()
        {
            if (m_hCamera > 0)
            {
                MvApi.CameraGrabber_Destroy(m_ImageBuffer);
                m_hCamera = 0;
            }
        }

        /*相机配置窗口的消息回调函数
        hCamera:当前相机的句柄
        MSG:消息类型，
	    SHEET_MSG_LOAD_PARAM_DEFAULT	= 0,//加载默认参数的按钮被点击，加载默认参数完成后触发该消息,
	    SHEET_MSG_LOAD_PARAM_GROUP		= 1,//切换参数组完成后触发该消息,
	    SHEET_MSG_LOAD_PARAM_FROMFILE	= 2,//加载参数按钮被点击，已从文件中加载相机参数后触发该消息
	    SHEET_MSG_SAVE_PARAM_GROUP		= 3//保存参数按钮被点击，参数保存后触发该消息
	    具体参见CameraDefine.h中emSdkPropSheetMsg类型

        uParam:消息附带的参数，不同的消息，参数意义不同。
	    当 MSG 为 SHEET_MSG_LOAD_PARAM_DEFAULT时，uParam表示被加载成默认参数组的索引号，从0开始，分别对应A,B,C,D四组
	    当 MSG 为 SHEET_MSG_LOAD_PARAM_GROUP时，uParam表示切换后的参数组的索引号，从0开始，分别对应A,B,C,D四组
	    当 MSG 为 SHEET_MSG_LOAD_PARAM_FROMFILE时，uParam表示被文件中参数覆盖的参数组的索引号，从0开始，分别对应A,B,C,D四组
	    当 MSG 为 SHEET_MSG_SAVE_PARAM_GROUP时，uParam表示当前保存的参数组的索引号，从0开始，分别对应A,B,C,D四组
        */
        //protected void SettingPageMsgCalBack(CameraHandle hCamera, uint MSG, uint uParam, uint pContext)
        //{

        //}
    }
}
