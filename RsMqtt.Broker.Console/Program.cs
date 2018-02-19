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


            var publish = new Publish("test", Encoding.UTF8.GetBytes("ajkdafhadfhjkjdf halkjshtest test test tsajkdafhadfhjkjdf halkjshtest test test tst test test test test test test test st test test testst test teest  test test st test test testst test teest  test test st test test testst test teest test test test tst test test test test test test test st testt test test test test test test test st test test testst test teest  test test st test test testst test teest  test test st test test testst test teest test test test tst test test test test test test test st test"));


            var bytes = publish.GetBytes();

            var publishReply = new Publish(bytes);

            //var pc = new Subscribe(new [] {"test"});

            //var b = pc.GetBytes();

            //var pcs = new Subscribe(b);


            var newsd = new PublishAck(publishReply.PacketId);

            var asdfasdf = newsd.GetBytes();

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
