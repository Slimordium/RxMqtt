﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using RxMqtt.Shared;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Client
{
    public class MqttClient : IDisposable
    {
        /// <summary>
        ///     MQTT client
        ///     QOS of 1
        /// </summary>
        /// <param name="connectionId"></param>
        /// <param name="brokerHostname"></param>
        /// <param name="port"></param>
        /// <param name="keepAliveInSeconds">1200 max</param>
        public MqttClient(
            string connectionId,
            string brokerHostname,
            int port,
            int keepAliveInSeconds = 1200)
        {
            _connectionId = connectionId;
            _keepAliveInSeconds = (ushort) keepAliveInSeconds;

            _logger.Log(LogLevel.Trace, $"MQTT Client {connectionId}, {brokerHostname}");

            _connection = new TcpConnection(
                brokerHostname,
                keepAliveInSeconds,
                port);
        }

        #region PrivateFields

        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();
        private readonly string _connectionId;
        private readonly TcpConnection _connection;
        private ushort _keepAliveInSeconds;
        private Status _status = Status.Error;
        private readonly Dictionary<string, IDisposable> _disposables = new Dictionary<string, IDisposable>();

        #endregion

        #region PublicMethods

        public async Task<Status> InitializeAsync()
        {
            if (_status == Status.Initialized || _status == Status.Initializing)
                return _status;

            _status = await _connection.Initialize();

            if (_status != Status.Initialized)
                return _status;

            if (_keepAliveInSeconds < 15 || _keepAliveInSeconds > 1200)
                _keepAliveInSeconds = 1200;

            _logger.Log(LogLevel.Trace, $"KeepAliveSeconds => {_keepAliveInSeconds}");

            _connection.Write(new Connect(_connectionId, _keepAliveInSeconds));

            return _status;
        }

        public async Task<PublishAck> PublishAsync(string message, string topic, TimeSpan timeout, QosLevel qosLevel = QosLevel.AtLeastOnce)
        {
            var messageToPublish = new Publish
            {
                Topic = topic,
                Message = Encoding.UTF8.GetBytes(message)
            };

            _logger.Log(LogLevel.Info, $"Publishing string to => '{topic}'");

            _connection.Write(messageToPublish);
            var packetId = 0;

            if (qosLevel == QosLevel.AtMostOnce)
                return new PublishAck((ushort)packetId);

            var packetEnvelope = await _connection.PacketSubject
                .Timeout(timeout)
                .SkipWhile(envelope => envelope?.MsgType != MsgType.PublishAck)
                .SkipWhile(envelope => envelope.PacketId != messageToPublish.PacketId)
                .SubscribeOn(Scheduler.Default)
                .Take(1);

            packetId = packetEnvelope.PacketId;

            return new PublishAck((ushort)packetId);
        }

        public async Task<PublishAck> PublishAsync(byte[] buffer, string topic, TimeSpan timeout, QosLevel qosLevel = QosLevel.AtLeastOnce)
        {
            var messageToPublish = new Publish
            {
                Topic = topic,
                Message = buffer
            };

            _logger.Log(LogLevel.Info, $"Publishing bytes to => '{topic}'");

            _connection.Write(messageToPublish);

            var packetId = 0;

            if (qosLevel == QosLevel.AtMostOnce)
                return new PublishAck((ushort)packetId);

            var packetEnvelope = await _connection.PacketSubject
                .Timeout(timeout)
                .SkipWhile(envelope => envelope?.MsgType != MsgType.PublishAck)
                .SkipWhile(envelope => envelope.PacketId != messageToPublish.PacketId)
                .SubscribeOn(Scheduler.Default)
                .Take(1);

            packetId = packetEnvelope.PacketId;

            return new PublishAck((ushort)packetId);
        }

        /// <summary>
        /// Message will be split into packets set by BufferLength
        /// </summary>
        /// <param name="message"></param>
        /// <param name="topic"></param>
        /// <param name="qos"></param>
        /// <param name="qosLevel"></param>
        /// <returns></returns>
        public async Task<PublishAck> PublishAsync(string message, string topic, QosLevel qosLevel = QosLevel.AtLeastOnce)
        {
            var messageToPublish = new Publish
            {
                Topic = topic,
                Message = Encoding.UTF8.GetBytes(message),
                
            };

            _logger.Log(LogLevel.Info, $"Publishing to => '{topic}'");

            _connection.Write(messageToPublish);

            var packetId = 0;

            if (qosLevel == QosLevel.AtMostOnce)
                return new PublishAck((ushort) packetId);

            var packetEnvelope = await _connection.PacketSubject
                .Timeout(TimeSpan.FromSeconds(3))
                .SkipWhile(envelope => envelope?.MsgType != MsgType.PublishAck)
                .SkipWhile(envelope => envelope.PacketId != messageToPublish.PacketId)
                .SubscribeOn(Scheduler.Default)
                .Take(1);

            packetId = packetEnvelope.PacketId;

            return new PublishAck((ushort) packetId);
        }

        public IObservable<string> GetPublishStringObservable(string topic)
        {
            var subscription = _connection.PacketSubject
                .Where(publish =>
                {
                    if (publish == null || publish.MsgType != MsgType.Publish)
                        return false;

                    var msg = (Publish)publish.Message;

                    return msg != null && msg.Topic.StartsWith(topic);
                })
                .SubscribeOn(Scheduler.Default)
                .Select(envelope =>
                {
                    var msg = (Publish)envelope.Message;

                    var s = Encoding.UTF8.GetString(msg.Message);

                    return s;
                });

            if (!_disposables.ContainsKey(topic))
                _disposables.Add(topic, null);

            _connection.Write(new Subscribe(_disposables.Keys.ToArray()));

            return subscription;
        }

        public IObservable<byte[]> GetPublishByteObservable(string topic)
        {
            var subscription = _connection.PacketSubject
                .Where(publish =>
                {
                    if (publish == null || publish.MsgType != MsgType.Publish)
                        return false;

                    var msg = (Publish)publish.Message;

                    return msg != null && msg.Topic.StartsWith(topic);
                })
                .SubscribeOn(Scheduler.Default)
                .Select(envelope =>
                {
                    var msg = (Publish)envelope.Message;

                    return msg.Message;
                });

            if (!_disposables.ContainsKey(topic))
                _disposables.Add(topic, null);

            _connection.Write(new Subscribe(_disposables.Keys.ToArray()));

            return subscription;
        }

        /// <summary>
        /// Topic.StartsWith(topic)
        /// </summary>
        /// <param name="callback"></param>
        /// <param name="topic"></param>
        public void Subscribe(Action<string> callback, string topic)
        {
            if (_disposables.ContainsKey(topic))
            {
                _logger.Log(LogLevel.Warn, $"Already subscribed to '{topic}'");
                return;
            }

            _disposables.Add(topic, 
                _connection.PacketSubject
                .Where(publish =>
                    {
                        if (publish == null || publish.MsgType != MsgType.Publish)
                            return false;

                        var msg = (Publish)publish.Message;

                        return msg != null && msg.Topic.StartsWith(topic);
                    })
                .SubscribeOn(Scheduler.Default)
                .Subscribe(publish =>
                    {
                        var msg = (Publish) publish.Message;

                        callback.Invoke(Encoding.UTF8.GetString(msg.Message)); 
                    }));

            _connection.Write(new Subscribe(_disposables.Keys.ToArray()));
        }

        public void Subscribe(Action<byte[]> callback, string topic)
        {
            if (_disposables.ContainsKey(topic))
            {
                _logger.Log(LogLevel.Warn, $"Already subscribed to '{topic}'");
                return;
            }

            _disposables.Add(topic,
                _connection.PacketSubject
                    .Where(publish =>
                    {
                        if (publish == null || publish.MsgType != MsgType.Publish)
                            return false;

                        var msg = (Publish)publish.Message;

                        return msg != null && msg.Topic.StartsWith(topic);
                    })
                    .SubscribeOn(Scheduler.Default)
                    .Subscribe(publish =>
                    {
                        var msg = (Publish)publish.Message;

                        callback.Invoke(msg.Message);
                    }));

            _connection.Write(new Subscribe(_disposables.Keys.ToArray()));
        }

        public void Unsubscribe(string topic)
        {
            _logger.Log(LogLevel.Trace, $"Unsubscribe from {topic}");

            if (!_disposables.ContainsKey(topic))
                return;

            _disposables[topic]?.Dispose();

            _disposables.Remove(topic);

            _connection.Write(new Unsubscribe(new[] { topic }));
        }

        #endregion

        private bool _disposed;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!disposing || _disposed) return;

            _disposed = true;

            foreach (var disposable in _disposables)
            {
                disposable.Value?.Dispose();
            }

            _connection?.Dispose();
        }
    }
}