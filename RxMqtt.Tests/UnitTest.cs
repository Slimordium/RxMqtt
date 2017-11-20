using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RxMqtt.Broker;
using RxMqtt.Client;
using RxMqtt.Shared;

namespace AwsIotTests
{
    [TestClass]
    public class UnitTest
    {
     
        [TestMethod]
        public async Task TestPublish()
        {
            var client = new MqttClient("test", "172.16.0.244", 1883, 60);

            await client.InitializeAsync();

            await client.SubscribeAsync(new Subscription(Handler, "Test" ));

            await client.PublishAsync("test", "test");

            Console.ReadLine();
        }

        private Task Handler(object o, string s)
        {
            return Task.Factory.StartNew(() =>
            {



            });
        }

        [TestMethod]
        public void TestListen()
        {
            var cts = new CancellationTokenSource();

            var t = new Thread(() =>
                {

                    var broker = new MqttBroker();
                    broker.StartListening(cts.Token);

                })
                {IsBackground = true};

            t.Start();

            Console.ReadLine();
            cts.Cancel();



        }
    }
}
