using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RxMqtt.Shared;

namespace RxMqtt.Client.Console
{

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

    class Program
    {
        static void Main(string[] args)
        {
            var defaultClientId = $"client-{DateTime.Now.Hour}.{DateTime.Now.Minute}.{DateTime.Now.Millisecond}";

            System.Console.WriteLine($"ClientId ({defaultClientId}): ");

            var clientId = System.Console.ReadLine();

            if (string.IsNullOrEmpty(clientId))
                clientId = defaultClientId;

            System.Console.WriteLine("Ip address (127.0.0.1): ");

            var ip = System.Console.ReadLine();

            if (string.IsNullOrEmpty(ip))
                ip = "127.0.0.1";

            var client = new MqttClient(clientId.Trim(), ip.Trim(), 1883, 60); // "172.16.0.244"

            client.InitializeAsync().Wait();

            while (true)
            {
                System.Console.WriteLine("s => subscribe");
                System.Console.WriteLine("p => publish");
                System.Console.WriteLine("q => quit");

                var line = System.Console.ReadLine();

                if (string.IsNullOrEmpty(line))
                    continue;

                if (line.StartsWith("q"))
                    return;

                if (line.StartsWith("s"))
                {
                    System.Console.WriteLine("Topic:");
                    line = System.Console.ReadLine();

                    if (string.IsNullOrEmpty(line))
                        continue;

                    client.SubscribeAsync(new Subscription(Handler, line.Trim()));

                    System.Console.WriteLine("subscribed");
                }

                if (!line.StartsWith("p"))
                    continue;

                System.Console.WriteLine("Publish to topic:");
                var topic = System.Console.ReadLine();

                System.Console.WriteLine("Message to publish:");
                var msg = System.Console.ReadLine();

                client.PublishAsync(msg, topic);

                System.Console.WriteLine("published");
            }
        }

        private static Task Handler(object o, string s)
        {
            return Task.Run((() =>
            {
                System.Console.WriteLine($"In => '{s}'");
            }));
        }
    }
}
