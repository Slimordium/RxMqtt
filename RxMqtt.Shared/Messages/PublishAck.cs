using System;
using System.Collections.Generic;
using RxMqtt.Shared;
using System.Runtime.CompilerServices;
using NLog;

[assembly: InternalsVisibleTo("RxMqtt.Broker")]
[assembly: InternalsVisibleTo("RxMqtt.Client")]

namespace RxMqtt.Shared.Messages
{
    public class PublishAck : MqttMessage
    {
        internal PublishAck(ushort packetId)
        {
            PacketId = packetId;
            MsgType = MsgType.PublishAck;
        }

        internal PublishAck(byte[] buffer)
        {
            PacketId = BytesToUshort(new [] {buffer[2], buffer[3]});

            MsgType = MsgType.PublishAck;
        }

        internal override byte[] GetBytes()
        {
            var buffer = new List<byte>
            {
                ((byte) MsgType.PublishAck << (byte) MsgOffset.Type) | 0x00,
                0x02
            };

            buffer.AddRange(UshortToBytes(PacketId));

            return buffer.ToArray();
        }
    }
}
