using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using NLog;
using RxMqtt.Shared.Messages;

// ReSharper disable PrivateFieldCanBeConvertedToLocalVariable
namespace RxMqtt.Shared
{
    internal class ReadWriteStream : IReadWriteStream
    {
        private ILogger _logger;
        private readonly NetworkStream _networkStream;
        private readonly IDisposable _disposable;
        private readonly Action<byte[]> _callback;
        private readonly CancellationTokenSource _cancellationTokenSourceSource;

        /// <summary>
        /// Complete messages will be passed to callback. If a read contains multiple messages, or partial messages, they will be assembled before the callback is invoked
        /// </summary>
        /// <param name="networkStream"></param>
        /// <param name="callback"></param>
        /// <param name="cancellationTokenSource"></param>
        internal ReadWriteStream(NetworkStream networkStream, Action<byte[]> callback, ref CancellationTokenSource cancellationTokenSource)
        {
            _cancellationTokenSourceSource = cancellationTokenSource;
            _logger = LogManager.GetLogger($"ReadWriteStream-{DateTime.Now.Minute}.{DateTime.Now.Millisecond}");

            _networkStream = networkStream;
            _callback = callback;

            _disposable = PacketEnumerable()
                .ToObservable()
                .SubscribeOn(Scheduler.Default)
                .Subscribe(_callback.Invoke);

            _cancellationTokenSourceSource.Token.Register(() => { _disposable?.Dispose(); });
        }

        public void SetLogger(ILogger logger)
        {
            _logger = logger;
        }

        private byte[] ReadBytes(int bytesToRead)
        {
            byte[] buffer = null;

            try
            {
                using (var br = new BinaryReader(_networkStream, Encoding.UTF8, true))
                {
                    buffer = br.ReadBytes(bytesToRead);
                }
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e.Message);

                if (!_cancellationTokenSourceSource.IsCancellationRequested)
                    _cancellationTokenSourceSource.Cancel();
            }

            return buffer;
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

                    _logger.Log(LogLevel.Info, $"Packet read completed");

                    totalPacketLength = 0;
                    offset = 0;
                    readLength = 0;

                    yield return packetBuffer;
                    packetBuffer = null;
                    continue;
                }

                var tempTypeByte = ReadBytes(1);

                if (tempTypeByte == null || tempTypeByte[0] == 0x00)
                    break;

                var typeByte = tempTypeByte[0];

                var msgType = ((MsgType)(byte)((tempTypeByte[0] & 0xf0) >> (byte)MsgOffset.Type));

                byte[] rxBuffer = null;
                byte[] tempBuffer = null;

                switch (msgType)
                {
                    case MsgType.Connect:
                    case MsgType.ConnectAck:
                    case MsgType.PublishAck:
                        tempBuffer = ReadBytes(3);

                        if (tempBuffer == null)
                            break;

                        rxBuffer = new byte[4];
                        rxBuffer[0] = typeByte;
                        Buffer.BlockCopy(tempBuffer, 0, rxBuffer, 1, 3);

                        break;
                    case MsgType.SubscribeAck:
                        tempBuffer = ReadBytes(2);

                        if (tempBuffer == null)
                            break;

                        rxBuffer = new byte[3];
                        rxBuffer[0] = typeByte;
                        Buffer.BlockCopy(tempBuffer, 0, rxBuffer, 1, 2);

                        break;
                    case MsgType.PingRequest:
                        break;
                    case MsgType.PingResponse:
                        break;
                    case MsgType.Publish:
                    case MsgType.Subscribe:
                        tempBuffer = ReadBytes(4);

                        if (tempBuffer == null)
                            break;

                        rxBuffer = new byte[5];
                        rxBuffer[0] = typeByte;
                        Buffer.BlockCopy(tempBuffer, 0, rxBuffer, 1, 4);
                        break;
                }

                if (rxBuffer == null)
                {
                    _logger.Log(LogLevel.Error, "rxBuffer was null");
                    break;
                }

