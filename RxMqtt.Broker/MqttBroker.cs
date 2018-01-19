using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// Clients/sockets
        /// </summary>
        private static readonly List<Task> _clients = new List<Task>();

        public MqttBroker()
        {
            _logger.Log(LogLevel.Info, "Binding to all local addresses");
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
            _logger.Log(LogLevel.Info, $"Broker started on '{_ipAddress}'");

            var localEndPoint = new IPEndPoint(_ipAddress, 1883);
            var listener = new Socket(IPAddress.Any.AddressFamily, SocketType.Stream, ProtocolType.Tcp) {UseOnlyOverlappedIO = true};

            listener.Bind(localEndPoint);
            listener.Listen(5);

            _logger.Log(LogLevel.Info, "Broker listening on TCP port '1883'");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
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
            _clients.RemoveAll(t => t.IsCompleted);

            _logger.Log(LogLevel.Trace, $"Client connecting...");

            var listener = (Socket)asyncResult.AsyncState;
            var socket = listener.EndAccept(asyncResult);

            socket.UseOnlyOverlappedIO = true;

            _clients.Add(Task.Factory.StartNew(() =>
                                {
                                    var client = new Client(socket, _publishSubject);
                                    var completed = client.Start();
                                    _logger.Log(LogLevel.Trace, "Client task completed");
                                }
                                , TaskCreationOptions.LongRunning));

            _logger.Log(LogLevel.Trace, $"Client task created");

            _acceptConnectionResetEvent.Set();
        }
    }
}
