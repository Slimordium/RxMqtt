
using System.Runtime.CompilerServices;
using RxMqtt.Shared.Enums;

[assembly: InternalsVisibleTo("RxMqtt.Broker")]
[assembly: InternalsVisibleTo("RxMqtt.Client")]

namespace RxMqtt.Shared.Messages
{
    internal class DisconnectMsg : MqttMessage
    {
        internal DisconnectMsg()
        {
            MsgType = MsgType.Disconnect;
        }

        internal override byte[] GetBytes()
        {
            return new byte[] { ((byte)MsgType.Disconnect << (byte)MsgOffset.Type) | 0x00, 0x00 };
        }
    }
}
