using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NLog;
using RxMqtt.Shared;

namespace RxMqtt.Broker
{
    public class MqttBroker
    {
        private readonly ManualResetEvent _acceptConnectionResetEvent = new ManualResetEvent(false);

        private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        private readonly IPAddress _ipAddress = IPAddress.Any;

        /// <summary>
        /// Clients/sockets
        /// </summary>
        private static readonly ConcurrentDictionary<string, ClientState> _clientStates = new ConcurrentDictionary<string, ClientState>();

        /// <summary>
        /// Subscriptions by client
        /// </summary>
        private static readonly Dictionary<string, List<string>> _clientSubscriptions = new Dictionary<string, List<string>>();

        /// <summary>
        /// Recursivly handle read/write of all clients
        /// </summary>
        public MqttBroker()
        {
            _logger.Log(LogLevel.Warn, "Binding to all local addresses");
        }

        /// <summary>
        /// Recursivly handle read/write to all clients
        /// </summary>
        public MqttBroker(string ipAddress)
        {
            if (!IPAddress.TryParse(ipAddress, out _ipAddress))
                _logger.Log(LogLevel.Warn, "Could not parse IP Address, listening on all local addresses");
        }

        public void StartListening(CancellationToken cancellationToken)
        {
            _logger.Log(LogLevel.Info, $"Broker starting on '{_ipAddress}'");

            var localEndPoint = new IPEndPoint(_ipAddress, 1883);
            var listener = new Socket(IPAddress.Any.AddressFamily, SocketType.Stream, ProtocolType.Tcp) {UseOnlyOverlappedIO = true};

            listener.Bind(localEndPoint);
            listener.Listen(100);

            _logger.Log(LogLevel.Info, "Broker listening on 1883");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _acceptConnectionResetEvent.Reset();

                    listener.BeginAccept(AcceptConnectionCallback, listener);

                    //Wait 30 seconds for client connection to finish
                    _acceptConnectionResetEvent.WaitOne(TimeSpan.FromSeconds(30));
                }
                catch (Exception e)
                {
                    _logger.Log(LogLevel.Error, e);
                }
            }
        }

        private void AcceptConnectionCallback(IAsyncResult asyncResult)
        {
            _logger.Log(LogLevel.Trace, $"Client connecting...");

            var listener = (Socket)asyncResult.AsyncState;
            var socket = listener.EndAccept(asyncResult);

            var state = new ClientState { Socket = socket };

            socket.BeginReceive(state.Buffer, 0, 128000, 0, ReceiveCallback, state);

            _logger.Log(LogLevel.Trace, $"Client connected");

            _acceptConnectionResetEvent.Set();
        }

        private static readonly object _theLock = new object();

        static SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1);

        private void ReceiveCallback(IAsyncResult asyncResult)
        {
            _semaphoreSlim.Wait();

            try
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
                                _logger.Log(LogLevel.Warn, $"'{client.ClientId}' - recursive publish, ignoring message");
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

                        //Remove old state/socket if already exists
                        _clientStates.TryRemove(connectMsg.ClientId, out var tempClient);
                        _clientStates.TryAdd(connectMsg.ClientId, client);

                        //Remove subscriptions if already exists
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
                            if (_clientSubscriptions[client.ClientId].Contains(topic))
                                continue;//Already subscribed, ignore the request

                            _logger.Log(LogLevel.Trace, $"Adding subscription to topic '{topic}' for client '{client.ClientId}'");
                            _clientSubscriptions[client.ClientId].Add(topic);
                        }

                        BeginSend(client, new Shared.Messages.SubscribeAck(subscribeMsg.PacketId).GetBytes());
                        break;
                    case MsgType.PublishAck:
                        break;
                    default:
                        _logger.Log(LogLevel.Warn, $"Ignoring message: '{Encoding.UTF8.GetString(newBuffer)}'");
                        break;
                }

                socket.BeginReceive(client.Buffer, 0, 128000, 0, ReceiveCallback, client);
            }
            finally 
            {
                _semaphoreSlim.Release();
            }
        }

        private void BeginSend(ClientState clientState, byte[] buffer)
        {
            clientState.Socket.BeginSend(buffer, 0, buffer.Length, 0, SendCallback, clientState.Socket);
        }

        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                var socket = (Socket)ar.AsyncState;
                var bytesSent = socket.EndSend(ar);

                _logger.Log(LogLevel.Trace, $"Sent '{bytesSent}' bytes");
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);
            }
        }
    }
}
