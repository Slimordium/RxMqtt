using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using RxMqtt.Shared;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Client
{
    internal interface IConnection : IDisposable
    {
        ISubject<MqttMessage> WriteSubject { get; }

        IObservable<Publish> PublishObservable { get; }

        IObservable<Tuple<MsgType, int>> AckObservable { get; }

        Task<Status> Initialize();

        Task<bool> WaitForAck(MsgType msgType, CancellationToken cancellationToken = default(CancellationToken), int? packetId = null);
    }
}