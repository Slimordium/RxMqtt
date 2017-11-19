
namespace RxMqtt.Shared.Messages
{
    internal class PingMsg : MqttMessage
    {
        private const byte MsgPingreqFlagBits = 0x00;

        internal PingMsg()
        {
            MsgType = MsgType.PingRequest;
        }

        
        internal override byte[] GetBytes()
        {
            return new byte[] { ((byte)MsgType.PingRequest << (byte)MsgOffset.Type) | MsgPingreqFlagBits, 0x00 };
        }
    }
}
