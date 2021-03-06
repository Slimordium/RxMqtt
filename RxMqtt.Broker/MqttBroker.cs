﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using NLog;
using RxMqtt.Shared.Enums;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Broker
{
    public class MqttBroker
    {
        private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        private static readonly ConcurrentDictionary<Guid, ConnectedClient> _clients = new ConcurrentDictionary<Guid, ConnectedClient>();

        internal static ISubject<Publish> PublishSyncSubject { get; private set; }
        private readonly AutoResetEvent _acceptConnectionResetEvent = new AutoResetEvent(false);

        private readonly IPAddress _ipAddress = IPAddress.Any;

        private readonly int _port = 1883;

        private readonly ISubject<Publish> _publishSubject = new Subject<Publish>();

        private CancellationToken _cancellationToken;

        private IDisposable _disposeDisconnectedClientsIntervalDisposable;

        private IDisposable _statsPublishDisposable;

        private IPEndPoint _ipEndPoint;

        private bool _started;

        private long _publishCount;

        private long _lastCount;

        private IDisposable _publishCountDisposable;

        private DateTime _startTime;

        public MqttBroker()
        {
            _logger.Log(LogLevel.Info, "Binding to all local addresses, port 1883");
        }

        /// <summary>
        ///     Recursively handle read/write to all clients
        /// </summary>
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
                .Where(message => message != null && message.IsTopicMatch(topic))
                .ObserveOn(Scheduler.Default);
        }

        public void StartListening(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_started)
            {
                _logger.Log(LogLevel.Warn, "Already running");
                return;
            }

            _startTime = DateTime.Now;

            _started = true;

            _disposeDisconnectedClientsIntervalDisposable = Observable
                .Interval(TimeSpan.FromSeconds(3))
                .ObserveOn(Scheduler.Default)
                .Subscribe(_ => DisposeDisconnectedClients());

            _statsPublishDisposable = Observable
                .Interval(TimeSpan.FromSeconds(5))
                .ObserveOn(Scheduler.Default)
                .Subscribe(_ => PublishStats());

            PublishSyncSubject = Subject.Synchronize(_publishSubject);

            _publishCountDisposable = PublishSyncSubject
                .ObserveOn(Scheduler.Default)
                .Subscribe(_ => Interlocked.Increment(ref _publishCount));

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
        }

        private void AcceptConnectionCallback(IAsyncResult asyncResult)
        {
            _logger.Log(LogLevel.Trace, "Client connecting...");

            var listener = (Socket) asyncResult.AsyncState;
            var socket = listener.EndAccept(asyncResult);

            _clients.TryAdd(Guid.NewGuid(), new ConnectedClient(socket));

            _logger.Log(LogLevel.Trace, "Client task created");

            _acceptConnectionResetEvent.Set();
        }

        private void PublishStats()
        {
            var pubCount = Interlocked.Read(ref _publishCount);

            var mps = (pubCount - _lastCount) / 5;//How many msgs per second?

            _lastCount = pubCount;

            var msg = Encoding.UTF8.GetBytes($"RunTime:{DateTime.Now - _startTime};PublishCount:{pubCount};MPS:{mps};ConnectedClients:{_clients.Count}");

            var statsMsg = new Publish {Topic = "$stats", Message = msg};

            PublishSyncSubject.OnNext(statsMsg);
        }

        private static void DisposeDisconnectedClients()
        {
            foreach (var ce in _clients.Where(c => !c.Value.IsConnected()))
            {
                try
                {
                    _clients.TryRemove(ce.Key, out var client);

                    client?.Dispose();
                }
                catch (Exception e)
                {
                    _logger.Log(LogLevel.Error, $"Error disposing client => '{e.Message}'");
                }
            }
        }
    }
}