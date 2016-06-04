using System;
using System.Collections.Generic;
using System.Text;

namespace KellMachineVision
{
    [Serializable]
    public class LineValue : IValue
    {
        double length;

        public double Length
        {
            get { return length; }
        }
        double slope;

        public double Slope
        {
            get { return slope; }
        }

        public LineValue(double length, double slope)
        {
            this.length = length;
            this.slope = slope;
        }

        public static bool operator ==(LineValue a, LineValue b)
        {
            return a.length == b.length && a.slope == b.slope;
        }

        public static bool operator !=(LineValue a, LineValue b)
        {
            return !(a==b);
        }

        public override bool Equals(object obj)
        {
            LineValue other = obj as LineValue;
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