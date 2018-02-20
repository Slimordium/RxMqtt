using System;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Shared{
    public interface IReadWriteStream{
        //void Read(Action<byte[]> callback);

        void Write(MqttMessage message);
    }
}