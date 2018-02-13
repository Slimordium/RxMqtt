using System;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Shared{
    public interface ISubscription
    {
        IDisposable SubscriptionDisposable { get; }
        string Topic { get; }
        void Subscribe(IObservable<Publish> observable);
    }
}