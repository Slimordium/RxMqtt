using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RxMqtt.Shared;

namespace RxMqtt.Client.Console
{
    class Program
    {
        static void Main(string[] args)
        {
            var client = new MqttClient("test", "172.16.0.244", 1883, 60);

            client.InitializeAsync().Wait();

            client.SubscribeAsync(new Subscription(Handler, "sensor_data/humidity"));

            client.PublishAsync("HTU21D_humidity_outside,119.00", "sensor_data/humidity").Wait();

            System.Console.ReadLine();

        }

        private static Task Handler(object o, string s)
        {
            throw new NotImplementedException();
        }
    }
}
