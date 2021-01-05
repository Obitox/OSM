using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Animation;
using Newtonsoft.Json;

namespace OSMTradeHistory
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private static MainViewModel _mainViewModel;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly string _username;
        private readonly string _password;
        
        public MainWindow()
        {
            InitializeComponent();
            _mainViewModel = new MainViewModel();
            DataContext = _mainViewModel;
            SizeToContent = SizeToContent.WidthAndHeight; 
            _username = ConfigurationManager.AppSettings["Username"];
            _password = ConfigurationManager.AppSettings["Password"];
        }

        private async void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_username) || string.IsNullOrEmpty(_password))
            {
                LblStatus.Content = "Invalid credentials in the App.config file.";
                return;
            }

            if (string.IsNullOrEmpty(_mainViewModel.SearchCriteria))
            {
                LblStatus.Content = "Search input can't be empty.";
                return;
            }
            
            if(!TblHistoryListView.Items.IsEmpty)
                TblHistoryListView.ItemsSource = new List<Item>();
            
            try
            {
                _cancellationTokenSource = new CancellationTokenSource();
                LblStatus.Content = "Searching...";

                const int days = 100;
                var url =
                    $"https://webservice.gvsi.com/query/json/GetDaily/tradedatetimegmt/open/high/low/close/volume?pricesymbol=\"{_mainViewModel.SearchCriteria}\"&daysBack={days}"; 
                var response = await GetAsync(url, _username, _password, _cancellationTokenSource.Token);

                if (response.Results == null)
                {
                    _mainViewModel.SearchCriteria = "";
                    return;
                }
                
                response.Results.Items = CalculateMovingAverage(response.Results.Items, 10);
                TblHistoryListView.ItemsSource = response.Results.Items;
                
                LblStatus.Content = "Complete";
                _mainViewModel.SearchCriteria = "";
            }
            catch (OperationCanceledException)
            {
                _cancellationTokenSource.Dispose();
                LblStatus.Content = "Request cancelled.";
                _mainViewModel.SearchCriteria = "";
            }
            
        }

        private IList<Item> CalculateMovingAverage(IList<Item> items, int period)
        {
            for (var i = 0; i < items.Count(); i++)
            {
                if (i < period - 1) continue;
                double sum = 0;
                for (var j = i; j > (i - period); j--)
                    sum += items.ElementAt(j).Close;
                
                var average = sum / period;
                items.ElementAt(i).MovingAverage = average;

            }
            return items;
        }
        
        private async Task<Response> GetAsync(string url, string username, string password, CancellationToken cancellationToken)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            
            request.AllowReadStreamBuffering = false;
            request.ContentType = "application/json";
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Credentials = new NetworkCredential(username, password);
            request.PreAuthenticate = true;
            
            HttpWebResponse response;
            try
            {
                using (response = await request.GetResponseAsync() as HttpWebResponse)
                    if (response != null)
                        using (var stream = response.GetResponseStream())
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                stream?.Dispose();
                                response.Dispose();
                                cancellationToken.ThrowIfCancellationRequested();
                            }
                            
                            if(response.StatusCode == HttpStatusCode.OK)
                                return DeserializeJsonFromStream<Response>(stream);
                        }
            }
            catch (WebException e)
            {
                if (e.Status == WebExceptionStatus.ProtocolError)
                {
                    response = e.Response as HttpWebResponse;
                    if (response != null && response.StatusCode == HttpStatusCode.BadRequest)
                    {
                        response.Dispose();
                        LblStatus.Content = $"Invalid price symbol of {_mainViewModel.SearchCriteria} supplied.";
                        _mainViewModel.SearchCriteria = "";
                        return new Response();
                    }
                        
                }
                LblStatus.Content = e.Message;
                _mainViewModel.SearchCriteria = "";
                return new Response();
            }

            return new Response();
        }
        
        private T DeserializeJsonFromStream<T>(Stream stream)
        {
            if (stream == null || stream.CanRead == false)
                return default;

            using (var sr = new StreamReader(stream))
            using (var jtr = new JsonTextReader(sr))
            {
                var js = new JsonSerializer();
                var searchResult = js.Deserialize<T>(jtr);
                return searchResult;
            }
        }

        private void BtnCancelSearch_Click(object sender, RoutedEventArgs e)
        {
            if(!_cancellationTokenSource.IsCancellationRequested)
                _cancellationTokenSource.Cancel();
        }
    }
}