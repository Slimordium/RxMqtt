using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RxMqtt.Shared;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Tests
{
    [TestClass]
    public class UnitTest
    {
     
        [TestMethod]
        public void TestEncodeValue()
        {
            var asdf = MqttMessage.EncodeValue(100);
        }

        [TestMethod]
        public void TestDecodeValue()
        {
        }
    }
}
