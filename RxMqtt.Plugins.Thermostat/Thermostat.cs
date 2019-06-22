using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using NLog;
using RxMqtt.Client;
using RxMqtt.Shared.Enums;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Plugins.Thermostat.Console
{
    public class Thermostat
    {
        private ILogger _logger = LogManager.GetCurrentClassLogger();

        private int _targetTemp;

        private readonly Temperatures _temperatures = new Temperatures();

        internal static MqttClient _mqttClient;

        private readonly List<IDisposable> _disposables = new List<IDisposable>();

        public Thermostat()
        {
            
        }

        public async Task Initialize()
        {
            _mqttClient = new MqttClient("Thermostat", "172.16.0.247", 1883);

            var status = await _mqttClient.InitializeAsync();

            if (status != Status.Initialized)
            {
                throw new Exception("Could not connect to broker");
            }

            var observable = await _mqttClient.WhenPublishedOn(new [] { "Temperature/#", "Thermostat/#" });

            _disposables.Add(observable.Where(m => m.Topic.Contains("SetTargetTemp")).ObserveOn(Scheduler.Default).Subscribe(SetTargetTemp));
            _disposables.Add(observable.Where(m => m.Topic.StartsWith("Temperature/")).ObserveOn(Scheduler.Default).Subscribe(AddTemp));
            _disposables.Add(observable.Where(m => m.Topic.Contains("SetSystemMode")).ObserveOn(Scheduler.Default).Subscribe(SetSystemMode));
            _disposables.Add(observable.Where(m => m.Topic.Contains("SetFanState")).ObserveOn(Scheduler.Default).Subscribe(SetFanState));
        }

        public void SetTargetTemp(Publish publishMsg)
        {
            var msg = Encoding.UTF8.GetString(publishMsg.Message);

            if (int.TryParse(msg, out var tempResult))
            {
                _targetTemp = tempResult;
            }
        }

        public void SetFanState(Publish publish)
        {

        }

        public void SetSystemMode(Publish publish)
        {

        }

        private void AddTemp(Publish publish)
        {
            var temperatureSource = Encoding.UTF8.GetString(publish.Message);

            if (decimal.TryParse(temperatureSource, out var tempResult))
            {
                UpdateTemperatureReadings(publish.Topic.Split('/').Last(), tempResult);
            }
        }

        public void UpdateTemperatureReadings(string source, decimal temp)
        {
            _temperatures.Add(source, DateTime.Now, temp);
        }
    }

    public enum Fan
    {
        On,
        Auto
    }

    public enum SystemMode
    {
        Heating,
        Cooling,
        Off
    }
}