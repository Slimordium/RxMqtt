using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using NLog;
using RxMqtt.Client;
using RxMqtt.Shared;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Broker
{
    internal class Client
    {
        private string _clientId;

        private readonly NetworkStream _networkStream;

        private ILogger _logger = LogManager.GetCurrentClassLogger();

        private readonly List<string> _subscriptions = new List<string>();

        private readonly ISubject<Publish> _brokerPublishSubject;

        private IDisposable _keepAliveDisposable;

        private readonly List<IDisposable> _subscriptionDisposables = new List<IDisposable>();

        private readonly CancellationTokenSource _cancellationTokenSource;

        private long _shouldCancel;

        private readonly AutoResetEvent _publishThrottleEvent = new AutoResetEvent(true);

        private readonly IReadWriteStream _readWriteStream;

        private readonly Thread _readThread;

        internal  Client(ref NetworkStream _networkStream, ref ISubject<Publish> brokerPublishSubject, ref CancellationTokenSource cancellationTokenSource)
        {
            _cancellationTokenSource = cancellationTokenSource;
            this._networkStream = _networkStream;
            _brokerPublishSubject = brokerPublishSubject;

            _readWriteStream = new ReadWriteAsync(ref _networkStream);

            _readThread = new Thread(() =>
            {
                while (!_cancellationTokenSource.IsCancellationRequested)
                {
                    _publishThrottleEvent.WaitOne();
                    _publishThrottleEvent.Reset();

                    _readWriteStream.Read(ProcessPackets);
                }
            }) {IsBackground = true};
            _readThread.Start();
        }

        private void ReadLoop()
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                _publishThrottleEvent.WaitOne();
                _publishThrottleEvent.Reset();

                _readWriteStream.Read(ProcessPackets);
            }
        }

        private void OnNextPublish(Publish mqttMessage)
        {
            //This sends the message to the client attached to this _networkStream
            _readWriteStream.Write(mqttMessage);
        }

        private void ProcessPackets(byte[] inBuffer)
        {
            foreach (var buffer in Utilities.ParseReadBuffer(inBuffer))
            {
                if (buffer == null || buffer.Length < 2)
                    break;

                var msgType = (MsgType)(byte)((buffer[0] & 0xf0) >> (byte)MsgOffset.Type);

                try
                {
                    _logger.Log(LogLevel.Trace, $"In <= '{msgType}'");

                    switch (msgType)
                    {
                        case MsgType.Publish:
                            var publishMsg = new Publish(buffer);

                            _brokerPublishSubject.OnNext(publishMsg); //Broadcast this message to any client that is subscirbed to the topic this was sent to

                            _readWriteStream.Write(new PublishAck(publishMsg.PacketId));

                            break;
                        case MsgType.Connect:
                            var connectMsg = new Connect(buffer);

                            _logger.Log(LogLevel.Trace, $"Client '{connectMsg.ClientId}' connected");

                            _clientId = connectMsg.ClientId;

                            _keepAliveDisposable?.Dispose();

                            _keepAliveDisposable = Observable.Interval(TimeSpan.FromSeconds(connectMsg.KeepAlivePeriod + connectMsg.KeepAlivePeriod / 2)).Subscribe(_ =>
                            {
                                if (Interlocked.Exchange(ref _shouldCancel, 1) != 1) return;

                                _logger.Log(LogLevel.Warn, "Client appears to be disconnected, dropping connection");

                                _cancellationTokenSource.Cancel(false);
                            });

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
                            _publishThrottleEvent.Set(); //Throttling of sorts...
                            break;
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

            Interlocked.Exchange(ref _shouldCancel, 0); //Reset after all incoming messages
        }

        private void Subscribe(IEnumerable<string> topics) //TODO: Support wild cards in topic path, like: mytopic/#/anothertopic
        {
            foreach (var topic in topics)
            {
                if (_subscriptions.Contains(topic))
                    continue;

                _subscriptions.Add(topic);

                //_topicSubject.OnNext(new KeyValuePair<string, string>(_clientId, topic));

                if (topic.EndsWith("#"))
                {
                    var newtopic = topic.Replace("#", "");
                    _subscriptionDisposables.Add(_brokerPublishSubject.Where(m => m.MsgType == MsgType.Publish && m.Topic.StartsWith(newtopic)).Subscribe(OnNextPublish));
                }
                else
                {
                    _subscriptionDisposables.Add(_brokerPublishSubject.Where(m => m.MsgType == MsgType.Publish && m.Topic.Equals(topic)).Subscribe(OnNextPublish));
                }
                
                _logger.Log(LogLevel.Info, $"Subscribed to '{topic}'");
            }
        }
    }

}