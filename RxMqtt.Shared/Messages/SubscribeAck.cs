
using System.Collections.Generic;
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

        public SubscribeAck(IReadOnlyList<byte> buffer)
        {
            MsgType = MsgType.SubscribeAck;

            PacketId = BytesToUshort(new[] { buffer[2], buffer[3] });
        }

        internal override byte[] GetBytes()
        {
            var buffer = new List<byte> {((byte) MsgType.SubscribeAck << (byte) MsgOffset.Type) | 0x00};

            buffer.AddRange(UshortToBytes(PacketId));

            return buffer.ToArray();
        }
    }
}
