using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using RxMqtt.Shared;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Client
{
    public class MqttClient : IMqttClient
    {
        /// <summary>
        ///     MQTT client
        ///     QOS of 1
        /// </summary>
        /// <param name="connectionId"></param>
        /// <param name="brokerHostname"></param>
        /// <param name="port"></param>
        /// <param name="keepAliveInSeconds">1200 max</param>
        /// <param name="pfxFileName">*-private.pfx</param>
        /// <param name="pfxPassword"></param>

        public MqttClient(
            string connectionId,
            string brokerHostname,
            int port,
            int keepAliveInSeconds = 1200,
            string pfxFileName = "",
            string pfxPassword = "")
        {
            _connectionId = connectionId;
            _keepAliveInSeconds = (ushort) keepAliveInSeconds;

            _logger.Log(LogLevel.Trace, $"MQTT Client {connectionId}, {brokerHostname}");

            //TODO: Allow use of websocket connection instead of TCP
            _connection = new TcpConnection(
                connectionId,
                brokerHostname,
                keepAliveInSeconds,
                port,
                pfxFileName,
                pfxPassword);
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);

            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            _disposed = true;

            _keepAliveDisposable?.Dispose();

            _connection?.Dispose();
        }
        
        #region PrivateFields

        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();
        private readonly string _connectionId;
        private bool _disposed;
        private readonly List<string> _subscriptions = new List<string>();
        private readonly IConnection _connection;
        private ushort _keepAliveInSeconds;
        private Timer _keepAliveTimer;
        private Status _status = Status.Error;
        private IDisposable _keepAliveDisposable;

        #endregion

        #region PublicMethods

        public async Task<Status> InitializeAsync()
        {
            if (_status == Status.Initialized || _status == Status.Initializing)
                return _status;

            _status = await _connection.Initialize();

            if (_status != Status.Initialized)
                return _status;

            _connection.AckObservable
                .Subscribe(async message =>
                {
                    if (message != null && message.Item1 == MsgType.ConnectAck)
                        await RefreshSubscriptions().ConfigureAwait(false);
                });

            if (_keepAliveInSeconds < 15 || _keepAliveInSeconds > 1200)
                _keepAliveInSeconds = 1200;

            _logger.Log(LogLevel.Trace, $"KeepAliveSeconds => {_keepAliveInSeconds}");

            _connection.WriteSubject.OnNext(new Connect(_connectionId, _keepAliveInSeconds));

            var ack = await _connection.WaitForAck(MsgType.ConnectAck);

            if (!ack)
                _status = Status.Error;

            _keepAliveTimer = new Timer(Ping);

            _keepAliveDisposable = _connection.WriteSubject.Subscribe(ResetKeepAliveTimer);

            return _status;
        }

        public async Task<bool> PublishAsync(string message, string topic, TimeSpan timeout = default(TimeSpan))
        {
            if (timeout == default(TimeSpan))
                timeout = TimeSpan.FromSeconds(15);

            var messageToPublish = new Publish
            {
                Topic = topic,
                Message = Encoding.UTF8.GetBytes(message)
            };

            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(timeout);

                try
                {
                    var result = await PublishAndWaitForAck(messageToPublish, cts.Token);
                }
                catch (Exception e)
                {
                    _logger.Log(LogLevel.Error, e);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// </summary>
        /// <param name="subscription"></param>
        /// <returns></returns>
        public async Task SubscribeAsync(ISubscription subscription)
        {
            if (!IsAlreadySubscribed(subscription.Topic))
                await AddSubscription(subscription.Topic);

            subscription.Subscribe(_connection.PublishObservable);
        }

        /// <summary>
        /// </summary>
        /// <param name="subscription"></param>
        /// <returns></returns>
        public async Task UnsubscribeAsync(ISubscription subscription)
        {
            _logger.Log(LogLevel.Trace, $"Unsubscribe from {subscription.Topic}");

            if (IsAlreadySubscribed(subscription.Topic))
                await RemoveSubscription(subscription.Topic);

            subscription.SubscriptionDisposable.Dispose();
        }

        #endregion

        #region PrivateMethods
        private Task<bool> PublishAndWaitForAck(Publish message, CancellationToken cancellationToken)
        {
            _connection.WriteSubject.OnNext(message);

            return _connection.WaitForAck(MsgType.PublishAck, cancellationToken, message.PacketId);
        }

        private void Ping(object sender)
        {
            _logger.Log(LogLevel.Trace, "Ping");

            _connection.WriteSubject.OnNext(new PingMsg());
        }

        private void ResetKeepAliveTimer(MqttMessage mqttMessage)
        {
            _keepAliveTimer.Change((int)TimeSpan.FromSeconds(_keepAliveInSeconds).TotalMilliseconds, Timeout.Infinite);
        }

        private async Task<bool> AddSubscription(string topic)
        {
            var tcs = new TaskCompletionSource<bool>();

            Subscribe subscribeMessage = null;

            lock (_subscriptions)
            {
                if (!_subscriptions.Contains(topic))
                {
                    _subscriptions.Add(topic);
                    subscribeMessage = new Subscribe(_subscriptions.ToArray());
                }
            }

            if (subscribeMessage != null)
            {
                if (_status != Status.Initialized)
                {
                    tcs.SetResult(false);
                }
                else
                {
                    using (var cts = new CancellationTokenSource())
                    {
                        cts.CancelAfter(TimeSpan.FromSeconds(5));

                        _connection.WriteSubject.OnNext(subscribeMessage);

                        tcs.SetResult(await _connection.WaitForAck(MsgType.SubscribeAck, cts.Token, subscribeMessage.PacketId));
                    }
                }
            }
            else
                tcs.SetResult(true);

            return await tcs.Task;
        }

        private async Task RefreshSubscriptions()
        {
            var tcs = new TaskCompletionSource<bool>();

            Subscribe subscribeMessage;

            lock (_subscriptions)
            {
                if (!_subscriptions.Any())
                    return;

                subscribeMessage = new Subscribe(_subscriptions.ToArray());
            }

            _connection.WriteSubject.OnNext(subscribeMessage);

            tcs.SetResult(await _connection.WaitForAck(MsgType.SubscribeAck, CancellationToken.None, subscribeMessage.PacketId));

            await tcs.Task;
        }

        private async Task<bool> RemoveSubscription(string topic)
        {
            var tcs = new TaskCompletionSource<bool>();

            Unsubscribe unsubscribeMessage;

            lock (_subscriptions)
            {
                _subscriptions.Remove(topic);
                unsubscribeMessage = new Unsubscribe(new[] {topic});
            }

            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(TimeSpan.FromSeconds(5));

                _connection.WriteSubject.OnNext(unsubscribeMessage);

                tcs.SetResult(await _connection.WaitForAck(MsgType.SubscribeAck, CancellationToken.None, unsubscribeMessage.PacketId));
            }
           
            return await tcs.Task;
        }

        private bool IsAlreadySubscribed(string topic)
        {
            bool alreadySubscribed;

            lock (_subscriptions)
            {
                alreadySubscribed = _subscriptions.Contains(topic);
            }

            return alreadySubscribed;
        }

        #endregion
    }
}