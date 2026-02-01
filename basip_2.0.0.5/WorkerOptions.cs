using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Basip
{
    public class WorkerOptions
    {
        public class LogsResponse
        {
            public ListOption list_option { get; set; }
            public List<LogItem> list_items { get; set; }
        }

        public class ListOption
        {
            public Pagination pagination { get; set; }
            public Filter filter { get; set; }
            public Locale locale { get; set; }
            public Sort sort { get; set; }
        }

        public class Pagination
        {
            public int total_pages { get; set; }
            public int items_limit { get; set; }
            public int total_items { get; set; }
            public int current_page { get; set; }
        }

        public class Filter
        {
            public bool available_filtering { get; set; }
            public List<AvailableField> available_fields { get; set; }
            public AvailableValues available_values { get; set; }
        }

        public class AvailableField
        {
            public List<string> available_types { get; set; }
            public string filter_field { get; set; }
        }

        public class AvailableValues
        {
            public List<string> category { get; set; }
            public List<string> priority { get; set; }
            public List<NameValue> name { get; set; }
        }

        public class NameValue
        {
            public string text { get; set; }
            public string key { get; set; }
        }

        public class Locale
        {
            public List<string> available_locales { get; set; }
            public string locale { get; set; }
        }

        public class Sort
        {
            public bool asc { get; set; }
            public List<string> available_values { get; set; }
        }

        public class LogItem
        {
            public long timestamp { get; set; }
            public string category { get; set; }
            public string priority { get; set; }
            public LogInfo info { get; set; }
            public LogName name { get; set; }

            public DateTime EventTime => DateTimeOffset.FromUnixTimeMilliseconds(timestamp).DateTime;
        }

        public class LogInfo
        {
            public string text { get; set; }
            public Dictionary<string, object> model { get; set; }
        }

        public class LogName
        {
            public string text { get; set; }
            public string key { get; set; }
        }
        public string db_config { get; set; }
        public string timestart { get; set; }
        public string timeout { get; set; }
        public bool run_now  { get; set; }
        public bool clear_log { get; set; }
        public int time_wait_http { get; set; }
        public int format_card_uid { get; set; }
        public string uidtransform(string id_card)
        {
            string idcard = "";
            switch (format_card_uid)
            {
                case 0:
                    string cardid = Convert.ToInt64(id_card.ToString(), 16).ToString();
                    idcard = string.Concat(Enumerable.Repeat('0', 10 - cardid.Length)) + cardid;
                    break;
                case 2:
                    idcard = id_card;
                    break;
            }
            return idcard;
        }
    }
}
