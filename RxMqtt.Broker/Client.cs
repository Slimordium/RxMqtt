using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using NLog;
using RxMqtt.Shared;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Broker
{
    internal class Client
    {
        private string _clientId;

        private readonly Socket _socket;

        private static ILogger _logger = LogManager.GetCurrentClassLogger();

        private readonly List<string> _subscriptions = new List<string>();

        private readonly ISubject<Publish> _brokerPublishSubject;

        private IDisposable _keepAliveDisposable;

        private readonly List<IDisposable> _subscriptionDisposables = new List<IDisposable>();

        private readonly CancellationTokenSource _cancellationTokenSource;

        private long _shouldCancel;

        internal  Client(Socket socket, ref ISubject<Publish> brokerPublishSubject, ref CancellationTokenSource cancellationTokenSource)
        {
            _cancellationTokenSource = cancellationTokenSource;
            _socket = socket;
            _brokerPublishSubject = brokerPublishSubject;

            BeginReceive();
        }

        private void OnNextPublish(Publish mqttMessage)
        {
            //This sends the message to the client attached to this socket
            BeginSend(mqttMessage);
        }

        private void BeginReceive()
        {
            var rs = new ReceiveState {Socket = _socket, Callback = ProcessPackets};

            try
            {
                var ar = _socket.BeginReceive(rs.Buffer, 0, rs.Buffer.Length, SocketFlags.None, EndReceive, rs);
                ar.AsyncWaitHandle.WaitOne();
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Trace, e.Message);
            }
        }

        private static void EndReceive(IAsyncResult asyncResult)
        {
            try
            {
                var ar = (ReceiveState)asyncResult.AsyncState;

                var bytesIn = ar.Socket.EndReceive(asyncResult);

                var newBuffer = new byte[bytesIn];

                Buffer.BlockCopy(ar.Buffer,0, newBuffer,0, bytesIn);

                ar.Callback.Invoke(newBuffer);
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Trace, e.Message);
            }
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

                            BeginSend(new PublishAck(publishMsg.PacketId));

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

                            BeginSend(new ConnectAck());
                            break;
                        case MsgType.PingRequest:


                            BeginSend(new PingResponse());
                            break;
                        case MsgType.Subscribe:
                            var subscribeMsg = new Subscribe(buffer);

                            BeginSend(new SubscribeAck(subscribeMsg.PacketId));

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
                }
                catch (Exception e)
                {
                    _logger.Log(LogLevel.Error, $"ProcessRead error '{e.Message}'");
                }
            }

            Interlocked.Exchange(ref _shouldCancel, 0); //Reset after all incoming messages

            BeginReceive();
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
                    _subscriptionDisposables.Add(_brokerPublishSubject.SubscribeOn(Scheduler.Default).Where(m => m.MsgType == MsgType.Publish && m.Topic.StartsWith(newtopic)).Subscribe(OnNextPublish));
                }
                else
                {
                    _subscriptionDisposables.Add(_brokerPublishSubject
                                .Where(m => m.MsgType == MsgType.Publish && m.Topic.Equals(topic))
                                .SubscribeOn(Scheduler.Default)
                                .Subscribe(OnNextPublish));
                }
                
                _logger.Log(LogLevel.Info, $"Subscribed to '{topic}'");
            }
        }

        private void BeginSend(MqttMessage message)
        {
            _logger.Log(LogLevel.Info, $"Out => '{message.MsgType}'");

            try
            {
                if (!_socket.Connected)
                    return;

                var buffer = message.GetBytes();

                var ar = _socket.BeginSend(buffer, 0, buffer.Length, SocketFlags.None, EndSend, new SendState {Socket = _socket});

                ar.AsyncWaitHandle.WaitOne();

            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, $"BeginSend => '{e.Message}'");
            }
        }

        private static void EndSend(IAsyncResult asyncResult)
        {
            var state = (SendState) asyncResult.AsyncState;

            state.Socket.EndSend(asyncResult);
            asyncResult.AsyncWaitHandle.WaitOne();
        }
    }

}