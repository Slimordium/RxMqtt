
using System;
using System.Text;
using System.Runtime.CompilerServices;
using RxMqtt.Shared.Enums;

[assembly: InternalsVisibleTo("RxMqtt.Broker")]
[assembly: InternalsVisibleTo("RxMqtt.Client")]

namespace RxMqtt.Shared.Messages
{
    public class Publish : MqttMessage
    {
        private const int MessageStartOffset = 1;

        private const byte DupFlagMask = 0x08;
        private const byte DupFlagOffset = 0x03;

        private const byte QosLevelMask = 0x06;
        private const byte QosLevelOffset = 0x01;

        private const byte RetainFlagMask = 0x01;
        private const byte RetainFlagOffset = 0x00;

        public byte[] Message { get; set; } = new byte[1];

        public string Topic { get; set; } = string.Empty;

        internal bool Retain { get; set; }

        internal Publish()
        {
            MsgType = MsgType.Publish;

            PacketId = GetNextPacketId();
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
            //First byte is the type of payload, so we can ignore it
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

            if (messageLength < buffer.Length)
            {
                Buffer.BlockCopy(buffer, messageStartIndex, Message, 0, buffer.Length - messageStartIndex);
            }
        }

        internal override byte[] GetBytes()
        {
            byte[] packet = null;

            var topicBytes = Encoding.UTF8.GetBytes(Topic);
            var messageLength = topicBytes.Length + (int)MsgSize.MessageId + 2;

            if (Message != null)
                messageLength += Message.Length;

            var encodedPacketSize = EncodeValue(messageLength);
            var topicLength = UshortToBytes(topicBytes.Length);
            var packetId = UshortToBytes(PacketId);

            packet = new byte[messageLength + encodedPacketSize.Length + 1];

            if (messageLength > 2000000)
                throw new ArgumentOutOfRangeException("Invalid buffer length");
            
            var messageTypeDupQosRetain = (byte)(((byte)MsgType.Publish << (byte)MsgOffset.Type) | ((byte)QosLevel << QosLevelOffset));

            messageTypeDupQosRetain |= IsDuplicate ? (byte)(1 << DupFlagOffset) : (byte)0x00;
            messageTypeDupQosRetain |= Retain ? (byte)(1 << RetainFlagOffset) : (byte)0x00;

            packet[0] = messageTypeDupQosRetain;

            Buffer.BlockCopy(encodedPacketSize, 0, packet, MessageStartOffset, encodedPacketSize.Length); //1 = the first byte in the array

            Buffer.BlockCopy(topicLength, 0, packet, MessageStartOffset + encodedPacketSize.Length, topicLength.Length);

            Buffer.BlockCopy(topicBytes, 0, packet, MessageStartOffset + encodedPacketSize.Length + topicLength.Length, topicBytes.Length);
            
            Buffer.BlockCopy(packetId, 0, packet, MessageStartOffset + encodedPacketSize.Length + topicLength.Length + topicBytes.Length, packetId.Length);

            if (Message != null)
                Buffer.BlockCopy(Message, 0, packet, MessageStartOffset + encodedPacketSize.Length + topicLength.Length + topicBytes.Length + packetId.Length, Message.Length);
      
            return packet;
        }

        internal bool IsTopicMatch(string topicFilter)
        {
            if (string.IsNullOrEmpty(Topic))
                return false;

            var topicParts = Topic.Split('/');
            var topicFilterParts = topicFilter.Split('/');

            var loopCount = topicParts.Length;

            //if (topicFilterParts.Length != topicParts.Length)
            //{
            //    return false;
            //}

            for (var i = 0; i < loopCount; i++)
            {
                if (topicParts[i].Contains("#") && i <= topicFilterParts.Length)
                {
                    return true;
                }

                if (!topicParts[i].Equals(topicFilterParts[i]) &&
                    !topicFilterParts[i].Equals("#") &&
                    !topicFilterParts[i].Equals("+"))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
