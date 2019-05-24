using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using NLog;
using RxMqtt.Shared;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Client
{
    internal class TcpConnection : IDisposable
    {
        internal TcpConnection
        (
            string hostName,
            int port
        )
        {
            PacketSubject = Subject.Synchronize(_packetSyncSubject);

            _port = port;
            _hostName = hostName;
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
               
                var socket = new Socket(_ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                {
                    UseOnlyOverlappedIO = true,
                    Blocking = true,
                };

                await socket.ConnectAsync(new System.Net.IPEndPoint(_ipAddress, _port));

                if (!socket.Connected)
                    return Status.SocketError;

                _status = Status.Initialized;
              
                _readWriteStream = new ReadWriteStream(new NetworkStream(socket, true));

                _readDisposable = _readWriteStream.PacketObservable.SubscribeOn(NewThreadScheduler.Default).Subscribe(ProcessPackets);
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);

                _status = Status.Error;
            }

            return _status;
        }

        public void Write(MqttMessage mqttMessage)
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

                        PacketSubject.OnNext(new PacketEnvelope { MsgType = MsgType.Publish, PacketId = msg.PacketId, Message = msg });
                        _readWriteStream.Write(new PublishAck(msg.PacketId));

                        _logger.Log(LogLevel.Info, $"In <= {msgType} PacketId: {msg.PacketId}");
                        break;
                    case MsgType.ConnectAck:
                        PacketSubject.OnNext(new PacketEnvelope { MsgType = MsgType.ConnectAck });
                        _logger.Log(LogLevel.Info, $"In <= {msgType}");
                        break;
                    case MsgType.PublishAck:
                        var pubAck = new PublishAck(packet.ToArray());
                        _logger.Log(LogLevel.Info, $"In <= {msgType} PacketId: {pubAck.PacketId}");
                        PacketSubject.OnNext(new PacketEnvelope { MsgType = MsgType.PublishAck, PacketId = pubAck.PacketId, Message = pubAck });
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

        #region PrivateFields

        private IPAddress _ipAddress;
        private Status _status = Status.Error;
        private readonly int _port;
        private readonly ISubject<PacketEnvelope> _packetSyncSubject = new BehaviorSubject<PacketEnvelope>(null);
        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();
        private ReadWriteStream _readWriteStream;
        private readonly string _hostName;
        private IDisposable _readDisposable;
        internal ISubject<PacketEnvelope> PacketSubject { get; }

        #endregion

        private void Dispose(bool disposing)
        {
            if (!disposing) return;

            _readDisposable?.Dispose();
            _readWriteStream?.Dispose();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}