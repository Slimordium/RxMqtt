using System;
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
    internal class Client : IDisposable
    {
        private readonly IDisposable _packetReadDisposable;

        private readonly EventLoopScheduler _readEventLoopScheduler = new EventLoopScheduler();

        private readonly ReadWriteStream _readWriteStream;

        private readonly Socket _socket;

        private readonly ConcurrentDictionary<string, IDisposable> _subscriptionDisposables = new ConcurrentDictionary<string, IDisposable>();

        private string _clientId;

        private Timer _heartbeatTimer;

        private int _keepAliveSeconds;

        private ILogger _logger = LogManager.GetCurrentClassLogger();

        internal Client(Socket socket)
        {
            while (!socket.Connected) Task.Delay(250).Wait();

            _socket = socket;

            _readWriteStream = new ReadWriteStream(new NetworkStream(socket));

            _packetReadDisposable = _readWriteStream.PacketObservable.SubscribeOn(_readEventLoopScheduler).Subscribe(ProcessPackets);
        }

        private bool _disposed;

        private void HeartbeatCallback(object state)
        {
            Dispose();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!disposing || _disposed) return;

            _disposed = true;

            DisposeAllSubscriptions();

            _packetReadDisposable?.Dispose();

            _readWriteStream?.Dispose();
        }

        private void DisposeAllSubscriptions()
        {
            foreach (var subscription in _subscriptionDisposables)
            {
                IDisposable disposable;

                while (!_subscriptionDisposables.TryRemove(subscription.Key, out disposable))
                {
                    Thread.Sleep(1);
                }

                disposable?.Dispose();
                disposable = null;
            }
        }

        internal bool IsConnected()
        {
            return _socket.Connected;
        }

        private void OnNext(MqttMessage buffer)
        {
            if (buffer == null || !_socket.Connected)
                return;

            _readWriteStream?.Write(buffer);
        }

        private void ProcessPackets(byte[] buffer)
        {
            if (buffer == null || buffer.Length <= 1)
                return;

            _heartbeatTimer?.Change(TimeSpan.FromSeconds(_keepAliveSeconds), Timeout.InfiniteTimeSpan);

            var msgType = (MsgType) (byte) ((buffer[0] & 0xf0) >> (byte) MsgOffset.Type);

            try
            {
                switch (msgType)
                {
                    case MsgType.Publish:
                        var publishMsg = new Publish(buffer);

                        _readWriteStream.Write(new PublishAck(publishMsg.PacketId));

                        MqttBroker.PublishSyncSubject.OnNext(publishMsg); //Broadcast this message to any client that is subscribed to the topic this was sent to
                        break;
                    case MsgType.Connect:
                        var connectMsg = new Connect(buffer);

                        _keepAliveSeconds = connectMsg.KeepAlivePeriod;

                        _logger.Log(LogLevel.Trace, $"Client '{connectMsg.ClientId}' connected");

                        _clientId = connectMsg.ClientId;

                        _heartbeatTimer = new Timer(HeartbeatCallback, null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

                        _heartbeatTimer.Change(TimeSpan.FromSeconds(_keepAliveSeconds), Timeout.InfiniteTimeSpan);

                        _logger = LogManager.GetLogger(_clientId);

                        _readWriteStream.SetLogger(_logger);

                        _readWriteStream.Write(new ConnectAck());
                        break;
                    case MsgType.PingRequest:

                        _readWriteStream.Write(new PingResponse());
                        break;
                    case MsgType.Subscribe:
                        var subscribeMsg = new Subscribe(buffer);

                        _readWriteStream.Write(new SubscribeAck(subscribeMsg.PacketId));

                        Subscribe(subscribeMsg.Topics);
                        break;
                    case MsgType.Unsubscribe:
                        var unsubscribeMsg = new Unsubscribe(buffer);

                        _readWriteStream.Write(new UnsubscribeAck(unsubscribeMsg.PacketId));

                        Unsubscribe(unsubscribeMsg.Topics);
                        break;
                    case MsgType.PublishAck:
                    case MsgType.Disconnect:
                        break;
                    default:
                        _logger.Log(LogLevel.Warn, "Ignoring message");
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

                while (!_subscriptionDisposables.TryAdd(topic, MqttBroker.Subscribe(topic).ObserveOn(Scheduler.Default).Subscribe(OnNext)))
                {
                    Thread.Sleep(1);
                }
            }
        }

        private void Unsubscribe(IEnumerable<string> topics)
        {
            if (topics == null)
                return;

            foreach (var topic in topics)
            {
                IDisposable disposable;

                while (!_subscriptionDisposables.TryRemove(topic, out disposable))
                {
                    Thread.Sleep(1);
                }

                disposable?.Dispose();
            }
        }
    }
}