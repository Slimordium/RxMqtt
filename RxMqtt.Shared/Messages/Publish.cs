
using System;
using System.IO;
using System.Text;
using NLog;
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("RxMqtt.Broker")]
[assembly: InternalsVisibleTo("RxMqtt.Client")]

namespace RxMqtt.Shared.Messages
{
    internal class Publish : MqttMessage
    {
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
        }

        internal Publish(byte[] buffer)
        {
            var index = 5;

            int topicLength = 0;

            if (buffer.Length > 5)
                topicLength = BytesToUshort(new[] {buffer[3], buffer[4]});
            else
            {
                Logger.Log(LogLevel.Warn, $"PublishMsg was not valid => {BitConverter.ToString(buffer)}");
                return;
            }

            if (topicLength > 128)
            {
                index = 4;
                topicLength = BytesToUshort(new[] { buffer[2], buffer[3] });
            }

            var topicBytes = new byte[topicLength];

            Array.Copy(buffer, index, topicBytes, 0, topicBytes.Length);

            index += topicLength;
            Topic = Encoding.UTF8.GetString(topicBytes);

            if (index >= buffer.Length + 2)
            {
                PacketId = 0;
                Message = new byte[] { 0x00 };

                Logger.Log(LogLevel.Debug, "Buffer does not contain PacketId or Message");
                return;
            }

            PacketId = BytesToUshort(new[] {buffer[index++], buffer[index++]});

            var messageSize = buffer.Length - index;
            Message = new byte[messageSize];

            Array.Copy(buffer, index, Message, 0, buffer.Length - index);
        }

        internal override byte[] GetBytes()
        {
            var topicBytes = Encoding.UTF8.GetBytes(Topic);
            var size = topicBytes.Length + (int)MsgSize.MessageId + 2;

            if (Message != null)
                size += Message.Length;

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
