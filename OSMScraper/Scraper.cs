using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace OSMScraper
{
    public class Scraper
        {
            private string StartDate { get; }
            private string EndDate { get; }
            private string Url { get;  }
            private string ScrapeDirectory { get; }
    
            public Scraper(string startDate, string endDate, string url, string scrapeDirectory)
            {
                StartDate = startDate;
                EndDate = endDate;
                Url = url;
                ScrapeDirectory = scrapeDirectory;
            }
    
            // Fetch all currencies available
            public async Task<List<string>> ScrapeAllCurrencies()
            {
                Console.WriteLine(@"Scraping all currencies...");
                try
                {
                    var request = (HttpWebRequest)WebRequest.Create(Url);
                    var doc = new HtmlDocument();
                    using (var response = await request.GetResponseAsync() as HttpWebResponse)
                        if (response != null)
                            using (var stream = response.GetResponseStream())
                            using (var sr = new StreamReader(stream))
                            {
                                var contentString = await sr.ReadToEndAsync();
                                doc.LoadHtml(contentString);
                            }
                        else
                            return null;
                    
                    
                    var currencies = doc.DocumentNode
                                                    .SelectNodes("//select/option")
                                                    .Skip(1)
                                                    .Select(node => node.InnerHtml)
                                                    .ToList();
                                    
                    // Workaround 
                    // In the dropdown on https://srh.bankofchina.com/search/whpj/searchen.jsp there is duplicate IDR currency, when first value from the dropdown is queried, it doesn't query IDR but rather TWD      
                    var idrIndex = currencies.FindIndex(currency => currency == "IDR");
                    currencies[idrIndex] = "TWD";
        
                    Console.WriteLine("Currencies retrieved from scrape: " + "\n\t" + string.Join("\n\t", currencies)); 
                    return currencies;
                }
                catch (WebException e)
                {
                    Console.WriteLine(e);
                    return null;
                }
            }
    
            // For each currency fetch num of pages to scrape, retry in case page fails
            public async Task<List<PageInfo>> InitialPagesScrape(List<string> currencies)
            {
                var semaphoreSlim = new SemaphoreSlim(
                    initialCount: 10,
                    maxCount: 10);
                var pageInfos = new ConcurrentBag<PageInfo>();
    
                var counter = 0;
                var tasks = currencies.Select(async currency =>
                {
                    await semaphoreSlim.WaitAsync();
                
                    try
                    {
                        var pageInfo = await ScrapeFirstPageForEachCurrency($"{Url}?erectDate={StartDate}&nothing={EndDate}&pjname={currency}", currency);
                        if(pageInfo.PageCount > 0)
                            pageInfos.Add(pageInfo);
                        counter++;
                        Console.WriteLine($@"Scraping first page for each currency: {currency} Pages {counter} / {currencies.Count}");
                    }
                    finally
                    {
                        semaphoreSlim.Release();
                    }
                });
                
                await Task.WhenAll(tasks);
                Console.WriteLine($"Currencies with data {pageInfos.Count} / {currencies.Count}: " + "\n\t" + string.Join("\n\t", pageInfos.Select(pageInfo => pageInfo.Currency))); 
                return pageInfos.ToList();
            }
    
            private async Task<PageInfo> ScrapeFirstPageForEachCurrency(string url, string currency)
            {
                _step1:

                try
                {
                    var web = new HtmlWeb();
                    var doc = await web.LoadFromWebAsync(url);
                
                    // Header row can be predefined
                    var headerRow = doc.DocumentNode
                        .SelectNodes("//tr/td")
                        .Where(node => node.HasClass("lan12_hover"))
                        .Select(node => $"\"{node.InnerHtml}\"")
                        .Prepend("\"Page\"")
                        .ToArray();
                
                    var table = doc.DocumentNode
                        .SelectNodes("//tr/td")
                        .Where(node => node.HasClass("hui12_20"))
                        .Select(node => node.InnerHtml)
                        .ToArray();

                    if (table.First() == "sorry, no records！")
                        return new PageInfo() {Currency = currency, PageCount = 0, RecordCount = 0};
    
                    if (table.First() != "soryy,wrong search word submit,please check your search word!")
                    {
                        const string recordCountPattern = @"var m_nRecordCount = [0-9]{1,}";
                        var matchedRecordCountString = Regex.Match(doc.Text, recordCountPattern).Value;
                        var recordCount = double.Parse(new string(matchedRecordCountString.Where(char.IsDigit).ToArray()));
                    
                        const string perPageCountPattern = @"var m_nPageSize = [0-9]{1,}";
                        var matchedPerPageCountString = Regex.Match(doc.Text, perPageCountPattern).Value;
                        var perPageCount = double.Parse(new string(matchedPerPageCountString.Where(char.IsDigit).ToArray()));
                    
                        var pageCount = (int) Math.Ceiling(recordCount / perPageCount);
                    
                        // Header row append
                        var scrapeTxt = $"{ScrapeDirectory}/{currency}_{StartDate}_{EndDate}.txt";
                        File.WriteAllText(scrapeTxt, string.Join(",", headerRow));
    
                        return new PageInfo() {Currency = currency, PageCount = pageCount, RecordCount = (int) recordCount, Pages = new List<Page>()};
                    }
                
                    goto _step1;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    return new PageInfo() {Currency = currency, PageCount = 0, RecordCount = 0};
                }
            }
    
            public async Task<List<PageInfo>> ScrapeAllPages(List<PageInfo> pageInfos)
            {
                var scrapedPageInfos = new List<PageInfo>();
                var counter = 0;
                foreach (var pageInfo in pageInfos)
                {
                    Console.WriteLine($@"Scraping all pages for currency: {pageInfo.Currency}");
                    var scrapedPageInfo = await ScrapeAllPagesBasedOnPageInfo(pageInfo);
                    Console.WriteLine($@"Scraping all pages for currency: {pageInfo.Currency} complete");
                    scrapedPageInfos.Add(scrapedPageInfo);
                    counter++;
                    Console.WriteLine($@"Currencies {counter} / {pageInfos.Count}");
                }
                
                return scrapedPageInfos;
            }
    
            private async Task<PageInfo> ScrapeAllPagesBasedOnPageInfo(PageInfo pageInfo)
            {
                var semaphoreSlim = new SemaphoreSlim(
                    10,
                    10);
    
                var tasks = Enumerable.Range(1, pageInfo.PageCount).Select( async i =>
                {
                    await semaphoreSlim.WaitAsync();
    
                    try
                    {
                            var page = await ScrapeEachPage(
                                $"{Url}?erectDate={StartDate}&nothing={EndDate}&pjname={pageInfo.Currency}&page={i}", i);
                            if(page.Id != 0)
                                pageInfo.Pages.Add(page);
                            Console.WriteLine($@"Currency {pageInfo.Currency} Pages {pageInfo.Pages.Count} / {pageInfo.PageCount}");
                    }
                    finally
                    {
                        semaphoreSlim.Release();
                    }
                });
                await Task.WhenAll(tasks);
                pageInfo.Pages = new List<Page>(pageInfo.Pages.OrderBy(page => page.Id));
                return pageInfo; 
            }
    
    
            private async Task<Page> ScrapeEachPage(string url, int pageNum)
            {
                _step1:
                try
                {
                    var request = (HttpWebRequest)WebRequest.Create(url);
                    var doc = new HtmlDocument();
                    using (var response = await request.GetResponseAsync() as HttpWebResponse)
                        if (response != null)
                            using (var stream = response.GetResponseStream())
                            using (var sr = new StreamReader(stream))
                            {
                                var contentString = await sr.ReadToEndAsync();
                                doc.LoadHtml(contentString);
                            }
                        else
                        {
                            Console.WriteLine($@"Scraping page on url: {url} failed.");
                            return new Page() { Id = 0 };
                        }
                    

                    var table = doc.DocumentNode
                        .SelectNodes("//tr/td")
                        .Where(node => node.HasClass("hui12_20"))
                        .Select(node => $"\"{node.InnerHtml}\"")
                        .ToArray();
    
                    if (table.First() != "\"soryy,wrong search word submit,please check your search word!\"")
                    {
                        for (var i = 0; i < table.Length; i++)
                        {
                            // Prepend page number to first row
                            if (i == 0) table[0] = $"\"{pageNum}\"," + table[0];
                            if (i <= 0 || i % 7 != 0) continue;
                            // Append new line with page number to every other row
                            table[i - 1] += $"\n\"{pageNum}\"";
                        }
                    
                        var content = "\n" + string.Join(",", table);
                        return new Page()
                        {
                            Id = pageNum,
                            Content = content
                        };
                    }
                    
                    goto _step1;
                }
                catch (WebException e)
                {
                    Console.WriteLine(e.Message);
                    return new Page() {Id = 0};
                }
            }
    
            public void WriteAllPagesForSingleCurrency(PageInfo pageInfo)
            {
                var scrapeTxt = $"{ScrapeDirectory}/{pageInfo.Currency}_{StartDate}_{EndDate}.txt";
                Console.WriteLine($@"Writing pages for currency: {pageInfo.Currency}");
                foreach (var page in pageInfo.Pages)
                {
                    File.AppendAllText(scrapeTxt, page.Content);
                    Console.WriteLine($@"Page {page.Id} complete");
                }
                Console.WriteLine($@"Writing pages for currency: {pageInfo.Currency} complete");
            }
        }
}