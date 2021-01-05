using System;
using System.Configuration;
using System.IO;
using System.Threading.Tasks;

namespace OSMScraper
{
    internal class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine(@"Scraper start...");
            const string initialScrapeUrl = "https://srh.bankofchina.com/search/whpj/searchen.jsp";

            string scrapeDirectory;
            try
            {
                scrapeDirectory = ConfigurationManager.AppSettings.Get("ScrapeDirectory");

                if (scrapeDirectory.Length == 0)
                {
                    Console.WriteLine($@"Directory path not specified in app.config file.");
                    return;
                }

                if (!Directory.Exists(scrapeDirectory))
                    Directory.CreateDirectory(scrapeDirectory);
            }
            catch (DirectoryNotFoundException e)
            {
                Console.WriteLine(e.Message);
                return;
            }
            
            var startDate = DateTime.UtcNow.AddDays(-2).ToString("yyyy-MM-dd");
            var endDate = DateTime.UtcNow.ToString("yyyy-MM-dd");

            var scraper = new Scraper(startDate, endDate, initialScrapeUrl, scrapeDirectory);
            
            var currencies = await scraper.ScrapeAllCurrencies();
            if (currencies == null || currencies.Count == 0)
            {
                Console.WriteLine(@"Failed to retrieve any currencies.");
                return;
            }

            var pageInfos  = await scraper.InitialPagesScrape(currencies);
            if (pageInfos == null || pageInfos.Count == 0)
            {
                Console.WriteLine(@"Failed to retrieve any page for any currency.");
                return;
            } 
            
            pageInfos = await scraper.ScrapeAllPages(pageInfos);
            foreach (var pageInfo in pageInfos)
            {
                scraper.WriteAllPagesForSingleCurrency(pageInfo);
            }
            
            Console.WriteLine(@"Scraper finished.");
        }
    }
}