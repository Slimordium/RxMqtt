using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using NLog;
using RxMqtt.Shared.Enums;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Broker
{
    public class MqttBroker : IDisposable
    {
        private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        private static readonly ConcurrentDictionary<Guid, Client> _clients = new ConcurrentDictionary<Guid, Client>();

        internal static ISubject<Publish> PublishSyncSubject;
        private readonly AutoResetEvent _acceptConnectionResetEvent = new AutoResetEvent(false);

        private readonly IPAddress _ipAddress = IPAddress.Any;

        private readonly int _port = 1883;

        private readonly ISubject<Publish> _publishSubject = new Subject<Publish>(); //Everyone pushes to this

        private CancellationToken _cancellationToken;

        private IDisposable _disposeAtIntervalDisposable;

        private IPEndPoint _ipEndPoint;

        private bool _started;

        public MqttBroker()
        {
            _logger.Log(LogLevel.Info, "Binding to all local addresses, port 1883");
        }

        public MqttBroker(string ipAddress)
        {
            if (!IPAddress.TryParse(ipAddress, out _ipAddress))
                _logger.Log(LogLevel.Warn, "Could not parse IP Address, listening on all local addresses, port 1883");
        }

        public MqttBroker(string ipAddress, int port)
        {
            if (!IPAddress.TryParse(ipAddress, out _ipAddress))
                _logger.Log(LogLevel.Warn,
                    $"Could not parse IP Address, listening on all local addresses, port {port}");

            _port = port;
        }

        internal static IObservable<Publish> Subscribe(string topic)
        {
            return PublishSyncSubject
                .Where(message => message != null
                                  && message.MsgType == MsgType.Publish
                                  && message.IsTopicMatch(topic))
                .ObserveOn(Scheduler.Default);
        }

        public void StartListening(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_started)
            {
                _logger.Log(LogLevel.Warn, "Already running");
                return;
            }

            _disposeAtIntervalDisposable = Observable
                .Interval(TimeSpan.FromSeconds(3))
                .ObserveOn(Scheduler.Default) //Was subscribe on NewThreadScheduler
                .Subscribe(_ => DisposeDisconnectedClients());

            PublishSyncSubject = Subject.Synchronize(_publishSubject);

            _started = true;

            _cancellationToken = cancellationToken;

            _logger.Log(LogLevel.Info, $"Broker started on '{_ipAddress}:{_port}'");

            _ipEndPoint = new IPEndPoint(_ipAddress, _port);
            var listener = new Socket(IPAddress.Any.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, true);

            listener.Bind(_ipEndPoint);
            listener.Listen(25);

            while (!_cancellationToken.IsCancellationRequested)
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

            Dispose();
        }

        private void AcceptConnectionCallback(IAsyncResult asyncResult)
        {
            _logger.Log(LogLevel.Trace, "Client connecting...");

            var listener = (Socket) asyncResult.AsyncState;
            var socket = listener.EndAccept(asyncResult);

            socket.UseOnlyOverlappedIO = true;
            socket.Blocking = true;

            _clients.TryAdd(Guid.NewGuid(), new Client(socket));

            _logger.Log(LogLevel.Trace, "Client task created");

            _acceptConnectionResetEvent.Set();
        }

        private static void DisposeDisconnectedClients()
        {
            foreach (var c in _clients.Where(c => !c.Value.IsConnected()))
            {
                if (!_clients.TryRemove(c.Key, out var client))
                    Thread.Sleep(1);

                client?.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private static bool _disposed;

        private void Dispose(bool disposing)
        {
            if (!disposing || _disposed) return;

            _disposed = true;

            foreach (var c in _clients)
            {
                if (!_clients.TryRemove(c.Key, out var client))
                    Thread.Sleep(1);

                client?.Dispose();
            }

            _acceptConnectionResetEvent?.Dispose();
            _disposeAtIntervalDisposable?.Dispose();
        }
    }
}