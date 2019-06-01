using System.Threading;

namespace RxMqtt.Broker.Console
{
    class Program
    {
        static void Main(string[] args)
        {
            var cts = new CancellationTokenSource();

            var broker = new MqttBroker();
            broker.StartListening(cts.Token);

            System.Console.ReadLine();
            cts.Cancel();
        }
    }
}
 