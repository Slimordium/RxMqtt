using System;
using System.IO;
using NLog;
using RxMqtt.Shared;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Shared
{
    internal class ReadWriteSync : IReadWriteStream
    {
        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();
        private readonly Stream _stream;

        internal ReadWriteSync(ref Stream stream)
        {
            _stream = stream;
        }

        /// <summary>
        /// Reads single packet (usually). This is a inefficent was to do things...
        /// </summary>
        /// <returns></returns>
        public void Read(Action<byte[]> callback)
        {
            if (_stream == null)
                throw new ArgumentNullException("ReadSync stream is null");

            byte[] newBuffer = null;

            try
            {
                var buffer = new byte[300000];
                var bytesIn = _stream.Read(buffer, 0, 2);

                if (bytesIn == 0 || buffer[0] == 0x00)
                    return;

                var msgType = (MsgType)(byte)((buffer[0] & 0xf0) >> (byte)MsgOffset.Type);

                _logger.Log(LogLevel.Trace, $"In <= '{msgType}'");

                if (msgType == MsgType.ConnectAck || msgType == MsgType.PingResponse)
                {
                    var rBuffer = new byte[2];

                    Buffer.BlockCopy(buffer, 0, rBuffer, 0, 2);

                    callback.Invoke(rBuffer);
                    return;
                }

                bytesIn += _stream.Read(buffer, 2, 3);

                var len = MqttMessage.DecodeValue(buffer, 1);

                var packetLength = len.Item1 + len.Item2 + 1;

                if (bytesIn == packetLength)
                {
                    newBuffer = new byte[packetLength];
                    Buffer.BlockCopy(buffer, 0, newBuffer, 0, packetLength);

                    callback.Invoke(newBuffer);
                    return;
                }

                _stream.Read(buffer, 5, packetLength);

                newBuffer = new byte[packetLength];

                Buffer.BlockCopy(buffer, 0, newBuffer, 0, packetLength);
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e.Message);    
            }

            callback.Invoke(newBuffer);
            return;
        }

        public void Write(MqttMessage message)
        {
            var buffer = message.GetBytes();

            _stream.Write(buffer, 0, buffer.Length);
        }
    }
}