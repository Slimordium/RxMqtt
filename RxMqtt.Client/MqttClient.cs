using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using RxMqtt.Shared;
using RxMqtt.Shared.Enums;
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
                port);
        }

        #region PrivateFields

        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();
        private readonly string _connectionId;
        private readonly TcpConnection _connection;
        private ushort _keepAliveInSeconds;
        private Status _status = Status.Error;
        private readonly Dictionary<string, IDisposable> _disposables = new Dictionary<string, IDisposable>();
        private Timer _keepAliveTimer;

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

            _keepAliveTimer = new Timer(Ping);
            _keepAliveTimer.Change((int)TimeSpan.FromSeconds(_keepAliveInSeconds).TotalMilliseconds, Timeout.Infinite);

            return _status;
        }

        private async void Ping(object sender)
        {
            _connection.Write(new PingMsg());

            var pingResponse = await _connection.PacketSubject.Where(envelope => envelope.MsgType == MsgType.PingResponse).Take(1);

            _keepAliveTimer.Change((int)TimeSpan.FromSeconds(_keepAliveInSeconds).TotalMilliseconds, Timeout.Infinite);
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
                .ObserveOn(Scheduler.Default)
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
                .ObserveOn(Scheduler.Default)
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
                .Timeout(TimeSpan.FromSeconds(5))
                .SkipWhile(envelope => envelope?.MsgType != MsgType.PublishAck)
                .SkipWhile(envelope => envelope.PacketId != messageToPublish.PacketId)
                .ObserveOn(Scheduler.Default)
                .Take(1);

            packetId = packetEnvelope.PacketId;

            return new PublishAck((ushort) packetId);
        }

        public async Task<IObservable<string>> GetPublishStringObservable(string topic)
        {
            var subscription = _connection.PublishedMessageSubject
                .Where(message => message.IsTopicMatch(topic))
                .ObserveOn(Scheduler.Default)
                .Select(publishedMessage => Encoding.UTF8.GetString(publishedMessage.Message));

            if (!_disposables.ContainsKey(topic))
                _disposables.Add(topic, null);

            var msg = new Subscribe(_disposables.Keys.ToArray());

            _connection.Write(msg);

            if (!await WaitForAck(MsgType.SubscribeAck, msg.PacketId))
                throw new TimeoutException();

            return subscription;
        }

        public async Task<IObservable<Publish>> GetPublishObservable(string topic)
        {
            var publishObservable = _connection.PublishedMessageSubject
                .Where(message => message.IsTopicMatch(topic))
                .ObserveOn(Scheduler.Default)
                .Select(publishedMessage => publishedMessage);

            if (!_disposables.ContainsKey(topic))
                _disposables.Add(topic, null);

            var msg = new Subscribe(_disposables.Keys.ToArray());

            _connection.Write(msg);

            if (!await WaitForAck(MsgType.SubscribeAck, msg.PacketId))
                throw new TimeoutException();

            return publishObservable;
        }

        public IObservable<byte[]> GetPublishByteObservable(string topic)
        {
            var publishByteObservable = _connection.PublishedMessageSubject
                .Where(message => message.IsTopicMatch(topic))
                .ObserveOn(Scheduler.Default)
                .Select(publishedMessage => publishedMessage.Message);

            if (!_disposables.ContainsKey(topic))
                _disposables.Add(topic, null);

            _connection.Write(new Subscribe(_disposables.Keys.ToArray()));

            return publishByteObservable;
        }

        /// <summary>
        /// Topic.StartsWith(topic)
        /// </summary>
        /// <param name="callback"></param>
        /// <param name="topic"></param>
        public async Task<bool> Subscribe(Action<string> callback, string topic)
        {
            if (_disposables.ContainsKey(topic))
            {
                _logger.Log(LogLevel.Warn, $"Already subscribed to '{topic}'");
                return await Task.FromResult(false);
            }

            if (!Shared.Messages.Subscribe.IsValidTopic(topic))
            {
                return await Task.FromResult(false);
            }

            _disposables.Add(topic,
                _connection.PublishedMessageSubject
                .Where(message => message.IsTopicMatch(topic))
                .ObserveOn(Scheduler.Default)
                .Subscribe(publishedMessage =>
                    {
                        try
                        {
                            callback.Invoke(Encoding.UTF8.GetString(publishedMessage.Message));
                        }
                        catch (Exception e)
                        {
                            _logger.Log(LogLevel.Error, $"Subscribe callback => {e}");
                        }
                    }));

            var subscribeMessage = new Subscribe(_disposables.Keys.ToArray());

            _connection.Write(subscribeMessage);

            return await WaitForAck(MsgType.SubscribeAck, subscribeMessage.PacketId);
        }

        private async Task<bool> WaitForAck(MsgType msgType, int packetId)
        {
            try
            {
                var subscribeAck = await _connection.PacketSubject
                    .Timeout(TimeSpan.FromSeconds(5))
                    .ObserveOn(Scheduler.Default)
                    .Where(packetEnvelope =>
                        packetEnvelope.MsgType == msgType &&
                        packetEnvelope.PacketId == packetId)
                    .Take(1);
            }
            catch (TimeoutException)
            {
                _logger.Log(LogLevel.Warn, $"Timed out waiting for {msgType}");

                return await Task.FromResult(false);
            }

            return await Task.FromResult(true);
        }

        public async Task<bool> Subscribe(Action<byte[]> callback, string topic)
        {
            if (_disposables.ContainsKey(topic))
            {
                _logger.Log(LogLevel.Warn, $"Already subscribed to '{topic}'");
                return await Task.FromResult(false);
            }

            if (!Shared.Messages.Subscribe.IsValidTopic(topic))
            {
                return await Task.FromResult(false);
            }

            _disposables.Add(topic,
                _connection.PublishedMessageSubject
                    .Where(message => message.IsTopicMatch(topic))
                    .ObserveOn(Scheduler.Default)
                    .Subscribe(publishedMessage =>
                    {
                        try
                        {
                            callback.Invoke(publishedMessage.Message);
                        }
                        catch (Exception e)
                        {
                            _logger.Log(LogLevel.Error, $"Subscribe callback => {e}");
                        }
                    }));

            var subscribeMessage = new Subscribe(_disposables.Keys.ToArray());

            _connection.Write(subscribeMessage);

            return await WaitForAck(MsgType.SubscribeAck, subscribeMessage.PacketId);
        }

        public async Task Unsubscribe(string topic)
        {
            _logger.Log(LogLevel.Trace, $"Unsubscribe from {topic}");

            if (!_disposables.ContainsKey(topic))
                return;

            _disposables[topic]?.Dispose();

            _disposables.Remove(topic);

            var unsubscribeMessage = new Unsubscribe(new[] {topic});

            _connection.Write(unsubscribeMessage);

            await WaitForAck(MsgType.UnsubscribeAck, unsubscribeMessage.PacketId);
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

            _keepAliveTimer?.Dispose();
        }
    }
}