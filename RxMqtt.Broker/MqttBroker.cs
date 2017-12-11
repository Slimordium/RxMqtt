using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using RxMqtt.Shared;

namespace RxMqtt.Broker
{
    public class MqttBroker
    {
        private readonly ManualResetEvent _acceptConnectionResetEvent = new ManualResetEvent(false);
        private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        public void StartListening(CancellationToken cancellationToken)
        {
            var localEndPoint = new IPEndPoint(IPAddress.Any, 1883);
            var listener = new Socket(IPAddress.Any.AddressFamily, SocketType.Stream, ProtocolType.Tcp) {UseOnlyOverlappedIO = true};

            listener.Bind(localEndPoint);
            listener.Listen(100);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _acceptConnectionResetEvent.Reset();

                    listener.BeginAccept(AcceptConnectionCallback, listener);

                    _acceptConnectionResetEvent.WaitOne();
                }
                catch (Exception e)
                {
                    _logger.Log(LogLevel.Error, e);
                }
            }
        }

        private void AcceptConnectionCallback(IAsyncResult asyncResult)
        {
            var listener = (Socket)asyncResult.AsyncState;
            var socket = listener.EndAccept(asyncResult);

            _logger.Log(LogLevel.Trace, $"Client connecting...");

            var state = new ClientState
            {
                Socket = socket
            };

            socket.BeginReceive(state.Buffer, 0, 128000, 0, ReceiveCallback, state);

            _acceptConnectionResetEvent.Set();
        }

        private static readonly Dictionary<string, ClientState> _clientStates = new Dictionary<string, ClientState>();

        private static readonly Dictionary<string, List<string>> _clientSubscriptions = new Dictionary<string, List<string>>();

        private static void ReceiveCallback(IAsyncResult asyncResult)
        {
            var client = (ClientState)asyncResult.AsyncState;
            var socket = client.Socket;
            var bytesIn = 0;

            try
            {
                bytesIn = socket.EndReceive(asyncResult);
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);
                return;
            }

            if (bytesIn <= 0)
                return;

            var newBuffer = new byte[bytesIn];

            Array.Copy(client.Buffer, 0, newBuffer, 0, bytesIn);

            client.Buffer = new byte[128000]; 

            var msgType = (MsgType)(byte)((newBuffer[0] & 0xf0) >> (byte)MsgOffset.Type);

            _logger.Log(LogLevel.Trace, $"In <= {msgType}");

            switch (msgType)
            {
                case MsgType.Publish:
                    var publishMsg = new Shared.Messages.Publish(newBuffer);

                    foreach (var subscriptionMapping in _clientSubscriptions.Where(subscription => subscription.Value.Contains(publishMsg.Topic)).ToList())
                    {
                        if (subscriptionMapping.Key.Equals(client.ClientId))
                        {
                            _logger.Log(LogLevel.Warn, "Recursive publish, ignoring");
                            continue;
                        }

                        _logger.Log(LogLevel.Trace, $"'{subscriptionMapping.Key}' is subscribed to '{publishMsg.Topic}', Publishing message");
                        BeginSend(_clientStates[subscriptionMapping.Key], new Shared.Messages.Publish(publishMsg.Topic, publishMsg.Message).GetBytes());
                    }
                    break;
                case MsgType.Connect:
                    var connectMsg = new Shared.Messages.Connect(newBuffer);

                    _logger.Log(LogLevel.Trace, $"Client '{connectMsg.ClientId}' connected. Sending ConnectAck");

                    client.ClientId = connectMsg.ClientId;

                    _clientStates.Remove(connectMsg.ClientId);
                    _clientStates.Add(connectMsg.ClientId, client);
                    _clientSubscriptions.Remove(connectMsg.ClientId);
                    _clientSubscriptions.Add(connectMsg.ClientId, new List<string>());

                    BeginSend(client, new Shared.Messages.ConnectAck().GetBytes());
                    break;
                case MsgType.PingRequest:
                    _logger.Log(LogLevel.Trace, $"Sending ping response '{client.ClientId}'");
                    BeginSend(client, new Shared.Messages.PingResponse().GetBytes());
                    break;
                case MsgType.Subscribe:
                    var subscribeMsg = new Shared.Messages.Subscribe(newBuffer);

                    foreach (var topic in subscribeMsg.Topics)
                    {
                        if (!_clientSubscriptions[client.ClientId].Contains(topic))
                        {
                            _logger.Log(LogLevel.Trace, $"Adding subscription to topic '{topic}' for client '{client.ClientId}'");
                            _clientSubscriptions[client.ClientId].Add(topic);
                        }
                    }

                    BeginSend(client, new Shared.Messages.SubscribeAck(subscribeMsg.PacketId).GetBytes());
                    break;
                default:
                    _logger.Log(LogLevel.Warn, $"Unknown message: {Encoding.UTF8.GetString(newBuffer)}");
                    break;
            }

            socket.BeginReceive(client.Buffer, 0, 128000, 0, ReceiveCallback, client);
        }

        private static void BeginSend(ClientState clientState, byte[] buffer)
        {
            clientState.Socket.BeginSend(buffer, 0, buffer.Length, 0, SendCallback, clientState.Socket);
        }

        private static void SendCallback(IAsyncResult ar)
        {
            try
            {
                var socket = (Socket)ar.AsyncState;
                var bytesSent = socket.EndSend(ar);

                _logger.Log(LogLevel.Trace, $"Sent {bytesSent} bytes");
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);
            }
        }
    }
}
