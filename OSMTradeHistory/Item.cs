using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace OSMTradeHistory
{
    public class Item
    {
        public DateTime TradeDateTimeGmt { get; set; }
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public double Volume { get; set; }
        [JsonIgnore]
        [JsonProperty(Required = Required.Default)] 
        public double MovingAverage { get; set; }
    }
}