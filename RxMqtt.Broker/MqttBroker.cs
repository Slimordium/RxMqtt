using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Broker
{
    public class MqttBroker
    {
        private readonly AutoResetEvent _acceptConnectionResetEvent = new AutoResetEvent(false);

        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        private readonly IPAddress _ipAddress = IPAddress.Any;

        private readonly Subject<Publish> _publishSubject = new Subject<Publish>();

        private readonly int _port = 1883;

        private IPEndPoint _ipEndPoint;

        private volatile bool _started;

        private CancellationTokenSource _cancellationTokenSource;

        /// <summary>
        /// Clients/sockets
        /// </summary>
        private readonly List<Task> _clients = new List<Task>();

        ~MqttBroker()
        {
            _cancellationTokenSource.Cancel();
        }

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

        public void StartListening(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_started)
            {
                _logger.Log(LogLevel.Warn, $"Already running");
                return;
            }

            _started = true;

            _cancellationTokenSource = cancellationToken != default(CancellationToken) ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : new CancellationTokenSource();

            _logger.Log(LogLevel.Info, $"Broker started on '{_ipAddress}:{_port}'");

            _ipEndPoint = new IPEndPoint(_ipAddress, _port);
            var listener = new Socket(IPAddress.Any.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { UseOnlyOverlappedIO = true };

            listener.Bind(_ipEndPoint);
            listener.Listen(15);

            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    listener.BeginAccept(AcceptConnectionCallback, listener);

                    _acceptConnectionResetEvent.WaitOne();
                }
                catch (Exception e)
                {
                    _logger.Log(LogLevel.Error, e);
                    _cancellationTokenSource.Cancel();
                    throw;
                }
            }
        }

        private void AcceptConnectionCallback(IAsyncResult asyncResult)
        {
            _clients.RemoveAll(t => t.IsCompleted);

            _logger.Log(LogLevel.Trace, $"Client connecting...");

            var listener = (Socket)asyncResult.AsyncState;
            var socket = listener.EndAccept(asyncResult);

            socket.UseOnlyOverlappedIO = true;

            _clients.Add(Task.Factory.StartNew(() =>
                {
                    var client = new Client(socket, _publishSubject, CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token).Token);
                    var completed = client.Start();
                    _logger.Log(LogLevel.Trace, "Client task completed");
                }
                , TaskCreationOptions.LongRunning));

            _logger.Log(LogLevel.Trace, $"Client task created");

            _acceptConnectionResetEvent.Set();
        }
    }
}
