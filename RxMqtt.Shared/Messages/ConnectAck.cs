

namespace RxMqtt.Shared.Messages
{
    internal class ConnectAck : MqttMessage
    {
        private const byte Accepted = 0x00;

        public ConnectAck()
        {
            MsgType = MsgType.ConnectAck;
        }

        internal override byte[] GetBytes() 
        {
            var index = 0;
            var buffer = new byte[4];

            buffer[index++] = (byte)(((byte)MsgType << 0x04) | 0x00); 

            index = GetRemainingLength(2, buffer, index);

            buffer[index++] = 0x00;

            buffer[index] = Accepted; //or 0x02, rejected?

            return buffer;
        }
    }
}
