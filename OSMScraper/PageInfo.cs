using System.Collections.Generic;

namespace OSMScraper
{
    public class PageInfo
    {
        public string Currency { get; set; }
        public int RecordCount { get; set; }
        public int PageCount { get; set; }
        public List<Page> Pages { get; set; }
    }
}