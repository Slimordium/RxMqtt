using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Subjects;
using System.Threading;
using NLog;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Broker
{
    public class MqttBroker
    {
        private readonly AutoResetEvent _acceptConnectionResetEvent = new AutoResetEvent(false);

        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        private readonly IPAddress _ipAddress = IPAddress.Any;

        private readonly ISubject<Publish> _publishSubject = new Subject<Publish>(); //Everyone pushes to this

        readonly List<CancellationTokenSource> _cancellationTokenSources = new List<CancellationTokenSource>();

        readonly Dictionary<Guid, Client> _clients = new Dictionary<Guid, Client>();

        private readonly int _port = 1883;

        private IPEndPoint _ipEndPoint;

        private bool _started;

        private CancellationToken _cancellationToken;

        public MqttBroker()
        {
            _logger.Log(LogLevel.Info, "Binding to all local addresses, port 1883");
        }

        /// <summary>
        /// Recursivly handle read/write to all clients
        /// </summary>
        public MqttBroker(string ipAddress)
        {
            if (!IPAddress.TryParse(ipAddress, out _ipAddress))
                _logger.Log(LogLevel.Warn, "Could not parse IP Address, listening on all local addresses, port 1883");
        }

        public MqttBroker(string ipAddress, int port)
        {
            if (!IPAddress.TryParse(ipAddress, out _ipAddress))
                _logger.Log(LogLevel.Warn, $"Could not parse IP Address, listening on all local addresses, port {port}");

            _port = port;
        }

        private ISubject<Publish> _publishSyncSubject;

        public void StartListening(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_started)
            {
                _logger.Log(LogLevel.Warn, $"Already running");
                return;
            }

            _publishSyncSubject = Subject.Synchronize(_publishSubject);

            _started = true;

            _cancellationToken = cancellationToken;

            _logger.Log(LogLevel.Info, $"Broker started on '{_ipAddress}:{_port}'");

            _ipEndPoint = new IPEndPoint(_ipAddress, _port);
            var listener = new Socket(IPAddress.Any.AddressFamily, SocketType.Stream, ProtocolType.Tcp);//

            listener.Bind(_ipEndPoint);
            listener.Listen(20);

            while (!_cancellationToken.IsCancellationRequested)
            {
                try
                {
                    listener.BeginAccept(AcceptConnectionCallback, listener);

                    _acceptConnectionResetEvent.WaitOne();
                }
                catch (Exception e)
                {
                    _logger.Log(LogLevel.Error, e);
                    throw;
                }
            }
        }

        private void AcceptConnectionCallback(IAsyncResult asyncResult)
        {
            _logger.Log(LogLevel.Trace, $"Client connecting...");

            var listener = (Socket)asyncResult.AsyncState;
            var socket = listener.EndAccept(asyncResult);

            socket.UseOnlyOverlappedIO = true;
            socket.Blocking = true;
            socket.ReceiveBufferSize = 300000;
            socket.SendBufferSize = 300000;
            //socket.NoDelay = true;

            var cuid = Guid.NewGuid();
            var cts = new CancellationTokenSource();
            cts.Token.Register(() =>
            {
                _clients.Remove(cuid);
            });

            _cancellationTokenSources.Add(cts);

            var networkStream = new NetworkStream(socket);

            _clients.Add(cuid, new Client(ref networkStream, ref _publishSyncSubject, ref cts));

            _logger.Log(LogLevel.Trace, $"Client task created");

            _acceptConnectionResetEvent.Set();
        }
    }
}
