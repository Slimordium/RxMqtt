using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;

namespace RxMqtt.Plugins.Thermostat.Console
{
    public class Temperatures : Dictionary<string, Dictionary<DateTime, decimal>>
    {
        public decimal GetAverage(string source)
        {
            return GetAverage(source, DateTime.Now - TimeSpan.FromSeconds(30), DateTime.Now);
        }

        public decimal GetAverage()
        {
            var sources = new List<decimal>();

            foreach (var temperature in this)
            {
                sources.Add(temperature.Value.Average(s => s.Value));
            }

            return sources.Average();
        }

        public decimal GetAverage(string source, DateTime start, DateTime end)
        {
            var temps = this[source].Where(s => s.Key >= start && s.Key <= end);

            var avg = temps.Average(t => t.Value);

            return avg;
        }

        public void Add(string source, DateTime dateTime, decimal temp)
        {
            if (ContainsKey(source))
            {
                this[source].Add(dateTime, temp);
            }
            else
            {
                Add(source, new Dictionary<DateTime, decimal>());
                this[source].Add(dateTime, temp);
            }
        }
    }
}