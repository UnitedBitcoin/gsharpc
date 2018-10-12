using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UvmCoreLib
{
    public class UvmMathModule
    {
        public static Dictionary<string, string> libContent =
       new Dictionary<string, string>
       {
      { "Abs", "abs" },
      { "Tointeger", "tointeger" },
      { "Floor", "floor" },
      { "Max", "max" },
      { "Min", "min" },
      { "Type", "type" },
       };

        public double Abs(double value)
        {
            return Math.Abs(value);
        }

        public int Abs(int value)
        {
            return Math.Abs(value);
        }

        public int? Tointeger(object value)
        {
            if (value == null)
            {
                return null;
            }
            int result = 0;
            if (!int.TryParse(value.ToString(), out result))
            {
                return null;
            }
            return result;
        }

        public long Floor(double value)
        {
            return (long)value;
        }

        public long Floor(long value)
        {
            return value;
        }

        public double Max(double a1, double a2)
        {
            return Math.Max(a1, a2);
        }

        public long Max(long a1, long a2)
        {
            return Math.Max(a1, a2);
        }

        public double Min(double a1, double a2)
        {
            return Math.Min(a1, a2);
        }

        public long Min(long a1, long a2)
        {
            return Math.Min(a1, a2);
        }

        public string Type(object value)
        {
            return (value is int) ? "int" : "number";
        }

        public readonly double pi = Math.PI;

        public readonly long maxinteger = long.MaxValue;

        public readonly long mininteger = long.MinValue;

    }
}
