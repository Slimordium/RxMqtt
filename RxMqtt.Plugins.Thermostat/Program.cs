using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RxMqtt.Client;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Plugins.Thermostat.Console
{
    class Program
    {
        

        static async Task Main(string[] args)
        {
            System.Console.CancelKeyPress += Console_CancelKeyPress;

            var defaultClientId = $"Thermostat-{DateTime.Now.Hour}.{DateTime.Now.Minute}.{DateTime.Now.Millisecond}";
            var defaultIp = "172.16.0.247";

            foreach (var arg in args)
            {
                if (arg.Contains("clientid"))
                {
                    defaultClientId = arg.Split('=')[1];
                }
                if (arg.Contains("broker"))
                {
                    defaultIp = arg.Split('=')[1];
                }
            }

            System.Console.WriteLine($"ClientId ({defaultClientId}): ");

            var clientId = System.Console.ReadLine();

            if (string.IsNullOrEmpty(clientId))
                clientId = defaultClientId;

            System.Console.WriteLine($"Ip address ({defaultIp}): ");

            var ip = System.Console.ReadLine();

            if (string.IsNullOrEmpty(ip))
                ip = "172.16.0.247";

            _mqttClient = new MqttClient(clientId.Trim(), ip.Trim(), 1883, 5); // "172.16.0.247"

            var s = await _mqttClient.InitializeAsync();

            System.Console.WriteLine($"MQTT => {s}");

            if (s != Shared.Enums.Status.Initialized)
            {
                System.Console.WriteLine("Error connecting...");
                return;
            }

            while (true)
            {
                System.Console.WriteLine("s => set target temp");
                System.Console.WriteLine("m => set drift margin - if temperature drifts outside of this range, either cool or heat");
                System.Console.WriteLine("g => get current temperatures");
                System.Console.WriteLine("q => quit");

                var line = System.Console.ReadLine();

                if (string.IsNullOrEmpty(line))
                    continue;

                if (line.StartsWith("q"))
                {
                    _mqttClient?.Dispose();
                    return;
                }

                var observable = await _mqttClient.WhenPublishedOn("Temperature/#");
                _subscriptions.Add("Temperature/#", observable.ObserveOn(Scheduler.Default).Subscribe(Callback));

                System.Console.WriteLine("subscribed");

            }
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs args)
        {
            args.Cancel = true; //Prevents ctrl+c from exiting console
            System.Console.WriteLine("Canceling...");
            _mre.Set();
        }

        private static void Callback(Publish publishedMessage)
        {
            System.Console.WriteLine($"To topic: '{publishedMessage.Topic}' => '{Encoding.UTF8.GetString(publishedMessage.Message)}'");
        }

        /// <summary>
        /// Throws timeout exception after 2 seconds
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="topic"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        private static async Task Publish(string msg, string topic, string count, string delay)
        {
            var sw = new Stopwatch();
            sw.Start();

            int parsedCount;

            if (!int.TryParse(count, out parsedCount))
            {
                parsedCount = 1;
            }

            int parsedDelay;

            if (!int.TryParse(delay, out parsedDelay))
            {
                parsedDelay = 50;
            }

            for (var i = 0; i < parsedCount; i++)
            {
                try
                {
                    var r = await _mqttClient.PublishAsync(msg, topic);

                    if (_mre.IsSet)
                    {
                        sw.Stop();

                        System.Console.WriteLine($"Publish canceled after publishing {i} in {sw.Elapsed}  @ {i / sw.Elapsed.TotalSeconds} per second");
                        _mre.Reset();
                        return;
                    }

                    if (parsedDelay > 0)
                    {
                        await Task.Delay(parsedDelay);
                    }
                }
                catch (Exception e)
                {
                    System.Console.WriteLine(e.Message);
                    return;
                }
            };

            sw.Stop();

            System.Console.WriteLine($"Publish completed {count} in {sw.Elapsed} @ {parsedCount / sw.Elapsed.TotalSeconds} per second");
        }
    }
}