                if (totalPacketLength == 0)
                {
                    totalPacketLength = GetMessageLength(rxBuffer);
                }

                if (totalPacketLength <= rxBuffer.Length)
                {
                    _logger.Log(LogLevel.Info, $"Packet read completed {rxBuffer.Length} bytes");

                    totalPacketLength = 0;
                    offset = 0;
                    readLength = 0;

                    yield return rxBuffer;

                    rxBuffer = null;
                    packetBuffer = null;
                    continue;
                }

                _logger.Log(LogLevel.Info, $"Packet read started for {totalPacketLength} bytes");

                packetBuffer = new byte[totalPacketLength];

                Buffer.BlockCopy(rxBuffer, 0, packetBuffer, 0, rxBuffer.Length);

                readLength = totalPacketLength - rxBuffer.Length;

                offset = rxBuffer.Length;
            }
        }

        public void Write(MqttMessage message)
        {
            if (message == null)
                return;

            //BeginWrite(message.GetBytes());
            WriteBinary(message.GetBytes());
        }

        private void WriteBinary(byte[] buffer)
        {
            var msgType = (MsgType)(byte)((buffer[0] & 0xf0) >> (byte)MsgOffset.Type);

            _logger.Log(LogLevel.Info, $"Out => '{msgType}', '{buffer.Length}' bytes - ");

            try
            {
                using (var writer = new BinaryWriter(_networkStream, Encoding.UTF8, true))
                {
                    writer.Write(buffer);
                }
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e.Message);

                if (!_cancellationTokenSourceSource.IsCancellationRequested)
                    _cancellationTokenSourceSource.Cancel();
            }
        }

        private void BeginWrite(byte[] buffer)
        {
            if (_cancellationTokenSourceSource.IsCancellationRequested)
                return;

            _logger.Log(LogLevel.Trace, $"Out bytes => {buffer.Length}");

            try
            {
                var ar = _networkStream.BeginWrite(buffer, 0, buffer.Length, EndWrite, _networkStream);
                ar.AsyncWaitHandle.WaitOne();
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e.Message);

                if (!_cancellationTokenSourceSource.IsCancellationRequested)
                    _cancellationTokenSourceSource.Cancel();
            }
        }

        public void Write(byte[] buffer)
        {
            if (buffer == null)
                return;

            //BeginWrite(buffer);
            WriteBinary(buffer);
        }

        private static IEnumerable<byte[]> ArraySplit(byte[] buffer, int segmentLength)
        {
            var arrayLength = buffer.Length;
            byte[] newBuffer = null;

            var i = 0;
            for (; arrayLength > (i + 1) * segmentLength; i++)
            {
                newBuffer = new byte[segmentLength];
                Buffer.BlockCopy(buffer, i * segmentLength, newBuffer, 0, segmentLength);
                yield return newBuffer;
            }

            var intBufforLeft = arrayLength - i * segmentLength;

            if (intBufforLeft <= 0)
                yield break;

            newBuffer = new byte[intBufforLeft];
            Buffer.BlockCopy(buffer, i * segmentLength, newBuffer, 0, intBufforLeft);
            yield return newBuffer;
        }

        private void EndWrite(IAsyncResult asyncResult)
        {
            try
            {
                var ar = (NetworkStream)asyncResult.AsyncState;
                ar.EndWrite(asyncResult);

            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e.Message);

                if (!_cancellationTokenSourceSource.IsCancellationRequested)
                    _cancellationTokenSourceSource.Cancel();
            }
        }

        /// <summary>
        /// Either returns a complete packet with the bool set to true, or a partial packet with the bool set to false
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        private static int GetMessageLength(IReadOnlyList<byte> buffer, int index = 1)
        {
            var decodeValue = MqttMessage.DecodeValue(buffer, 1);

            var r = decodeValue.Item1 + decodeValue.Item2;

            //if (decodeValue.Item2 == 1)
            ++r;

            return r;
        }
    }
}