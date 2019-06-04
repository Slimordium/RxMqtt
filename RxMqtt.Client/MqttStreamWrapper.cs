using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using NLog;
using RxMqtt.Shared;
using RxMqtt.Shared.Enums;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Client
{
    internal class MqttStreamWrapper : IDisposable
    {
        internal MqttStreamWrapper
        (
            string hostName,
            int port
        )
        {
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

                var socket = new Socket(_ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, true);

                await socket.ConnectAsync(new IPEndPoint(_ipAddress, _port));

                if (!socket.Connected)
                    return Status.SocketError;

                _status = Status.Initialized;
              
                _readWriteStream = new MqttStream(socket);

                _readDisposable = _readWriteStream.PacketObservable.Subscribe(ParsePacket);
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, $"Initialize => {e}");

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

        private void ParsePacket(byte[] packet) 
        {
            if (packet == null)
                return;

            try
            {
                var msgType = (MsgType)(byte)((packet[0] & 0xf0) >> (byte)MsgOffset.Type);

                var packetId = 0;

                switch (msgType)
                {
                    case MsgType.Publish:
                        var msg = new Publish(packet);

                        PacketSubject.OnNext(new PacketEnvelope { MsgType = MsgType.Publish, PacketId = msg.PacketId, Message = msg });
                        _readWriteStream.Write(new PublishAck(msg.PacketId));

                        packetId = msg.PacketId;
                        
                        break;
                    case MsgType.ConnectAck:
                        PacketSubject.OnNext(new PacketEnvelope { MsgType = MsgType.ConnectAck });
                        break;
                    case MsgType.PublishAck:
                        var pubAck = new PublishAck(packet.ToArray());
                        packetId = pubAck.PacketId;
                        PacketSubject.OnNext(new PacketEnvelope { MsgType = MsgType.PublishAck, PacketId = pubAck.PacketId, Message = pubAck });
                        break;
                    case MsgType.SubscribeAck:
                        var subAck = new SubscribeAck(packet.ToArray());
                        packetId = subAck.PacketId;
                        PacketSubject.OnNext(new PacketEnvelope { MsgType = MsgType.SubscribeAck, PacketId = subAck.PacketId, Message = subAck });
                        break;
                    case MsgType.UnsubscribeAck:
                        var unsubAck = new UnsubscribeAck(packet.ToArray());
                        packetId = unsubAck.PacketId;
                        PacketSubject.OnNext(new PacketEnvelope { MsgType = MsgType.UnsubscribeAck, PacketId = unsubAck.PacketId, Message = unsubAck });
                        break;
                    case MsgType.PingResponse:
                        break;
                    default:
                        _logger.Log(LogLevel.Warn, $"Unhandled message type In <= {msgType}");
                        break;
                }

                _logger.Log(LogLevel.Info, $"In <= {msgType} PacketId: {packetId}");
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, $"ProcessPackets => {e}");
            }
        }

        #region PrivateFields

        private IPAddress _ipAddress;
        private Status _status = Status.Error;
        private readonly int _port;
        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();
        private MqttStream _readWriteStream;
        private readonly string _hostName;
        private IDisposable _readDisposable;
        internal ISubject<PacketEnvelope> PacketSubject { get; } = new BehaviorSubject<PacketEnvelope>(new PacketEnvelope());

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