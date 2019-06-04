using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using NLog;
using RxMqtt.Shared.Enums;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Shared
{
    internal class MqttStream : NetworkStream
    {
        private readonly CancellationTokenSource _cancellationTokenSourceSource = new CancellationTokenSource();
        private BinaryReader _binaryReader;
        private BinaryWriter _binaryWriter;
        private bool _disposed;
        private ILogger _logger;

        internal MqttStream(Socket socket) : base(socket, true)
        {
            _logger = LogManager.GetLogger($"ReadWriteStream-{DateTime.Now.Minute}.{DateTime.Now.Millisecond}");

            PacketObservable = PacketEnumerable().ToObservable(System.Reactive.Concurrency.Scheduler.Default);
        }

        internal IObservable<byte[]> PacketObservable { get; private set; }

        public void SetLogger(ILogger logger)
        {
            _logger = logger;
        }

        private IEnumerable<byte[]> PacketEnumerable()
        {
            var packetBuffer = new byte[256];
            var totalPacketLength = 0;
            var readLength = 0;
            var offset = 0;

            while (!_cancellationTokenSourceSource.IsCancellationRequested)
            {
                if (totalPacketLength > 0 && packetBuffer != null)
                {
                    if (readLength == 0)
                    {
                        totalPacketLength = 0;
                        offset = 0;
                        readLength = 0;
                        yield return packetBuffer;
                        packetBuffer = null;
                        continue;
                    }

                    var nb = ReadBytes(readLength);

                    if (nb == null)
                        break;

                    Buffer.BlockCopy(nb, 0, packetBuffer, offset, readLength);

                    totalPacketLength = 0;
                    offset = 0;
                    readLength = 0;

                    yield return packetBuffer;
                    packetBuffer = null;
                    continue;
                }

                var tempTypeByte = ReadBytes(1);

                if (tempTypeByte == null || tempTypeByte.Length == 0 || tempTypeByte[0] == 0x00)
                    break;

                var typeByte = tempTypeByte[0];

                var msgType = (MsgType) (byte) ((tempTypeByte[0] & 0xf0) >> (byte) MsgOffset.Type);

                byte[] rxBuffer = null;
                byte[] tempBuffer = null;

                _logger.Log(LogLevel.Debug, $"In <= {msgType}");

                switch (msgType)
                {
                    case MsgType.SubscribeAck:
                    case MsgType.UnsubscribeAck:
                    case MsgType.Connect:
                    case MsgType.ConnectAck:
                    case MsgType.PublishAck:

                        tempBuffer = ReadBytes(3);

                        if (tempBuffer == null || !tempBuffer.Any())
                        {
                            _cancellationTokenSourceSource.Cancel();
                            break;
                        }

                        rxBuffer = new byte[4];
                        rxBuffer[0] = typeByte;
                        Buffer.BlockCopy(tempBuffer, 0, rxBuffer, 1, 3);
                        break;
                    case MsgType.PingRequest:
                    case MsgType.PingResponse:
                        tempBuffer = ReadBytes(1);

                        if (tempBuffer == null || !tempBuffer.Any())
                        {
                            _cancellationTokenSourceSource.Cancel();
                            break;
                        }

                        totalPacketLength = 2; //It will always be 2

                        rxBuffer = new byte[2];
                        rxBuffer[0] = typeByte;
                        Buffer.BlockCopy(tempBuffer, 0, rxBuffer, 1, 1);
                        break;
                    case MsgType.Publish:
                    case MsgType.Subscribe:
                    case MsgType.Unsubscribe:
                        tempBuffer = ReadBytes(4);

                        if (tempBuffer == null || !tempBuffer.Any())
                        {
                            _cancellationTokenSourceSource.Cancel();
                            break;
                        }

                        rxBuffer = new byte[5];
                        rxBuffer[0] = typeByte;
                        Buffer.BlockCopy(tempBuffer, 0, rxBuffer, 1, 4);
                        break;
                }

                if (rxBuffer == null || rxBuffer.Length == 0)
                {
                    _logger.Log(LogLevel.Error, "rxBuffer was null or empty");

                    if (!_cancellationTokenSourceSource.IsCancellationRequested)
                        _cancellationTokenSourceSource.Cancel();

                    continue;
                }

                if (totalPacketLength == 0)
                    totalPacketLength = GetMessageLength(rxBuffer);

                if (totalPacketLength <= rxBuffer.Length)
                {
                    totalPacketLength = 0;
                    offset = 0;
                    readLength = 0;

                    yield return rxBuffer;

                    packetBuffer = null;
                    continue;
                }

                packetBuffer = new byte[totalPacketLength];

                Buffer.BlockCopy(rxBuffer, 0, packetBuffer, 0, rxBuffer.Length);

                readLength = totalPacketLength - rxBuffer.Length;

                offset = rxBuffer.Length;
            }
        }

        internal void Write(MqttMessage message)
        {
            if (message == null)
                return;

            var buffer = message.GetBytes();

            _logger.Log(LogLevel.Info, $"Out => '{message.MsgType}', '{buffer.Length}' bytes");

            WriteBytes(buffer);
        }

        private void WriteBytes(byte[] buffer)
        {
            if (_binaryWriter == null)
                _binaryWriter = new BinaryWriter(this, Encoding.UTF8, true);

            try
            {
                _binaryWriter.Write(buffer);
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e.Message);

                if (!_cancellationTokenSourceSource.IsCancellationRequested)
                    _cancellationTokenSourceSource.Cancel();
            }
        }

        private byte[] ReadBytes(int bytesToRead)
        {
            if (_binaryReader == null)
                _binaryReader = new BinaryReader(this, Encoding.UTF8, true);

            byte[] buffer = null;

            try
            {
                buffer = _binaryReader.ReadBytes(bytesToRead);
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e.Message);

                if (!_cancellationTokenSourceSource.IsCancellationRequested)
                    _cancellationTokenSourceSource.Cancel();
            }

            return buffer;
        }

        private static int GetMessageLength(IReadOnlyList<byte> buffer)
        {
            var decodeValue = MqttMessage.DecodeValue(buffer, 1);

            return decodeValue.Item1 + decodeValue.Item2 + 1;
        }

        private new void Dispose(bool disposing)
        {
            if (!disposing || _disposed) return;

            _disposed = true;

            if (!_cancellationTokenSourceSource.IsCancellationRequested)
                _cancellationTokenSourceSource.Cancel();

            base.Dispose();

            PacketObservable = null;
        }

        public new void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}