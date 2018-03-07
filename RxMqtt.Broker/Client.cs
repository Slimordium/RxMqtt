using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using RxMqtt.Shared;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Broker
{
    internal class Client
    {
        private string _clientId;

        private ILogger _logger = LogManager.GetCurrentClassLogger();

        private readonly ConcurrentBag<string> _subscriptions = new ConcurrentBag<string>();

        private readonly List<IDisposable> _disposables = new List<IDisposable>();

        private readonly IReadWriteStream _readWriteStream;

        private readonly ISubject<Publish> _writeSyncSubject = new BehaviorSubject<Publish>(null);

        private readonly ISubject<Publish> _writeSubject;

        internal Client(Socket socket, ref CancellationTokenSource cancellationTokenSource)
        {
            while (!socket.Connected)
            {
                Task.Delay(500).Wait();
            }

            cancellationTokenSource.Token.Register(() =>
            {
                foreach (var disposable in _disposables)
                {
                    disposable.Dispose();
                }
            });

            _readWriteStream = new ReadWriteStream(new NetworkStream(socket), ProcessPackets, ref cancellationTokenSource);

            _writeSubject = Subject.Synchronize(_writeSyncSubject);
            _disposables.Add(_writeSubject.Subscribe(OnNext));
        }

        private void OnNext(Publish buffer)
        {
            //This sends the message to the client attached to this _networkStream

            if (buffer == null)
                return;

            _readWriteStream.Write(buffer);
        }

        private void ProcessPackets(byte[] buffer)
        {
            if (buffer == null || buffer.Length <= 1)
                return;

            var msgType = (MsgType)(byte)((buffer[0] & 0xf0) >> (byte)MsgOffset.Type);

            try
            {
                _logger.Log(LogLevel.Trace, $"In <= '{msgType}'");

                switch (msgType)
                {
                    case MsgType.Publish:
                        var publishMsg = new Publish(buffer);

                        MqttBroker.PublishSyncSubject.OnNext(publishMsg); //Broadcast this message to any client that is subscirbed to the topic this was sent to

                        _readWriteStream.Write(new PublishAck(publishMsg.PacketId));

                        break;
                    case MsgType.Connect:
                        var connectMsg = new Connect(buffer);

                        _logger.Log(LogLevel.Trace, $"Client '{connectMsg.ClientId}' connected");

                        _clientId = connectMsg.ClientId;

                        //_keepAliveDisposable?.Dispose();

                        //_keepAliveDisposable = Observable.Interval(TimeSpan.FromSeconds(connectMsg.KeepAlivePeriod + connectMsg.KeepAlivePeriod / 2)).Subscribe(_ =>
                        //{
                        //    if (Interlocked.Exchange(ref _shouldCancel, 1) != 1) return;

                        //    _logger.Log(LogLevel.Warn, "Client appears to be disconnected, dropping connection");

                        //    _cancellationTokenSource.Cancel(false);
                        //});

                        _logger = LogManager.GetLogger(_clientId);

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

            //Interlocked.Exchange(ref _shouldCancel, 0); //Reset after all incoming messages
        }

        private void Subscribe(IEnumerable<string> topics) //TODO: Support wild cards in topic path, like: mytopic/#/anothertopic
        {
            foreach (var topic in topics)
            {
                if (_subscriptions.Contains(topic))
                    continue;

                _subscriptions.Add(topic);

                //_topicSubject.OnNext(new KeyValuePair<string, string>(_clientId, topic));

                //if (topic.EndsWith("#"))
                //{
                //    var newtopic = topic.Replace("#", "");
                //    _disposables.Add(MqttBroker.PublishSyncSubject.SubscribeOn(Scheduler.Default).Where(m => m != null && m.MsgType == MsgType.Publish && m.Topic.StartsWith(newtopic)).Subscribe(OnNext));
                //}
                //else
                //{
                //    _disposables.Add(MqttBroker.PublishSyncSubject.Where(m => m != null && m.MsgType == MsgType.Publish && m.Topic.Equals(topic)).Subscribe(OnNext));
                //}

                //_logger.Log(LogLevel.Info, $"Subscribed to '{topic}'");

                _disposables.Add(MqttBroker.Subscribe(topic).SubscribeOn(Scheduler.Default).Subscribe(_writeSubject.OnNext));
            }
        }
    }

}