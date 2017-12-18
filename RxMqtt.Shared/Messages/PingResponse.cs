
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("RxMqtt.Broker")]
[assembly: InternalsVisibleTo("RxMqtt.Client")]

namespace RxMqtt.Shared.Messages
{
    internal class PingResponse : MqttMessage
    {
        internal PingResponse()
        {
            MsgType = MsgType.PingResponse;
        }
        
        internal override byte[] GetBytes()
        {
            var buffer = new byte[2];
            var index = 0;

            buffer[index++] = ((byte)MsgType.PingResponse << 0x04) | 0x00;

            buffer[index] = 0x00;

            return buffer;
        }
    }
}
