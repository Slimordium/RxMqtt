﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reactive.Subjects;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using RxMqtt.Shared;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Client{
    internal class TcpConnection{
        private Status _status = Status.Error;
        private readonly int _port;

        internal ISubject<PacketEnvelope> PacketSyncSubject { get; }

        protected ISubject<PacketEnvelope> _packetSyncSubject = new BehaviorSubject<PacketEnvelope>(null);

        protected ILogger _logger = LogManager.GetCurrentClassLogger();

        private IReadWriteStream _readWriteStream;

        protected string HostName;

        private CancellationTokenSource _cancellationTokenSource;

        internal TcpConnection
        (
            string connectionId,
            string hostName,
            int keepAliveInSeconds,
            int port,
            ref CancellationTokenSource cancellationTokenSource,
            string certFileName = "",
            string pfxPw = ""
        )
        {
            PacketSyncSubject = Subject.Synchronize(_packetSyncSubject);

            _cancellationTokenSource = cancellationTokenSource;

            _keepAliveInSeconds = (ushort) keepAliveInSeconds;
            _connectionId = connectionId;
            _pfxFileName = certFileName;
            _certPassword = pfxPw;
            _port = port;
            HostName = hostName;
        }

        public async Task<Status> Initialize()
        {
            try
            {
                var localDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().CodeBase)?.Replace(@"file:\", "");

                if (!string.IsNullOrEmpty(_pfxFileName) && !string.IsNullOrEmpty(_certPassword))
                {
                    _logger.Log(LogLevel.Trace, $@"Certificate directory: {localDirectory}\{_pfxFileName}");

                    _certificate = new X509Certificate2($@"{localDirectory}\{_pfxFileName}", _certPassword);
                }

                // in this case the parameter remoteHostName isn't a valid IP address
                if (!IPAddress.TryParse(HostName, out _ipAddress))
                {
                    var entries = Dns.GetHostAddresses(HostName);

                    if (entries.Any())
                    {
                        _ipAddress = entries.First();
                    }
                    else
                    {
                        _logger.Log(LogLevel.Trace, $"IP Address not found for {HostName}");
                        return _status;
                    }
                }

                _status = await InitializeConnection(_port);

                if (_status == Status.Initialized)
                {
                    _readWriteStream = new ReadWriteAsync(ref _networkStream, ProcessPackets, ref _cancellationTokenSource, ref _logger);
                }
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);

                _status = Status.Error;
            }

            return _status;
        }

        internal void Write(MqttMessage mqttMessage)
        {
            _readWriteStream.Write(mqttMessage);
        }

        private async Task<Status> InitializeConnection(int port = 8883)
        {
            var status = Status.Error;

            try
            {
                _socket = new Socket(_ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                {
                    UseOnlyOverlappedIO = true,
                    Blocking = true,
                };

                await _socket.ConnectAsync(new IPEndPoint(_ipAddress, port));

                if (!_socket.Connected)
                    return Status.SocketError;

                var networkStream = new NetworkStream(_socket, true);

                //if (_certificate != null)
                //{
                //    var sslStream = new SslStream(networkStream, false, null, null);

                //    _x509CertificateCollection = new X509CertificateCollection(new X509Certificate[] {_certificate});

                //    await sslStream.AuthenticateAsClientAsync(HostName, _x509CertificateCollection, SslProtocols.Tls12, true);

                //    _networkStream = sslStream;

                //    if (!sslStream.IsAuthenticated || !sslStream.IsEncrypted || !sslStream.IsMutuallyAuthenticated)
                //    {
                //        _logger.Log(LogLevel.Trace, "SSL Authentication failed");
                //        return Status.SslError;
                //    }
                //}
                //else
                //{
                    _networkStream = networkStream;
                //}

                status = Status.Initialized;
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);
            }

            return status;
        }
        private void ProcessPackets(byte[] nextBuffer) 
        {
            if (nextBuffer == null)
                return;

            var msgType = (MsgType)(byte)((nextBuffer[0] & 0xf0) >> (byte)MsgOffset.Type);

            _logger.Log(LogLevel.Info, $"In <= {msgType}");

            switch (msgType)
            {
                case MsgType.Publish:
                    var msg = new Publish(nextBuffer);
                    PacketSyncSubject.OnNext(new PacketEnvelope { MsgType = MsgType.Publish, PacketId = msg.PacketId, Message = msg });
                    _readWriteStream.Write(new PublishAck(msg.PacketId));
                    break;
                case MsgType.ConnectAck:
                    PacketSyncSubject.OnNext(new PacketEnvelope { MsgType = MsgType.ConnectAck });
                    break;
                case MsgType.PingResponse:
                    break;
                case MsgType.PublishAck:
                    var pubAck = new PublishAck(nextBuffer.ToArray());

                    PacketSyncSubject.OnNext(new PacketEnvelope { MsgType = MsgType.PublishAck, PacketId = pubAck.PacketId, Message = pubAck });
                    break;
            }
        }

        #region PrivateFields

        private static Socket _socket;
        private static NetworkStream _networkStream;
        private readonly string _pfxFileName;
        private readonly string _certPassword;

        private IPAddress _ipAddress;
        private X509Certificate2 _certificate;
        private X509CertificateCollection _x509CertificateCollection;
        private ushort _keepAliveInSeconds;
        private string _connectionId;

        #endregion
    }
}