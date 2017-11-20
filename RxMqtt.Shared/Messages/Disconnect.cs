
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
            return new byte[] { ((byte)Shared.MsgType.Disconnect << (byte)MsgOffset.Type) | 0x00, 0x00 };
        }
    }
}
