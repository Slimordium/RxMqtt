
using System;
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

        public SubscribeAck(byte[] buffer)
        {
            MsgType = MsgType.SubscribeAck;

            PacketId = BitConverter.ToUInt16(buffer, 2);
        }

        internal override byte[] GetBytes()
        {
            var buffer = new List<byte> {((byte) MsgType.SubscribeAck << (byte) MsgOffset.Type) | 0x00};

            var encodeValue = EncodeValue(PacketId);

            buffer.Add((byte)encodeValue.Length); //Remaining length

            buffer.AddRange(encodeValue);

            return buffer.ToArray();
        }
    }
}
