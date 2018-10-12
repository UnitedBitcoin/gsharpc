using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UvmCoreLib
{
    public class UvmStringModule
    {
        public static Dictionary<string, string> libContent =
       new Dictionary<string, string>{
     {"Byte", "byte" },
     {"Char", "char" },
     { "Dump", "dump" },
    { "Find", "find" },
    { "Format", "format" },
    { "Gmatch", "gmatch" },
    { "Gsub", "gsub" },
    { "Split", "split" },
    { "Len", "len" },
    { "Lower", "lower" },
    { "Match", "match" },
    { "Rep", "rep" },
    { "Reverse", "reverse" },
    { "Sub", "sub" },
    { "Upper", "upper" },
    { "Pack", "pack" },
    { "Packsize", "packsize" },
    { "Unpack", "unpack" }
        };

        public int Len(string str)
        {
            return str != null ? str.Length : 0;
        }
        public int Byte(string str)
        {
            if (str == null || str.Length < 1)
            {
                return 0;
            }
            return (int)str[0];
        }

        public string Char(int i)
        {
            return "" + (char)i;
        }

        public string Dump(object toDumpFunction, bool strip)
        {
            return "mock of dump function";
        }

        public int? Find(string text, string pattern, int init = 0, bool plain = false)
        {
            if (text == null || pattern == null)
            {
                return null;
            }
            if (plain)
            {
                if (!text.Contains(pattern))
                {
                    return null;
                }
                var index = text.IndexOf(pattern);
                if (index < init)
                {
                    return null;
                }
                return index;
            }
            else
            {
                // pattern 是包含类似 . %s %d %s %w %x 等模式
                throw new Exception("暂时没提供C#中模式字符串库的mock"); // TODO
            }
        }

        public string Format(string format, object arg1)
        {
            // TODO: 暂时不支持多参数format
            return string.Format(format, arg1);
        }

        public delegate KeyValuePair<object, object> IteratorFunc(object coll, object key);

        public IteratorFunc Gmatch(string text, string pattern)
        {
            throw new Exception("暂时不支持C#中模式字符串库的mock"); // TODO
        }

        public string Gsub(string src, string pattern, string replacer, int? n = null)
        {
            throw new Exception("暂时不支持C#中模式字符串库的mock"); // TODO
        }

        public UvmArray<string> Split(string str, string sep)
        {
            var result = UvmArray<string>.Create();
            if (str == null || sep == null)
            {
                return result;
            }
            var splited = str.Split(sep[0]);
            for (var i = 0; i < splited.Length; i++)
            {
                result.Add(splited[i]);
            }
            return result;
        }

        public string Lower(string text)
        {
            return text.ToLower();
        }

        public string Match()
        {
            throw new Exception("暂时不支持C#中模式字符串库的mock"); // TODO
        }

        // 返回str字符串重复n次的结果，间隔符是字符串sep
        public string Rep(string str, int n, string sep = "")
        {
            var result = new StringBuilder();
            if (str == null || n < 1)
            {
                return result.ToString();
            }
            for (int i = 0; i < n; i++)
            {
                if (i > 0)
                {
                    if (sep != null)
                    {
                        result.Append(sep);
                    }
                }
                result.Append(str);
            }
            return result.ToString();
        }

        public string Reverse(string text)
        {
            if (text == null)
            {
                return null;
            }
            var result = new StringBuilder();
            for (var i = 0; i < text.Length; i++)
            {
                result.Append(text[text.Length - 1 - i]);
            }
            return result.ToString();
        }

        // 获取str字符串的子字符串，从第i个字符开始，到第j个字符结束（包含第i和第j个字符），i和j可以是负数，表示从str反方向开始的第-i/-j个字符
        public string Sub(string str, int i, int j = -1)
        {
            if (j >= 0)
            {
                return str.Substring(i, j - i);
            }
            else
            {
                return Reverse(str).Substring(i, -j - i);
            }
        }

        public string Upper(string str)
        {
            return str.ToUpper();
        }

        public string Pack(string str)
        {
            throw new Exception("暂时不提供此函数在C#中的mock"); // TODO
        }

        public string Packsize(string str)
        {
            throw new Exception("暂时不提供此函数在C#中的mock"); // TODO
        }

        public object Unpack(string format, string data)
        {
            throw new Exception("暂时不提供此函数在C#中的mock"); // TODO
        }
    }
}
