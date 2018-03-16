using System;
using NLog;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Shared{
    public interface IReadWriteStream : IDisposable
    {
        void Write(MqttMessage message);

        void Write(byte[] buffer);

        void SetLogger(ILogger logger);

        IObservable<byte[]> PacketObservable { get; }
    }
}