
using System;
using System.Collections.Generic;
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
            try
            {
                var newBuffer = new List<byte>(buffer);

                newBuffer = newBuffer.GetRange(1, newBuffer.Count - 1);

                var len = DecodeValue(newBuffer);

                var index = len.Item2;

                var bufferAfterGetRemaining = newBuffer.GetRange(index, newBuffer.Count - index);

                if (bufferAfterGetRemaining[0] == 0x00)
                    bufferAfterGetRemaining = bufferAfterGetRemaining.GetRange(1, bufferAfterGetRemaining.Count - 1);

                var topicLength = 0;

                len = DecodeValue(bufferAfterGetRemaining);

                index = len.Item2;

                topicLength = len.Item1;

                Topic = Encoding.UTF8.GetString(bufferAfterGetRemaining.GetRange(index, topicLength).ToArray());

                var bufferAfterGetTopic = bufferAfterGetRemaining.GetRange(index + topicLength, bufferAfterGetRemaining.Count - (index + topicLength));

                PacketId = BytesToUshort(new[] {bufferAfterGetTopic[0], bufferAfterGetTopic[1]});

                Message = bufferAfterGetTopic.GetRange(2, bufferAfterGetTopic.Count - 2).ToArray();
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, $"Publish Exception Buffer Length:{buffer.Length}, PacketId:{PacketId}, Topic:{Topic}, Buffer:{Encoding.UTF8.GetString(buffer.ToArray())} => {e.Message}");
            }
        }

        internal override byte[] GetBytes()
        {
            var packet = new List<byte>();

            try
            {
                var topicBytes = Encoding.UTF8.GetBytes(Topic);
                var size = topicBytes.Length + (int)MsgSize.MessageId + 2;

                if (Message != null)
                    size += Message.Length;

                if (size > 1e+7)
                    throw new ArgumentOutOfRangeException("Invalid buffer length! Maximum is 1e+7. ");
                
                var dupRetainFlags = (byte)(((byte)MsgType.Publish << (byte)MsgOffset.Type) | ((byte)QosLevel << QosLevelOffset));

                dupRetainFlags |= IsDuplicate ? (byte)(1 << DupFlagOffset) : (byte)0x00;
                dupRetainFlags |= Retain ? (byte)(1 << RetainFlagOffset) : (byte)0x00;

                packet.Add(dupRetainFlags);

                var sizeEnc = EncodeValue(size);

                packet.AddRange(sizeEnc);

                if (sizeEnc.Length == 1)
                    packet.Add(0x00);

                packet.AddRange(EncodeValue(topicBytes.Length));
                packet.AddRange(topicBytes);
                packet.AddRange(UshortToBytes(PacketId));

                if (Message != null)
                    packet.AddRange(Message);
            }
            catch (Exception e)
            {
                Logger.Log(LogLevel.Error, $"Publish.GetBytes => {e.Message}");
            }

            return packet.ToArray();
        }
    }
}
