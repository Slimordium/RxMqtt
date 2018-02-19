
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

            buffer.Add(0x03); //Remaining length

            buffer.AddRange(UshortToBytes(PacketId)); //Always 2 bytes

            buffer.Add(0x01); //QOS 1

            return buffer.ToArray();
        }
    }
}
