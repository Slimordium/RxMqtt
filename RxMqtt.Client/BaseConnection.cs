﻿using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using NLog;
using RxMqtt.Shared;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Client
{
    internal abstract class BaseConnection
    {
        protected static Subject<Tuple<MsgType, int>> _ackSubject;

        protected static Subject<PublishMsg> _publishSubject;
        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        private readonly object _pubSync = new object();

        protected string HostName;

        public IObservable<PublishMsg> PublishObservable { get; set; }

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
                _publishSubject = new Subject<PublishMsg>();

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