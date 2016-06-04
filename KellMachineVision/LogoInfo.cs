using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing;

namespace KellMachineVision
{
    [Serializable]
    public class LogoInfo
    {
        string logoText;

        public string LogoText
        {
            get { return logoText; }
            set { logoText = value; }
        }
        Size textSize;

        public Size TextSize
        {
            get { return textSize; }
            set { textSize = value; }
        }
        Rectangle showRect;

        public Rectangle ShowRect
        {
            get { return showRect; }
            set { showRect = value; }
        }
        Color textColor;

        public Color TextColor
        {
            get { return textColor; }
            set { textColor = value; }
        }

        public LogoInfo(string logoText, Size textSize, Rectangle showRect, Color textColor)
        {
            this.logoText = logoText;
            this.textSize = textSize;
            this.showRect = showRect;
            this.textColor = textColor;
        }
    }
}
