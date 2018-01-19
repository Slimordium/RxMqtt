using System;
using System.Collections.Generic;
using System.Net.Sockets;
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
        internal string ClientId { get; private set; }

        private readonly Socket _socket;

        private ILogger _logger = LogManager.GetCurrentClassLogger();

        private readonly Task _clientReceiveTask;

        internal CancellationTokenSource CancellationTokenSource { get; } = new CancellationTokenSource();

        private readonly IObservable<byte[]> _clientReceiveObservable;
        private readonly Subject<byte[]> _clientReceiveSubject = new Subject<byte[]>();

        private readonly IObservable<Publish> _brokerPublishObservable;
        private readonly Subject<Publish> _brokerPublishSubject;

        private readonly List<string> _subscriptions = new List<string>();

        internal Client(Socket socket, Subject<Publish> brokerPublishSubject)
        {
            _socket = socket;

            _brokerPublishObservable = brokerPublishSubject.AsObservable();
            _brokerPublishSubject = brokerPublishSubject;

            _clientReceiveObservable = _clientReceiveSubject.AsObservable();
            _clientReceiveObservable.Subscribe(OnNextPacket);

            CancellationTokenSource.Token.Register(() =>
            {
                _socket.Dispose();
            });

            _clientReceiveTask = new Task( () =>
            {
                while (!CancellationTokenSource.IsCancellationRequested)
                {
                    try
                    {
                        var buffer = new byte[128000];

                        var bytesIn = _socket.Receive(buffer, SocketFlags.None);

                        if (bytesIn == 0)
                            continue;

                        var newBuffer = new byte[bytesIn];

                        Array.Copy(buffer, 0, newBuffer, 0, bytesIn);

                        _clientReceiveSubject.OnNext(newBuffer);
                    }
                    catch (Exception e)
                    {
                        _logger.Log(LogLevel.Debug, e.Message);
                        MqttBroker.Disconnect(ClientId);
                        break;
                    }
                }
            }, CancellationTokenSource.Token, TaskCreationOptions.LongRunning);

            _clientReceiveTask.Start();
        }

        internal void OnNextPublish(Publish mqttMessage)
        {
            //This sends the message to the client attached to this socket
            Send(mqttMessage);
        }

        internal void OnNextPacket(byte[] buffer)
        {
            var msgType = (MsgType)(byte)((buffer[0] & 0xf0) >> (byte)MsgOffset.Type);

            _logger.Log(LogLevel.Trace, $"In <= {msgType}");

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

                    ClientId = connectMsg.ClientId;

                    _logger = LogManager.GetLogger(ClientId);

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
                    MqttBroker.Disconnect(ClientId);
                    break;
                default:
                    _logger.Log(LogLevel.Warn, $"Ignoring message: '{Encoding.UTF8.GetString(buffer)}'");
                    break;
            }
        }

        private void Subscribe(IEnumerable<string> topics) //TODO: Support wild cards in topic path, like: mytopic/#/anothertopic
        {
            foreach (var topic in topics)
            {
                if (_subscriptions.Contains(topic))
                    continue;

                _subscriptions.Add(topic);

                if (topic.EndsWith("#"))
                {
                    var newtopic = topic.Replace("#", "");
                    _brokerPublishObservable.Where(m => m.MsgType == MsgType.Publish && m.Topic.StartsWith(newtopic)).Subscribe(OnNextPublish);
                }
                else
                {
                    _brokerPublishObservable.Where(m => m.MsgType == MsgType.Publish && m.Topic.Equals(topic)).Subscribe(OnNextPublish);
                }
                
                _logger.Log(LogLevel.Info, $"Subscribed to '{topic}'");
            }
        }

        private void Send(MqttMessage message)
        {
            _logger.Log(LogLevel.Info, $"Out => {message.MsgType} '");

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