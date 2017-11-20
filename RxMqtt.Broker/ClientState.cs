using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using NLog;

namespace RxMqtt.Broker
{
    internal class ClientState
    {
        internal string ClientId { get; set; }
        internal byte[] Buffer { get; set; } = new byte[128000];
        internal Socket Socket { get; set; }
    }
}