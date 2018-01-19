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

        internal Socket Socket { get; }

        private ILogger _logger = LogManager.GetCurrentClassLogger();

        internal CancellationTokenSource CancellationTokenSource { get; } = new CancellationTokenSource();

        private readonly IObservable<byte[]> _clientReceiveObservable;
        private readonly IObservable<Publish> _brokerPublishObservable;

        private readonly List<string> _subscriptions = new List<string>();
        private readonly Subject<Publish> _brokerPublishSubject;

        internal Client(Socket socket, Subject<Publish> brokerPublishSubject)
        {
            Socket = socket;

            _brokerPublishSubject = brokerPublishSubject;
            _brokerPublishObservable = _brokerPublishSubject.AsObservable();

            _clientReceiveObservable = ReadSocket().ToObservable();
        }

        internal bool Start()
        {
            _clientReceiveObservable.Subscribe(OnNextIncomingPacket); //Blocks

            return true;
        }

        private IEnumerable<byte[]> ReadSocket()
        {
            while (!CancellationTokenSource.IsCancellationRequested)
            {
                byte[] packet = null;
                var buffer = new byte[128000];

                try
                {
                    var bytesIn = Socket.Receive(buffer, SocketFlags.None);

                    if (bytesIn == 0)
                        continue;

                    packet = new byte[bytesIn];

                    Array.Copy(buffer, 0, packet, 0, bytesIn);
                }
                catch (Exception e)
                {
                    _logger.Log(LogLevel.Warn, e.Message);
                    break;
                }

                yield return packet;
            }
        }

        internal void Dispose()
        {
            try
            {
                Socket.Dispose();
                CancellationTokenSource.Cancel();
            }
            catch (Exception)
            {
                //


            }
        }

        internal void OnNextPublish(Publish mqttMessage)
        {
            //This sends the message to the client attached to this socket
            Send(mqttMessage);
        }

        internal void OnNextIncomingPacket(byte[] buffer)
        {
            if (buffer.Length <= 1)
                return;

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
                if (!Socket.Connected)
                    return;

                Socket.Send(message.GetBytes());
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, $"Send => '{e.Message}'");
            }
        }
    }
}