using RxMqtt.Shared.Messages;

namespace RxMqtt.Shared{
    internal class PacketEnvelope
    {
        public MsgType MsgType { get; set; }

        public MqttMessage Message { get; set; }

        public int PacketId { get; set; }
    }
}