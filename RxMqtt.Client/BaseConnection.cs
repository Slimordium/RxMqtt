using System;
using System.Collections.Generic;
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

        protected string HostName;

        public IObservable<IList<Publish>> PublishObservable { get; set; }

        public IObservable<IList<Tuple<MsgType, int>>> AckObservable { get; set; }

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

                AckObservable = _ackSubject.AsObservable().Buffer(1);
                PublishObservable = _publishSubject.AsObservable().Buffer(1);
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);
            }
        }

        protected void OnReceived(byte[] buffer)
        {
            var msgType = (MsgType)(byte)((buffer[0] & 0xf0) >> (byte)MsgOffset.Type);

            _logger.Log(LogLevel.Trace, $"In <= {msgType}");

            switch (msgType)
            {
                case MsgType.Publish:
                    var msg = new Publish(buffer);
                    _publishSubject.OnNext(msg);
                    break;
                case MsgType.ConnectAck:
                    _ackSubject.OnNext(new Tuple<MsgType, int>(msgType, 0));
                    break;
                case MsgType.PingResponse:
                    break;
                default:
                    _ackSubject.OnNext(new Tuple<MsgType, int>(msgType, MqttMessage.BytesToUshort(new[] { buffer[1], buffer[2] })));
                    break;
            }
        }
    }
}