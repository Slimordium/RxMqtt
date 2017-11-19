using System;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using RxMqtt.Shared;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Client
{
    internal interface IConnection : IDisposable
    {
        Subject<MqttMessage> WriteSubject { get; }

        IObservable<PublishMsg> PublishObservable { get; }

        IObservable<Tuple<MsgType, int>> AckObservable { get; }

        Task<Status> Initialize();
    }
}