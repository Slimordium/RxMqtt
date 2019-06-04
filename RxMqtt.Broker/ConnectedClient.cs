﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using RxMqtt.Shared;
using RxMqtt.Shared.Enums;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Broker
{
    /// <summary>
    /// 
    /// </summary>
    internal class ConnectedClient
    {
        private string _clientId;

        private ILogger _logger = LogManager.GetCurrentClassLogger();

        private readonly IDisposable _disposable;

        private ConcurrentDictionary<string, IDisposable> _subscriptionDisposables = new ConcurrentDictionary<string, IDisposable>();

        private MqttStream _mqttStream;

        private readonly EventLoopScheduler _readEventLoopScheduler = new EventLoopScheduler();

        internal bool Disposed { get; set; }

        private int _keepAliveSeconds;

        private Timer _heartbeatTimer;

        private Socket _socket;

        internal ConnectedClient(Socket socket)
        {
            while (!socket.Connected)
            {
                Task.Delay(250).Wait();
            }

            _socket = socket;
            _mqttStream = new MqttStream(_socket);

            _disposable = _mqttStream.PacketObservable.Subscribe(ParsePacket);
        }

        /// <summary>
        /// This is only invoked if the client does not send a Ping
        /// </summary>
        /// <param name="state"></param>
        private void HeartbeatCallback(object state)
        {
            Dispose();
        }

        internal void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!disposing || Disposed) return;

            Disposed = true;

            foreach (var subscription in _subscriptionDisposables)
            {
                subscription.Value.Dispose();
            }

            _subscriptionDisposables = null;
            _mqttStream.Dispose();
            _mqttStream = null;
        }

        internal bool IsConnected()
        {
            return _socket.Connected;
        }

        private void OnNext(MqttMessage buffer)
        {
            if (buffer == null || !_socket.Connected)
                return;

            _mqttStream?.Write(buffer);
        }
        
        private void ParsePacket(byte[] packet)
        {
            if (packet == null || packet.Length <= 1)
                return;

            _heartbeatTimer?.Change(TimeSpan.FromSeconds(_keepAliveSeconds), Timeout.InfiniteTimeSpan);

            var msgType = (MsgType)(byte)((packet[0] & 0xf0) >> (byte)MsgOffset.Type);

            try
            {
                switch (msgType)
                {
                    case MsgType.Publish:
                        var publishMsg = new Publish(packet);

                        _mqttStream.Write(new PublishAck(publishMsg.PacketId));

                        MqttBroker.PublishSyncSubject.OnNext(publishMsg); //Broadcast this message to any client that is subscribed to the topic this was sent to
                        break;
                    case MsgType.Connect:
                        var connectMsg = new Connect(packet);

                        _keepAliveSeconds = connectMsg.KeepAlivePeriod;

                        _logger.Log(LogLevel.Trace, $"Client '{connectMsg.ClientId}' connected");

                        _clientId = connectMsg.ClientId;

                        _heartbeatTimer = new Timer(HeartbeatCallback, null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

                        _heartbeatTimer.Change(TimeSpan.FromSeconds(_keepAliveSeconds), Timeout.InfiniteTimeSpan);

                        _logger = LogManager.GetLogger(_clientId);

                        _mqttStream.SetLogger(_logger);

                        _mqttStream.Write(new ConnectAck());
                        break;
                    case MsgType.PingRequest:

                        _mqttStream.Write(new PingResponse());
                        break;
                    case MsgType.Subscribe:
                        var subscribeMsg = new Subscribe(packet);

                        _mqttStream.Write(new SubscribeAck(subscribeMsg.PacketId));

                        Subscribe(subscribeMsg.Topics);
                        break;
                    case MsgType.Unsubscribe:
                        var unsubscribeMsg = new Unsubscribe(packet);

                        _mqttStream.Write(new UnsubscribeAck(unsubscribeMsg.PacketId));

                        Unsubscribe(unsubscribeMsg.Topics);
                        break;
                    case MsgType.PublishAck:
                    case MsgType.Disconnect:
                        break;
                    default:
                        _logger.Log(LogLevel.Warn, $"Ignoring message");
                        break;
                }
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, $"ProcessRead error '{e.Message}'");
            }
        }

        private void Subscribe(IEnumerable<string> topics)
        {
            if (topics == null)
                return;

            foreach (var topic in topics)
            {
                if (_subscriptionDisposables.ContainsKey(topic))
                    continue;

                _subscriptionDisposables.TryAdd(topic, MqttBroker.Subscribe(topic).ObserveOn(Scheduler.Default).Subscribe(OnNext));
            }
        }

        private void Unsubscribe(IEnumerable<string> topics)
        {
            if (topics == null)
                return;

            foreach (var topic in topics)
            {
                _subscriptionDisposables.TryRemove(topic, out var disposable);
                disposable?.Dispose();
            }
        }
    }
}