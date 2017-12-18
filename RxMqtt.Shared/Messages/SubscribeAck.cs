
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("RxMqtt.Broker")]
[assembly: InternalsVisibleTo("RxMqtt.Client")]

namespace RxMqtt.Shared.Messages
{
    internal class SubscribeAck : MqttMessage
    {
        public SubscribeAck(ushort packetId)
        {
            MsgType = MsgType.SubscribeAck;
            PacketId = packetId;
        }

        internal override byte[] GetBytes()
        {
            var buffer = new byte[4];

            buffer[0] = ((byte)MsgType.SubscribeAck << (byte)MsgOffset.Type) | 0x00;
            buffer[1] = 0x02;
            var bytes = UshortToBytes(PacketId);
            buffer[2] = bytes[0];
            buffer[3] = bytes[1];

            return buffer;
        }
    }
}
