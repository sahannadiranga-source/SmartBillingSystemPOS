using System;
using System.Collections.Generic;

namespace POSGardenia.Models
{
    public enum KitchenTicketKind
    {
        New,       // first kitchen items of this bill
        AddOn,     // more kitchen items for a bill that already sent some
        Reprint    // full list again (paper jam, lost ticket)
    }

    public class KitchenTicketLine
    {
        public string Name { get; set; } = "";
        public decimal Quantity { get; set; }
    }

    // One kitchen order ticket (KOT).
    public class KitchenTicket
    {
        // Running number of the day (1, 2, 3 ...); 0 = none (test print, reprint).
        public int KotNo { get; set; }

        // Reprint only: the numbers of the tickets this list was originally sent on, e.g. "0003, 0007".
        public string OriginalKots { get; set; } = "";

        public string BillNo { get; set; } = "";
        public string TableName { get; set; } = "";
        public DateTime PrintedAt { get; set; }
        public KitchenTicketKind Kind { get; set; }
        public List<KitchenTicketLine> Lines { get; set; } = new();
    }
}
