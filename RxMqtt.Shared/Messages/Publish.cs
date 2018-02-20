
using System;
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

        private const int MessageStartOffset = 1;

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
                //First byte is the type of payload, so we can igrnore it
                //The next 1 - 4 bytes (variable) determine packet length
                //The 2 bytes after that are the length of the topic
                //The 2 bytes after the topic are the Packet ID
                //The remaining bytes are the payload

                var decodeValue = DecodeValue(buffer, MessageStartOffset);
                var lengthByteCount = decodeValue.Item2;
                var topicLength = BytesToUshort(new[] {buffer[1 + lengthByteCount], buffer[2 + lengthByteCount]});
                var topicStartIndex = MessageStartOffset + lengthByteCount + 2;
                var topicArray = new byte[topicLength];
                var messageStartIndex = MessageStartOffset + lengthByteCount + 2 + topicLength + 2;
                var messageLength = buffer.Length - messageStartIndex;

                Buffer.BlockCopy(buffer, topicStartIndex, topicArray, 0, topicLength);

                Topic = Encoding.UTF8.GetString(topicArray);

                PacketId = BytesToUshort(new[] {buffer[topicStartIndex + topicLength], buffer[topicStartIndex + topicLength + 1]});

                Message = new byte[messageLength]; //topicLength - (2 for packet ID) - (2 for topic length) - (1 for payload type)

                if (messageLength < buffer.Length - messageStartIndex)
                {
                    Buffer.BlockCopy(buffer, messageStartIndex, Message, 0, buffer.Length - messageStartIndex);
                }
                else
                {
                    _logger.Log(LogLevel.Warn, $"Invalid buffer! => Buffer Length:{buffer.Length}, PacketId:{PacketId}, Topic:{Topic}, Minimum packet length: {decodeValue.Item1}");
                }
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, $"Publish Exception Buffer Length:{buffer.Length}, PacketId:{PacketId}, Topic:{Topic}, Buffer:{Encoding.UTF8.GetString(buffer.ToArray())} => {e.Message}");
            }
        }

        internal override byte[] GetBytes()
        {
            byte[] packet = null;

            try
            {
                
                var topicBytes = Encoding.UTF8.GetBytes(Topic);
                var messageLength = topicBytes.Length + (int)MsgSize.MessageId + 2;

                if (Message != null)
                    messageLength += Message.Length;

                var encodedPacketSize = EncodeValue(messageLength);
                var topicLength = UshortToBytes(topicBytes.Length);
                var packetId = UshortToBytes(PacketId);

                packet = new byte[messageLength + encodedPacketSize.Length + 1];

                if (messageLength > 1e+7)
                    throw new ArgumentOutOfRangeException("Invalid buffer length! Maximum is 1e+7. ");
                
                var messageTypeDupQosRetain = (byte)(((byte)MsgType.Publish << (byte)MsgOffset.Type) | ((byte)QosLevel << QosLevelOffset));

                messageTypeDupQosRetain |= IsDuplicate ? (byte)(1 << DupFlagOffset) : (byte)0x00;
                messageTypeDupQosRetain |= Retain ? (byte)(1 << RetainFlagOffset) : (byte)0x00;

                packet[0] = messageTypeDupQosRetain;

                Buffer.BlockCopy(encodedPacketSize, 0, packet, MessageStartOffset, encodedPacketSize.Length); //1 = the first byte in the array

                Buffer.BlockCopy(topicLength, 0, packet, MessageStartOffset + encodedPacketSize.Length, topicLength.Length);

                Buffer.BlockCopy(topicBytes, 0, packet, MessageStartOffset + encodedPacketSize.Length + topicLength.Length, topicBytes.Length);
                
                Buffer.BlockCopy(packetId, 0, packet, MessageStartOffset + encodedPacketSize.Length + topicLength.Length + topicBytes.Length, packetId.Length);

                Buffer.BlockCopy(Message, 0, packet, MessageStartOffset + encodedPacketSize.Length + topicLength.Length + topicBytes.Length + packetId.Length, Message.Length);
            }
            catch (Exception e)
            {
                Logger.Log(LogLevel.Error, $"Publish.GetBytes => {e.Message}");
            }

            return packet;
        }
    }
}
