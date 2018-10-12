using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UvmCoreLib
{
    public class UvmJsonModule
    {
        public static Dictionary<string, string> libContent =
      new Dictionary<string, string>
      {
      { "Dumps", "dumps" },
      { "Loads", "loads" }
      };

        private UvmMap<object> jsonToMap(JObject jobj)
        {
            if(jobj == null)
            {
                return null;
            }
            else
            {
                var result = UvmMap<object>.Create();
                foreach(var p in jobj)
                {
                    var item = p.Value;
                    object value = null;
                    if(item == null)
                    {
                        value = null;
                    }
                    else if(item is JObject && item.ToString().StartsWith("{"))
                    {
                        value = jsonToMap(item as JObject);
                    }
                    else if(item is JObject && item.ToString().StartsWith("["))
                    {
                        value = jsonToArray(item as JObject);
                    }
                    else
                    {
                        value = item;
                    }
                    result.Set(p.Key, value);
                }
                return result;
            }
        }

        private UvmArray<object> jsonToArray(JObject jobj)
        {
            if (jobj == null)
            {
                return null;
            }
            else
            {
                var result = UvmArray<object>.Create();
                for (var i= 0;i< jobj.Count;i++)
                {
                    var item = jobj[i];
                    object value = null;
                    if (item == null)
                    {
                        value = null;
                    }
                    else if (item is JObject && item.ToString().StartsWith("{"))
                    {
                        value = jsonToMap(item as JObject);
                    }
                    else if (item is JObject && item.ToString().StartsWith("["))
                    {
                        value = jsonToArray(item as JObject);
                    }
                    else
                    {
                        value = item;
                    }
                    result.Add(value);
                }
                return result;
            }
        }

        public object Loads(string jsonStr)
        {
            if (jsonStr == null || jsonStr.Length < 1)
            {
                return null;
            }
            if (jsonStr[0] == '{')
            {
                // loads to UvmMap
                JObject jobj = JObject.Parse(jsonStr);
                var result = jsonToMap(jobj);
                return result;
            }
            else if (jsonStr[0] == '[')
            {
                // loads to UvmArray
                JObject jobj = JObject.Parse(jsonStr);
                var result = jsonToArray(jobj);
                return result;
            }
            else
            {
                return JsonConvert.DeserializeObject(jsonStr);
            }
        }
        public string Dumps(object value)
        {
            return JsonConvert.SerializeObject(value);
        }
    }
}
