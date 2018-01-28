using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using RxMqtt.Shared;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Client
{
    internal interface IConnection : IDisposable
    {
        Subject<MqttMessage> WriteSubject { get; }

        IObservable<IList<Publish>> PublishObservable { get; }

        IObservable<IList<Tuple<MsgType, int>>> AckObservable { get; }

        Task<Status> Initialize();
    }
}