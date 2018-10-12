using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UvmCoreLib
{
    public class UvmTimeModule
    {
        public static Dictionary<string, string> libContent =
       new Dictionary<string, string>
       {
      { "Add", "add" },
      { "Tostr", "tostr" },
      { "Difftime", "difftime" }
       };

        private static DateTime UnixTimeStampToDateTime(long unixTimeStamp)
        {
            System.DateTime dtDateTime = new System.DateTime(1970, 1, 1, 0, 0, 0, 0);
            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp);
            return dtDateTime;
        }

        private static long DateTimeToUnixTimeStamp(DateTime time)
        {
            System.DateTime dtDateTime = new System.DateTime(1970, 1, 1, 0, 0, 0, 0);
            return (long)((time - dtDateTime).TotalSeconds);
        }

        public long Add(long timestamp, string field, long offset)
        {
            var date = UnixTimeStampToDateTime(timestamp);
            switch (field)
            {
                case "year":
                    date = date.AddYears((int)offset);
                    break;
                case "month":
                    date = date.AddMonths((int)offset);
                    break;
                case "day":
                    date = date.AddDays((int)offset);
                    break;
                case "hour":
                    date = date.AddHours((int)offset);
                    break;
                case "minute":
                    date = date.AddMinutes((int)offset);
                    break;
                case "second":
                    date = date.AddSeconds((int)offset);
                    break;
                default: throw new Exception("not support time field " + field);
            }
            return DateTimeToUnixTimeStamp(date);
        }

        public string Tostr(long timestamp)
        {
            var date = UnixTimeStampToDateTime(timestamp);
            return date.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public long Difftime(long timestamp1, long timestamp2)
        {
            return timestamp1 - timestamp2;
        }

    }
}
