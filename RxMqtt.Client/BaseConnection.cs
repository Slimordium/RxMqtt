using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using NLog;
using RxMqtt.Shared;
using RxMqtt.Shared.Messages;
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("RxMqtt.Broker")]
[assembly: InternalsVisibleTo("RxMqtt.Client")]

namespace RxMqtt.Client
{
    internal abstract class BaseConnection
    {
        protected static Subject<Tuple<MsgType, int>> _ackSubject;

        protected static Subject<Publish> _publishSubject;
        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        private readonly object _pubSync = new object();

        protected string HostName;

        public IObservable<Publish> PublishObservable { get; set; }

        public IObservable<Tuple<MsgType, int>> AckObservable { get; set; }

        public Subject<MqttMessage> WriteSubject { get; } = new Subject<MqttMessage>();

        protected abstract void DisposeStreams();

        protected abstract void ReEstablishConnection();

        protected void InitializeObservables()
        {
            try
            {
                _ackSubject?.Dispose();
                _publishSubject?.Dispose();

                _ackSubject = new Subject<Tuple<MsgType, int>>();
                _publishSubject = new Subject<Publish>();

                AckObservable = _ackSubject.AsObservable();
                PublishObservable = _publishSubject.AsObservable().Synchronize(_pubSync);
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);
            }
        }
    }
}