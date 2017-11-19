using RxMqtt.Shared;
namespace RxMqtt.Shared.Messages
{
    internal class PublishAckMsg : MqttMessage
    {
        internal PublishAckMsg(int messageId)
        {
            PacketId = (ushort)messageId;
            MsgType = MsgType.PublishAck;
        }

        internal override byte[] GetBytes()
        {
            var buffer = new byte[4];

            buffer[0] = ((byte)MsgType.PublishAck << (byte)MsgOffset.Type) | 0x00;//PublishAck
            buffer[1] = 0x02;
            var bytes = UshortToBytes(PacketId);
            buffer[2] = bytes[0];
            buffer[3] = bytes[1];

            return buffer;
        }
    }
}
