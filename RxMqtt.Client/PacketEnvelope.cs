using RxMqtt.Shared.Enums;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Client{
    internal class PacketEnvelope
    {
        public MsgType MsgType { get; set; }

        public MqttMessage Message { get; set; }

        public int PacketId { get; set; }
    }
}