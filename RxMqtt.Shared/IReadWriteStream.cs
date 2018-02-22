using System;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Shared{
    public interface IReadWriteStream
    {
        void Write(MqttMessage message);
    }
}