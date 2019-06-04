using RxMqtt.Shared.Enums;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Client{
    internal class PacketEnvelope
    {
        public MsgType MsgType { get; set; } = MsgType.Disconnect;

        public MqttMessage Message { get; set; } = new Publish();

        public int PacketId { get; set; }
    }
}