using System;
using System.Collections.Generic;
using System.Text;

namespace KellMachineVision
{
    [Serializable]
    public class EllipseValue : IValue
    {
        double area;

        public double Area
        {
            get { return area; }
        }
        double perimeter;

        public double Perimeter
        {
            get { return perimeter; }
        }
        double angle;

        public double Angle
        {
            get { return angle; }
        }

        public EllipseValue(double area, double perimeter, double angle)
        {
            this.area = area;
            this.perimeter = perimeter;
            this.angle = angle;
        }

        public static bool operator ==(EllipseValue a, EllipseValue b)
        {
            return a.area == b.area && a.angle == b.angle;
        }

        public static bool operator !=(EllipseValue a, EllipseValue b)
        {
            return !(a==b);
        }

        public override bool Equals(object obj)
        {
            EllipseValue other = obj as EllipseValue;
            if (other != null)
                return this == other;
            return false;
        }

        public override int GetHashCode()
        {
            return this.GetHashCode();
        }
    }
}