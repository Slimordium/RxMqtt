
using System.Runtime.CompilerServices;
using RxMqtt.Shared.Enums;

[assembly: InternalsVisibleTo("RxMqtt.Broker")]
[assembly: InternalsVisibleTo("RxMqtt.Client")]

namespace RxMqtt.Shared.Messages
{
    internal class PingMsg : MqttMessage
    {
        internal PingMsg()
        {
            MsgType = MsgType.PingRequest;
            PacketId = GetNextPacketId();
        }
        
        internal override byte[] GetBytes()
        {
            return new byte[] { ((byte)MsgType.PingRequest << (byte)MsgOffset.Type) | 0x00, 0x00 };
        }
    }
}
