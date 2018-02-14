
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NLog;
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("RxMqtt.Broker")]
[assembly: InternalsVisibleTo("RxMqtt.Client")]

namespace RxMqtt.Shared.Messages
{
    public class Publish : MqttMessage{
        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        private const byte DupFlagMask = 0x08;
        private const byte DupFlagOffset = 0x03;

        private const byte QosLevelMask = 0x06;
        private const byte QosLevelOffset = 0x01;

        private const byte RetainFlagMask = 0x01;
        private const byte RetainFlagOffset = 0x00;

        internal byte[] Message { get; set; } = new byte[1];

        internal string Topic { get; set; } = string.Empty;

        internal bool Retain { get; set; }

        internal Publish()
        {
            MsgType = MsgType.Publish;
        }

        internal Publish(string topic, byte[] message)
        {
            MsgType = MsgType.Publish;
            Topic = topic;
            Message = message ?? new byte[] { 0x00 };

            if (message == null || message.Length > 1e+7)
                throw new ArgumentOutOfRangeException("Invalid buffer length! Maximum is 1e+7 and cannot be null");
        }

        internal Publish(byte[] buffer)
        {
            if (buffer.Length > 1e+7)
                throw new ArgumentOutOfRangeException("Invalid buffer length! Maximum is 1e+7 ");

            try
            {
                var newBuffer = new List<byte>(buffer);

                newBuffer = newBuffer.GetRange(1, newBuffer.Count - 1);

                var publishMessageLength = 0;
                var index = 0;
                var bufferAfterGetRemaining = new List<byte>();

                foreach (var @byte in newBuffer)
                {
                    index++;

                    if (index == 1 && @byte < 0x7f)
                    {
                        publishMessageLength = Convert.ToUInt16(@byte);
                        bufferAfterGetRemaining = newBuffer.GetRange(index, newBuffer.Count - index);
                        break;
                    }

                    if (@byte > 0x7f)
                    {
                        publishMessageLength = publishMessageLength + Convert.ToUInt16(@byte);
                    }
                    else
                    {
                        publishMessageLength = publishMessageLength + @byte * 128;
                        bufferAfterGetRemaining = newBuffer.GetRange(index, newBuffer.Count - index);
                        break;
                    }
                }

                var topicLength = 0;
                index = 0;

                foreach (var @byte in bufferAfterGetRemaining)
                {
                    index++;

                    if (index == 1 && @byte < 0x7f && @byte != 0x00)
                    {
                        topicLength = Convert.ToUInt16(@byte);

                        break;
                    }

                    if (index > 1 && @byte < 0x7f && @byte != 0x00)
                    {
                        topicLength = Convert.ToUInt16(@byte);

                        break;
                    }

                    if (@byte > 0x7f)
                    {
                        publishMessageLength = publishMessageLength + Convert.ToUInt16(@byte);
                    }

                    if (@byte > 0x00 && @byte <= 0x74 && index > 1)
                    {
                        topicLength = publishMessageLength + @byte * 128;

                        break;
                    }
                }

                Topic = Encoding.UTF8.GetString(bufferAfterGetRemaining.GetRange(index, topicLength).ToArray());

                var bufferAfterGetTopic = bufferAfterGetRemaining.GetRange(index + topicLength, bufferAfterGetRemaining.Count - (index + topicLength));

                PacketId = Convert.ToUInt16(bufferAfterGetTopic[0] + bufferAfterGetTopic[1]);

                Message = bufferAfterGetTopic.GetRange(2, bufferAfterGetTopic.Count - 2).ToArray();
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, $"Publish Exception Buffer Length:{buffer.Length}, PacketId:{PacketId}, Topic:{Topic}, Buffer:{Encoding.UTF8.GetString(buffer.ToArray())}");
            }
        }

        internal override byte[] GetBytes()
        {
            var topicBytes = Encoding.UTF8.GetBytes(Topic);
            var size = topicBytes.Length + (int)MsgSize.MessageId + 2;

            if (Message != null)
                size += Message.Length;

            if (size > 1e+7)
                throw new ArgumentOutOfRangeException("Invalid buffer length! Maximum is 1e+7. ");

            using (var stream = new MemoryStream())
            {
                using (var binaryWriter = new BinaryWriter(stream))
                {
                    stream.Capacity = size;

                    var dupRetainFlags = (byte)(((byte)MsgType.Publish << (byte)MsgOffset.Type) | ((byte)QosLevel << QosLevelOffset));
                    dupRetainFlags |= IsDuplicate ? (byte)(1 << DupFlagOffset) : (byte)0x00;
                    dupRetainFlags |= Retain ? (byte)(1 << RetainFlagOffset) : (byte)0x00;

                    binaryWriter.Write(dupRetainFlags);

                    do
                    {
                        var digit = size % 128;

                        size /= 128;

                        if (size > 0)
                            digit = digit | 0x80;

                        binaryWriter.Write((byte)digit);
                    }
                    while
                    (
                        size > 0
                    );

                    binaryWriter.Write(UshortToBytes(topicBytes.Length));
                    binaryWriter.Write(topicBytes);
                    binaryWriter.Write(UshortToBytes(PacketId));

                    if (Message != null)
                        binaryWriter.Write(Message);

                    binaryWriter.Flush();

                    var bytes = stream.ToArray();

                    return bytes;
                }
            }
        }
    }
}
