using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UvmCoreLib
{
    /**
     * Array和Map的父类
     */
    public class UvmTable
    {
        protected IList<object> _items;
        protected Dictionary<object, object> _hashitems;
        public UvmTable()
        {
            _items = new List<object>();
            _hashitems = new Dictionary<object, object>();
        }
    }

    /**
     * uvm中Array类型的模拟
     */
    public class UvmArray<T> : UvmTable
    {
        public static UvmArray<T> Create()
        {
            return new UvmArray<T>();
        }
        public void Add(T value)
        {
            _items.Add(value);
        }
        public void Pop()
        {
            if (_items.Count > 0)
            {
                _items.RemoveAt(_items.Count - 1);
            }
        }
        public T Get(int index)
        {
            if (index >= 1 && index <= _items.Count)
            {
                return (T)_items[index - 1];
            }
            else if (_hashitems.ContainsKey(index))
            {
                return (T)_hashitems[index];
            }
            else
            {
                return default(T);
            }
        }

        public int Count()
        {
            return _items.Count;
        }

        public void Set(int index, object value)
        {
            if (index >= 1 && index <= _items.Count)
            {
                _items[index - 1] = value;
                if (value == null && index == _items.Count)
                {
                    _items.RemoveAt(_items.Count - 1);
                }
            }
            else if (index == _items.Count + 1)
            {
                if (value != null)
                {
                    _items.Add(value);
                }
            }
            else
            {
                if (value != null)
                {
                    _hashitems[index] = value;
                }
                else
                {
                    _hashitems.Remove(index);
                }
            }
        }

        public delegate KeyValuePair<object, T> ArrayIterator(UvmArray<T> map, object key);

        public ArrayIterator Ipairs()
        {
            return (UvmArray<T> map, object key) =>
            {
                var foundKey = false;
                object nextKey = null;
                T nextValue = default(T);
                for (var k = 1; k <= map.Count(); k++)
                {
                    if (key == null)
                    {
                        nextKey = k;
                        nextValue = (T)map._items[k - 1];
                        break;
                    }
                    if (!foundKey && key is int && (k == (int)key))
                    {
                        foundKey = true;
                    }
                    else if (foundKey)
                    {
                        nextKey = k;
                        nextValue = (T)map._items[k - 1];
                        break;
                    }
                }
                return new KeyValuePair<object, T>(nextKey, nextValue);
            };
        }
    }

    public class UvmMap<T> : UvmTable
    {
        private UvmMap()
        {
        }

        public static UvmMap<T> Create()
        {
            return new UvmMap<T>();
        }

        public void Set(string key, T value)
        {
            if (value == null)
            {
                _hashitems.Remove(key);
            }
            else
            {
                _hashitems[key] = value;
            }
        }
        public T Get(string key)
        {
            if (_hashitems.ContainsKey(key))
            {
                return (T)_hashitems[key];
            }
            else
            {
                return default(T);
            }
        }

        public delegate KeyValuePair<string, T> MapIterator(UvmMap<T> map, string key);

        public MapIterator Pairs()
        {
            return (UvmMap<T> map, string key) =>
            {
                var foundKey = false;
                string nextKey = null;
                T nextValue = default(T);
                foreach (var k in map._hashitems.Keys)
                {
                    if (key == null)
                    {
                        nextKey = (string)k;
                        nextValue = (T)map._hashitems[k];
                        break;
                    }
                    if (!foundKey && (string)k == key)
                    {
                        foundKey = true;
                    }
                    else if (foundKey)
                    {
                        nextKey = (string)k;
                        nextValue = (T)map._hashitems[k];
                        break;
                    }
                }
                return new KeyValuePair<string, T>(nextKey, nextValue);
            };
        }
    }
}