using System;
using System.Threading.Tasks;

namespace RxMqtt.Client.Console
{

    class Program{
        private static MqttClient _mqttClient;

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

            _mqttClient = new MqttClient(clientId.Trim(), ip.Trim(), 1883); // "172.16.0.244"

            _mqttClient.InitializeAsync().Wait();

            while (true)
            {
                System.Console.WriteLine("s => subscribe");
                System.Console.WriteLine("u => unsubscribe");
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

                    _mqttClient.Subscribe(Handler, line.Trim());

                    System.Console.WriteLine("subscribed");
                }

                if (!line.StartsWith("p"))
                    continue;

                System.Console.WriteLine("Publish to topic:");
                var topic = System.Console.ReadLine();

                System.Console.WriteLine("Message to publish:");
                var msg = System.Console.ReadLine();

                System.Console.WriteLine("Publish count (1):");
                var count = System.Console.ReadLine();

                if (string.IsNullOrEmpty(count))
                    count = "1";

                Publish(msg, topic, count).Wait();//.ConfigureAwait(false);
                
                

                System.Console.WriteLine("published");
            }
        }

        private static async Task Publish(string msg, string topic, string count)
        {
            for (var i = 1; i <= Convert.ToInt32(count); i++)
            {
                try
                {
                    var r = await _mqttClient.PublishAsync(msg, topic);
                    System.Console.WriteLine($"Done {r}");
                }
                catch (Exception e)
                {
                    System.Console.WriteLine(e.Message);
                }
                
            }
        }

        private static void Handler(string s)
        {
            System.Console.WriteLine($"In => {s}");
        }
    }
}
