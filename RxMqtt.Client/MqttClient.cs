using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using RxMqtt.Shared;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Client
{
    public class MqttClient
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

    
        #region PrivateFields

        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();
        private readonly string _connectionId;
        private readonly TcpConnection _connection;
        private ushort _keepAliveInSeconds;
        private Timer _keepAliveTimer;
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

            _keepAliveTimer = new Timer(Ping);
            
            //_keepAliveDisposable = _connection.WriteSubject.Subscribe(ResetKeepAliveTimer);

            return _status;
        }

        public async Task<PublishAck> PublishAsync(string message, string topic)
        {
            var messageToPublish = new Publish
            {
                Topic = topic,
                Message = Encoding.UTF8.GetBytes(message)
            };
            _connection.Write(messageToPublish);

            var packetEnvelope = await _connection.PacketSyncSubject
                                .SkipWhile(envelope =>
                                {
                                    if (envelope.MsgType != MsgType.PublishAck)
                                        return true;

                                    if (envelope.PacketId != messageToPublish.PacketId)
                                        return true;

                                    return false;
                                })
                                .Take(1);

            return (PublishAck)packetEnvelope.Message;
        }

        public void Subscribe(Action<string> callback, string topic)
        {
            if (_disposables.ContainsKey(topic))
            {
                _logger.Log(LogLevel.Warn, $"Already subscribed to '{topic}'");
                return;
            }

            _disposables.Add(topic, 
                _connection.PacketSyncSubject
                .Where(publish =>
                    {
                        if (publish != null && publish.MsgType == MsgType.Publish)
                        {
                            var msg = (Publish) publish.Message;

                            if (msg == null || !msg.Topic.StartsWith(topic))
                                return false;

                            return true;
                        }

                        return false;
                    })
                .SubscribeOn(Scheduler.Default)
                .Subscribe(publish =>
                    {
                        var msg = (Publish) publish.Message;

                        callback.Invoke(Encoding.UTF8.GetString(msg.Message)); 
                    }));

            _connection.Write(new Subscribe(_disposables.Keys.ToArray()));
        }

        public void Unsubscribe(string topic)
        {
            _logger.Log(LogLevel.Trace, $"Unsubscribe from {topic}");

            _disposables[topic].Dispose();

            _disposables.Remove(topic);

            _connection.Write(new Unsubscribe(new []{topic}));
        }

        #endregion

        #region PrivateMethods
        
        private void Ping(object sender)
        {
            _logger.Log(LogLevel.Trace, "Ping");

            _connection.Write(new PingMsg());
        }

        private void ResetKeepAliveTimer(MqttMessage mqttMessage)
        {
            _keepAliveTimer.Change((int)TimeSpan.FromSeconds(_keepAliveInSeconds).TotalMilliseconds, Timeout.Infinite);
        }


        #endregion
    }
}