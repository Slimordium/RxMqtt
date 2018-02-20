using System.Net.Sockets;

namespace RxMqtt.Broker{

    internal class SendState
    {
        public Socket Socket { get; set; }
    }
}