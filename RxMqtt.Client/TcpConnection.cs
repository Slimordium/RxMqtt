using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using RxMqtt.Shared;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Client{
    internal class TcpConnection
    {
        internal ISubject<PacketEnvelope> PacketSyncSubject { get; }

        internal TcpConnection
        (
            string hostName,
            int keepAliveInSeconds,
            int port,
            int bufferLength,
            ref CancellationTokenSource cancellationTokenSource
        )
        {
            PacketSyncSubject = Subject.Synchronize(_packetSyncSubject);

            _cancellationTokenSource = cancellationTokenSource;

            _keepAliveInSeconds = (ushort) keepAliveInSeconds;
            _port = port;
            _bufferLength = bufferLength;
            _hostName = hostName;

            _keepAliveTimer = new Timer(Ping);
            _keepAliveTimer.Change((int)TimeSpan.FromSeconds(_keepAliveInSeconds).TotalMilliseconds, Timeout.Infinite);
        }

        public async Task<Status> Initialize()
        {
            try
            {
                if (!IPAddress.TryParse(_hostName, out _ipAddress))
                {
                    var entries = Dns.GetHostAddresses(_hostName);

                    if (entries.Any())
                    {
                        _ipAddress = entries.First();
                    }
                    else
                    {
                        _logger.Log(LogLevel.Trace, $"IP Address not found for {_hostName}");
                        return _status;
                    }
                }
               
                _socket = new Socket(_ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                {
                    UseOnlyOverlappedIO = true,
                    Blocking = true,
                    SendBufferSize = _bufferLength,
                    ReceiveBufferSize = _bufferLength
                };

                await _socket.ConnectAsync(new IPEndPoint(_ipAddress, _port));

                if (!_socket.Connected)
                    return Status.SocketError;

                _networkStream = new NetworkStream(_socket, true);

                _status = Status.Initialized;
              
                _readWriteStream = new ReadWriteStream(ref _networkStream, ProcessPackets, _bufferLength, ref _cancellationTokenSource);
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
            if (_status != Status.Initialized || _readWriteStream == null)
                throw new Exception("Not initialized");

            _readWriteStream.Write(mqttMessage);
        }

        private void ProcessPackets(byte[] packet) 
        {
            if (packet == null)
                return;

            try
            {
                var msgType = (MsgType)(byte)((packet[0] & 0xf0) >> (byte)MsgOffset.Type);

                switch (msgType)
                {
                    case MsgType.Publish:
                        var msg = new Publish(packet);
                        PacketSyncSubject.OnNext(new PacketEnvelope { MsgType = MsgType.Publish, PacketId = msg.PacketId, Message = msg });
                        _readWriteStream.Write(new PublishAck(msg.PacketId));

                        _logger.Log(LogLevel.Info, $"In <= {msgType} PacketId: {msg.PacketId}");
                        break;
                    case MsgType.ConnectAck:
                        PacketSyncSubject.OnNext(new PacketEnvelope { MsgType = MsgType.ConnectAck });
                        _logger.Log(LogLevel.Info, $"In <= {msgType}");
                        break;
                    case MsgType.PublishAck:
                        var pubAck = new PublishAck(packet.ToArray());
                        _logger.Log(LogLevel.Info, $"In <= {msgType} PacketId: {pubAck.PacketId}");
                        PacketSyncSubject.OnNext(new PacketEnvelope { MsgType = MsgType.PublishAck, PacketId = pubAck.PacketId, Message = pubAck });
                        break;
                    default:
                        _logger.Log(LogLevel.Info, $"In <= {msgType}");
                        break;
                }
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);
            }
        }

        private void Ping(object sender)
        {
            _readWriteStream.Write(new PingMsg());
            _keepAliveTimer.Change((int)TimeSpan.FromSeconds(_keepAliveInSeconds).TotalMilliseconds, Timeout.Infinite);
        }

        #region PrivateFields

        private static Socket _socket;
        private static NetworkStream _networkStream;

        private IPAddress _ipAddress;
        private readonly ushort _keepAliveInSeconds;

        private Status _status = Status.Error;
        private readonly int _port;
        private readonly int _bufferLength;
        private readonly ISubject<PacketEnvelope> _packetSyncSubject = new BehaviorSubject<PacketEnvelope>(null);
        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();
        private IReadWriteStream _readWriteStream;
        private readonly string _hostName;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly Timer _keepAliveTimer;

        #endregion
    }
}