using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
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

        #endregion

        #region PublicMethods

        public async Task<Status> InitializeAsync()
        {
            if (_status == Status.Initialized || _status == Status.Initializing)
                return _status;

            var tcs = new TaskCompletionSource<Status>();

            _status = await _connection.Initialize();

            if (_status != Status.Initialized)
                return _status;

            _connection.AckObservable.Where(m => m.Item1 == MsgType.ConnectAck)
                .Subscribe(m =>
                {
                    RefreshSubscriptions().ConfigureAwait(false);
                });

            if (_keepAliveInSeconds < 15 || _keepAliveInSeconds > 1200)
                _keepAliveInSeconds = 1200;

            _logger.Log(LogLevel.Trace, $"KeepAliveSeconds => {_keepAliveInSeconds}");

            var disposable = _connection.AckObservable
                .Where(m => m.Item1 == MsgType.ConnectAck)
                .Subscribe(m =>
                {
                    tcs.SetResult(Status.Initialized);
                });

            _connection.WriteSubject.OnNext(new ConnectMsg(_connectionId, _keepAliveInSeconds));

            _keepAliveTimer = new Timer(Ping);

            _connection.WriteSubject.Subscribe(ResetKeepAliveTimer);

            _connection.PublishObservable.Subscribe(PublishAck);

            await tcs.Task;

            disposable.Dispose();

            return _status;
        }

        public async Task<bool> PublishAsync(string message, string topic, TimeSpan timeout = default(TimeSpan))
        {
            if (timeout == default(TimeSpan))
                timeout = TimeSpan.FromSeconds(15);

            var messageToPublish = new PublishMsg
            {
                Topic = topic,
                Message = Encoding.UTF8.GetBytes(message)
            };

            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(timeout);

                try
                {
                    await PublishAndWaitForAck(messageToPublish, cts.Token);
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
        public async Task SubscribeAsync(Subscription subscription)
        {
            var streamSubscriptionDisposable = _connection.PublishObservable.Subscribe(subscription.PublishReceived);

            if (!IsAlreadySubscribed(subscription.Topic))
                await AddSubscription(subscription.Topic);

            subscription.Disposable = streamSubscriptionDisposable;
        }

        /// <summary>
        /// </summary>
        /// <param name="subscription"></param>
        /// <returns></returns>
        public async Task UnsubscribeAsync(Subscription subscription)
        {
            _logger.Log(LogLevel.Trace, $"Unsubscribe from {subscription.Topic}");

            if (IsAlreadySubscribed(subscription.Topic))
                await RemoveSubscription(subscription.Topic);

            subscription.Disposable.Dispose();
        }

        #endregion

        #region PrivateMethods
        private async Task<bool> PublishAndWaitForAck(PublishMsg messageToPublish, CancellationToken cancellationToken)
        {
            var tcsAck = new TaskCompletionSource<PublishMsg>();

            var publishAckDisposable = _connection.AckObservable
                .Where(s => s.Item1 == MsgType.PublishAck && s.Item2 == messageToPublish.PacketId)
                .Subscribe(m => { tcsAck.SetResult(new PublishMsg()); });

            cancellationToken.Register(() => { tcsAck.TrySetCanceled(); });

            _connection.WriteSubject.OnNext(messageToPublish);

            try
            {
                await tcsAck.Task;
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);
                return false;
            }

            publishAckDisposable.Dispose();
            return true;
        }

        private void PublishAck(MqttMessage mqttMessage)
        {
            _connection.WriteSubject.OnNext(new PublishAckMsg(mqttMessage.PacketId));
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

            SubscribeMsg subscribeMessage = null;

            lock (_subscriptions)
            {
                if (!_subscriptions.Contains(topic))
                {
                    _subscriptions.Add(topic);
                    subscribeMessage = new SubscribeMsg(_subscriptions.ToArray());
                }
                else
                {
                    tcs.SetResult(true);
                }
            }

            if (_status != Status.Initialized)
            {
                tcs.SetResult(false);
            }
            else
            {

                using (var cts = new CancellationTokenSource())
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(5));

                    if (subscribeMessage == null)
                        return await tcs.Task;

                    var streamSubscriptionDisposable = new SingleAssignmentDisposable();
                    var streamSubscriptionAction = new Action<Tuple<MsgType, int>>(message =>
                    {
                        if (message.Item2 != subscribeMessage.PacketId)
                            return;

                        tcs.SetResult(true);
                        streamSubscriptionDisposable.Disposable.Dispose();
                    });

                    cts.Token.Register(() =>
                    {
                        streamSubscriptionDisposable.Disposable.Dispose();
                        tcs.SetResult(false);
                    });

                    streamSubscriptionDisposable.Disposable = _connection.AckObservable.Where(s => s.Item1 == MsgType.SubscribeAck).Subscribe(streamSubscriptionAction);

                    _connection.WriteSubject.OnNext(subscribeMessage);
                }
            }

            return await tcs.Task;
        }

        private async Task RefreshSubscriptions()
        {
            var tcs = new TaskCompletionSource<bool>();

            SubscribeMsg subscribeMessage;

            lock (_subscriptions)
            {
                if (!_subscriptions.Any())
                    return;

                subscribeMessage = new SubscribeMsg(_subscriptions.ToArray());
            }

            var streamSubscriptionDisposable = new SingleAssignmentDisposable();
            var streamSubscriptionAction = new Action<Tuple<MsgType, int>>(message =>
            {
                if (message.Item2 != subscribeMessage.PacketId)
                    return;

                tcs.SetResult(true);
                streamSubscriptionDisposable.Disposable.Dispose();
            });

            streamSubscriptionDisposable.Disposable = _connection.AckObservable.Where(s => s.Item1 == MsgType.SubscribeAck).Subscribe(streamSubscriptionAction);

            _connection.WriteSubject.OnNext(subscribeMessage);

            await tcs.Task;
        }

        private async Task<bool> RemoveSubscription(string topic)
        {
            var tcs = new TaskCompletionSource<bool>();

            UnsubscribeMsg unsubscribeMessage;

            lock (_subscriptions)
            {
                _subscriptions.Remove(topic);
                unsubscribeMessage = new UnsubscribeMsg(new[] {topic});
            }

            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(TimeSpan.FromSeconds(5));

                var streamSubscriptionDisposable = new SingleAssignmentDisposable();
                var streamSubscriptionAction = new Action<Tuple<MsgType, int>>(message =>
                {
                    if (message.Item2 != unsubscribeMessage.PacketId)
                        return;

                    tcs.SetResult(true);
                    streamSubscriptionDisposable.Disposable.Dispose();
                });

                cts.Token.Register(() =>
                {
                    streamSubscriptionDisposable.Disposable.Dispose();
                    tcs.SetResult(false);
                });

                streamSubscriptionDisposable.Disposable = _connection.AckObservable
                    .Where(s => s.Item1 == MsgType.UnsubscribeAck)
                    .Subscribe(streamSubscriptionAction);

                _connection.WriteSubject.OnNext(unsubscribeMessage);
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