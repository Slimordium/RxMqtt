using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RxMqtt.Broker;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Broker.Console
{
    class Program
    {
        static void Main(string[] args)
        {
            //var asdf = MqttMessage.EncodeValue(202300);

            //var ddd = MqttMessage.DecodeValue(asdf);

            //var publish = new Publish("test", Encoding.UTF8.GetBytes("1test1"));


            //var bytes = publish.GetBytes();

            //var publishReply = new Publish(bytes);


            var cts = new CancellationTokenSource();

            Task.Factory.StartNew(() =>
                {
                    var broker = new MqttBroker();
                    broker.StartListening(cts.Token);
                }
                , TaskCreationOptions.LongRunning);

            System.Console.ReadLine();
            cts.Cancel();
        }
    }
}
