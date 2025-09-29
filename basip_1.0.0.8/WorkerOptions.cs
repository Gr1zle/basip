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
        public string db_config { get; set; }
        public string timestart { get; set; }
        public string timeout { get; set; }
        public bool run_now  { get; set; }
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
