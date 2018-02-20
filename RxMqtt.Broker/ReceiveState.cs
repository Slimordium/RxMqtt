using System;
using System.Net.Sockets;

namespace RxMqtt.Broker{
    class ReceiveState
    {
        public Socket Socket { get; set; }
        public byte[] Buffer { get; set; } = new byte[300000];

        public Action<byte[]> Callback { get; set; }
    }
}