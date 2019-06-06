using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
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

            _logger.Log(LogLevel.Trace, $"MQTT Client '{connectionId}' => '{brokerHostname}'");

            _connection = new MqttStreamWrapper(
                brokerHostname,
                port);
        }

        #region PrivateFields

        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();
        private readonly string _connectionId;
        private readonly MqttStreamWrapper _connection;
        private ushort _keepAliveInSeconds;
        private Status _status = Status.Error;
        private readonly ConcurrentDictionary<string, IDisposable> _subscriptions = new ConcurrentDictionary<string, IDisposable>();
        private bool _disposed;
        private Timer _keepAliveTimer;
        private TimeSpan _timeOut = TimeSpan.FromSeconds(3);

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
            try
            {
                _connection.Write(new PingMsg());

                await _connection.MessageAckSubject
                            .Timeout(_timeOut)
                            .Where(envelope => envelope.MsgType == MsgType.PingResponse)
                            .ObserveOn(Scheduler.Default)
                            .Take(1);

                _keepAliveTimer.Change((int)TimeSpan.FromSeconds(_keepAliveInSeconds).TotalMilliseconds, Timeout.Infinite);
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, $"Waiting for PingResponse => {e.Message}");
            }
        }

        public Task<bool> PublishAsync(string message, string topic)
        {
            return PublishAsync(Encoding.UTF8.GetBytes(message), topic);
        }

        public Task<bool> PublishAsync(byte[] buffer, string topic)
        {
            if (topic.Contains("#") || topic.Contains("+"))
            {
                throw new ArgumentException($"'{topic}' is not a valid topic");
            }

            var messageToPublish = new Publish
            {
                Topic = topic,
                Message = buffer
            };

            _logger.Log(LogLevel.Info, $"Publishing to => '{topic}'");

            _connection.Write(messageToPublish);

            //TODO: If waiting for the ack times out, check connection state and re-publish 
            return WaitForAck(MsgType.PublishAck, messageToPublish.PacketId);
        }

        /// <summary>
        /// Returns observable that emits messages published on specified topic
        /// </summary>
        /// <param name="topic"></param>
        /// <returns></returns>
        public async Task<IObservable<Publish>> WhenPublishedOn(string topic)
        {
            if (!Shared.Messages.Subscribe.IsValidTopic(topic))
            {
                throw new ArgumentException($"'{topic}' is not a valid topic");
            }

            var publishObservable = _connection.PublishedMessagesSubject
                .Where(publishedMessage => publishedMessage.IsTopicMatch(topic))
                .ObserveOn(Scheduler.Default)
                .Select(publishedMessage => publishedMessage);

            _subscriptions.TryAdd(topic, null);

            var msg = new Subscribe(_subscriptions.Keys.ToArray());

            _connection.Write(msg);

            if (!await WaitForAck(MsgType.SubscribeAck, msg.PacketId))
                throw new TimeoutException();

            return await Task.FromResult(publishObservable);
        }

        /// <summary>
        /// Topic.StartsWith(topic)
        /// </summary>
        /// <param name="callback"></param>
        /// <param name="topic"></param>
        public async Task<bool> Subscribe(Action<Publish> callback, string topic)
        {
            if (_subscriptions.ContainsKey(topic))
            {
                _logger.Log(LogLevel.Warn, $"Already subscribed to '{topic}'");
                return await Task.FromResult(false);
            }

            if (!Shared.Messages.Subscribe.IsValidTopic(topic))
            {
                throw new ArgumentException($"'{topic}' is not a valid topic");
            }

            _subscriptions.TryAdd(topic,
                _connection.PublishedMessagesSubject
                .Where(publishedMessages => publishedMessages.IsTopicMatch(topic))
                .ObserveOn(Scheduler.Default)
                .Subscribe(publishedMessage =>
                    {
                        try
                        {
                            callback.Invoke(publishedMessage);
                        }
                        catch (Exception e)
                        {
                            _logger.Log(LogLevel.Error, $"Subscribe callback => {e}");
                        }
                    }));

            var subscribeMessage = new Subscribe(_subscriptions.Keys.ToArray());

            _connection.Write(subscribeMessage);

            return await WaitForAck(MsgType.SubscribeAck, subscribeMessage.PacketId);
        }

        private async Task<bool> WaitForAck(MsgType msgType, int packetId)
        {
            try
            {
                await _connection.MessageAckSubject
                                .Timeout(_timeOut)
                                .Where(packetEnvelope => packetEnvelope.MsgType == msgType && packetEnvelope.PacketId == packetId)
                                .ObserveOn(Scheduler.Default)
                                .Take(1);
            }
            catch (TimeoutException)
            {
                _logger.Log(LogLevel.Warn, $"Timed out waiting for '{msgType}'");

                return await Task.FromResult(false);
            }

            return await Task.FromResult(true);
        }

        public async Task Unsubscribe(string topic)
        {
            _logger.Log(LogLevel.Trace, $"Unsubscribe from '{topic}'");

            if (!_subscriptions.ContainsKey(topic))
                return;

            _subscriptions.TryRemove(topic, out var dispossable);

            dispossable?.Dispose();

            var unsubscribeMessage = new Unsubscribe(new[] {topic});

            _connection.Write(unsubscribeMessage);

            await WaitForAck(MsgType.UnsubscribeAck, unsubscribeMessage.PacketId);
        }

        #endregion

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!disposing || _disposed) return;

            _disposed = true;

            foreach (var disposable in _subscriptions)
            {
                disposable.Value?.Dispose();
            }

            _connection?.Dispose();

            _keepAliveTimer?.Dispose();
        }
    }
}