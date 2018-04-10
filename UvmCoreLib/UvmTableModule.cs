using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UvmCoreLib
{
    public class UvmTableModule
    {
        public static Dictionary<string, string> libContent =
       new Dictionary<string, string>
       {
     { "Concat", "concat" },
    { "Length", "length" },
    { "Insert", "insert" },
    { "Append", "append" },
    { "Remove", "remove" },
    { "Sort", "sort" }
       };

        public string Concat(UvmArray<string> col, string sep)
        {
            var result = new StringBuilder();
            if (col == null)
            {
                return result.ToString();
            }
            for (int i = 0; i < col.Count(); i++)
            {
                if (i > 0)
                {
                    if (sep != null)
                    {
                        result.Append(sep);
                    }
                }
                result.Append(col.Get(i + 1));
            }
            return result.ToString();
        }

        public int Length<T>(UvmArray<T> table)
        {
            return table.Count();
        }

        public void Insert<T>(UvmArray<T> col, int pos, T value)
        {
            if (col == null)
            {
                return;
            }
            if (pos > col.Count() || pos < 1)
            {
                col.Set(pos, value);
            }
            else
            {
                // 插入中间，要把pos后位置的值向后移动一位，再把value放入pos位置
                for (int i = col.Count(); i > pos; --i)
                {
                    col.Set(i + 1, col.Get(i));
                }
                col.Set(pos, value);
            }
        }

        public void Append<T>(UvmArray<T> col, T value)
        {
            col.Add(value);
        }

        public void Remove<T>(UvmArray<T> col, int pos)
        {
            col.Set(pos, null);
        }

        public void Sort<T>(UvmArray<T> col)
        {
            if (col == null || col.Count() < 2)
            {
                return;
            }
            // 快排
            int pivot = 1;
            T pivotValue = col.Get(pivot);
            var lessItems = new UvmArray<T>();
            var greaterItems = new UvmArray<T>();
            for (int i = 1; i <= col.Count(); i++)
            {
                if (i == pivot)
                {
                    continue;
                }
                var item = col.Get(i);
                if (pivotValue == null)
                {
                    greaterItems.Add(item);
                }
                else if (item == null)
                {
                    lessItems.Add(item);
                }
                else if (string.Compare(item.ToString(), pivotValue.ToString()) < 0)
                {
                    lessItems.Add(item);
                }
                else
                {
                    greaterItems.Add(item);
                }
            }
            this.Sort(lessItems);
            this.Sort(greaterItems);
            var result = new UvmArray<T>();
            for (var i = 1; i <= lessItems.Count(); i++)
            {
                result.Add(lessItems.Get(i));
            }
            result.Add(pivotValue);
            for (var i = 1; i <= greaterItems.Count(); i++)
            {
                result.Add(greaterItems.Get(i));
            }
            for (var i = 1; i <= result.Count(); i++)
            {
                col.Set(i, result.Get(i));
            }
        }

    }
}
