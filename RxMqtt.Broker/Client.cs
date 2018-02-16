using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
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

        private readonly Socket _socket;

        private ILogger _logger = LogManager.GetCurrentClassLogger();

        //private readonly IObservable<byte[]> _clientReceiveObservable;





        private readonly List<string> _subscriptions = new List<string>();

        private readonly ISubject<Publish> _brokerPublishSubject;


        private readonly ISubject<KeyValuePair<string, string>> _topicSubject;

        private readonly IDisposable _readDisposable;
        private IDisposable _keepAliveDisposable;

        private readonly List<IDisposable> _subscriptionDisposables = new List<IDisposable>();

        private readonly CancellationTokenSource _cancellationTokenSource;

        private long _shouldCancel;



        internal Client(Socket socket, ISubject<Publish> brokerPublishSubject, ref CancellationTokenSource cancellationTokenSource)
        {

            _cancellationTokenSource = cancellationTokenSource;

            //_cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _socket = socket;
            //_topicSubject = topicSubject;

            _brokerPublishSubject = brokerPublishSubject;
            //_brokerPublishInObservable = brokerPublishSubject.AsObservable().ObserveOn(Scheduler.Default);

            _readDisposable = ReadSocket().ToObservable().SubscribeOn(Scheduler.Default).Subscribe(OnNextIncomingPacket);

          
        }

        private IEnumerable<byte[]> ReadSocket()
        {
            while (true)
            {
                byte[] packet = null;
                var buffer = new byte[200000];

                try
                {
                    var bytesIn = _socket.Receive(buffer, SocketFlags.None);

                    if (bytesIn == 0)
                        continue;

                    packet = new byte[bytesIn];

                    Array.Copy(buffer, 0, packet, 0, bytesIn);

                }
                catch (Exception e)
                {
                    _logger.Log(LogLevel.Info, e.Message);

                    
                    break;
                }

                yield return packet;
            }

            _readDisposable.Dispose();

            foreach (var subscriptionDisposable in _subscriptionDisposables)
            {
                subscriptionDisposable.Dispose();
            }

            _cancellationTokenSource.Cancel();
        }

        private void OnNextPublish(Publish mqttMessage)
        {
            //This sends the message to the client attached to this socket
            Send(mqttMessage);
        }

        private void OnNextIncomingPacket(byte[] buffer)
        {
            if (buffer.Length <= 1)
                return;

            var msgType = (MsgType)(byte)((buffer[0] & 0xf0) >> (byte)MsgOffset.Type);

            _logger.Log(LogLevel.Trace, $"In <= '{msgType}'");

            switch (msgType)
            {
                case MsgType.Publish:
                    var publishMsg = new Publish(buffer);

                    Send(new PublishAck(publishMsg.PacketId));

                    _brokerPublishSubject.OnNext(publishMsg); //Broadcast this message to any client that is subscirbed to the topic this was sent to

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

                    Send(new ConnectAck());
                    break;
                case MsgType.PingRequest:
                    

                    Send(new PingResponse());
                    break;
                case MsgType.Subscribe:
                    var subscribeMsg = new Subscribe(buffer);

                    Send(new SubscribeAck(subscribeMsg.PacketId));

                    Subscribe(subscribeMsg.Topics);
                    break;
                case MsgType.PublishAck:

                    break;
                case MsgType.Disconnect:

                    break;
                default:
                    _logger.Log(LogLevel.Warn, $"Ignoring message");
                    break;
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
                    _subscriptionDisposables.Add(_brokerPublishSubject.Where(m => m.MsgType == MsgType.Publish && m.Topic.StartsWith(newtopic)).SubscribeOn(Scheduler.Default).Subscribe(OnNextPublish));
                }
                else
                {
                    _subscriptionDisposables.Add(_brokerPublishSubject.Where(m => 
                                m.MsgType == MsgType.Publish && 
                                m.Topic.Equals(topic)
                                ).SubscribeOn(Scheduler.Default).Subscribe(OnNextPublish));
                }
                
                _logger.Log(LogLevel.Info, $"Subscribed to '{topic}'");
            }
        }

        private void Send(MqttMessage message)
        {
            _logger.Log(LogLevel.Info, $"Out => '{message.MsgType}'");

            try
            {
                if (!_socket.Connected)
                    return;

                _socket.Send(message.GetBytes());
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, $"Send => '{e.Message}'");
            }
        }
    }
}