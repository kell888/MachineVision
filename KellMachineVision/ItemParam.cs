using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing;

namespace KellMachineVision
{
    [Serializable]
    public class ItemParam
    {
        List<Point> points;

        public List<Point> Points
        {
            get { return points; }
        }
        List<object> param;

        public List<object> Param
        {
            get { return param; }
        }
        DrawType drawType;

        public DrawType DrawType
        {
            get { return drawType; }
        }
        Color color;

        public Color Color
        {
            get { return color; }
        }

        public ItemParam(List<Point> points, List<object> param, DrawType drawType, Color color)
        {
            this.points = points;
            this.param = param;
            this.drawType = drawType;
            this.color = color;
        }
    }
}
