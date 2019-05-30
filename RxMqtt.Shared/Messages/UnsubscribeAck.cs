
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RxMqtt.Shared.Enums;

[assembly: InternalsVisibleTo("RxMqtt.Broker")]
[assembly: InternalsVisibleTo("RxMqtt.Client")]

namespace RxMqtt.Shared.Messages
{
    internal class UnsubscribeAck : MqttMessage
    {
        public UnsubscribeAck(ushort packetId)
        {
            MsgType = MsgType.UnsubscribeAck;
            PacketId = packetId;
        }

        public UnsubscribeAck(IReadOnlyList<byte> buffer)
        {
            MsgType = MsgType.UnsubscribeAck;

            PacketId = BytesToUshort(new[] { buffer[2], buffer[3] });
        }

        internal override byte[] GetBytes()
        {
            var buffer = new List<byte> {((byte) MsgType.UnsubscribeAck << (byte) MsgOffset.Type) | 0x00};

            buffer.Add(0x03); //Remaining length

            buffer.AddRange(UshortToBytes(PacketId)); //Always 2 bytes

            buffer.Add(0x01); //QOS 1

            return buffer.ToArray();
        }
    }
}
