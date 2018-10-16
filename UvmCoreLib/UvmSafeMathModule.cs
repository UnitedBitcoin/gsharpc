using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using static UvmCoreLib.UvmCoreFuncs;


namespace UvmCoreLib
{

    public abstract class UvmBigInt
    {
        protected Object _o;

    }


    class UvmBigIntImpl : UvmBigInt
    {
        public UvmBigIntImpl(Object o){
            _o = o;
        }
    }
    public class UvmSafeMathModule
    {
        public static Dictionary<string, string> libContent =
      new Dictionary<string, string>
      {
      { "bigint", "bigint" },
      { "add", "add" },

      { "sub", "sub" },
      { "mul", "mul" },
      { "div", "div" },
      { "pow", "pow" },
      { "rem", "rem" },

      { "tohex", "tohex" },
      { "toint", "toint" },
      { "tostring", "tostring" },
      { "gt", "gt" },
      { "ge", "ge" },
      { "lt", "lt" },
      { "le", "le" },
      { "eq", "eq" },
      { "ne", "ne" },
      { "max", "max" },
      { "min", "min" }
     
      };

        public UvmBigInt bigint(Object value) { return (UvmBigInt)new UvmBigIntImpl(value); }

        public UvmBigInt add(UvmBigInt value1, UvmBigInt value2)
        {
            return new UvmBigIntImpl(tointeger(value1) + tointeger(value2));
        }
        public UvmBigInt sub(UvmBigInt value1, UvmBigInt value2)
        {
            return new UvmBigIntImpl(tointeger(value1) - tointeger(value2));
        }
        public UvmBigInt mul(UvmBigInt value1, UvmBigInt value2)
        {
            return new UvmBigIntImpl(tointeger(value1) * tointeger(value2));
        }
        public UvmBigInt div(UvmBigInt value1, UvmBigInt value2)
        {
            return new UvmBigIntImpl(tointeger(value1) / tointeger(value2));
        }
        public UvmBigInt pow(UvmBigInt value1, UvmBigInt value2)
        {
            return new UvmBigIntImpl(Math.Pow(tointeger(value1), tointeger(value1)));
        }
        public UvmBigInt rem(UvmBigInt value1, UvmBigInt value2)
        {
            return new UvmBigIntImpl(tointeger(value1) % tointeger(value1));
        }

        public String tohex(UvmBigInt value)
        {
            return tostring(value);
        }
        public long toint(UvmBigInt value)
        {
            return tointeger(value);
        }
        public String tostring(UvmBigInt value)
        {
            return tostring(value);
        }


        public bool gt(UvmBigInt value1, UvmBigInt value2)
        {
            return tointeger(value1) > tointeger(value2);
        }
        public bool ge(UvmBigInt value1, UvmBigInt value2)
        {
            return tointeger(value1) >= tointeger(value2);
        }

        public bool lt(UvmBigInt value1, UvmBigInt value2)
        {
            return tointeger(value1) < tointeger(value2);
        }
        public bool le(UvmBigInt value1, UvmBigInt value2)
        {
            return tointeger(value1) <= tointeger(value2);
        }

        public bool eq(UvmBigInt value1, UvmBigInt value2)
        {
            return tointeger(value1) == tointeger(value2);
        }
        public bool ne(UvmBigInt value1, UvmBigInt value2)
        {
            return tointeger(value1) != tointeger(value2);
        }

        public UvmBigInt max(UvmBigInt value1, UvmBigInt value2)
        {
            long v1 = tointeger(value1);
            long v2 = tointeger(value2);
            return v1 > v2 ? value1 : value2;
        }
        public UvmBigInt min(UvmBigInt value1, UvmBigInt value2)
        {
            long v1 = tointeger(value1);
            long v2 = tointeger(value2);
            return v1 < v2 ? value1 : value2;
        }

    }
}



