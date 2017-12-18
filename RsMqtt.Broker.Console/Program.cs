using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RxMqtt.Broker;

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
