using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RxMqtt.Client.Console
{
    class Program
    {
        static void Main(string[] args)
        {
            var client = new MqttClient("test", "172.16.0.244", 1883, 60);

            client.InitializeAsync().Wait();



            System.Console.ReadLine();

        }
    }
}
