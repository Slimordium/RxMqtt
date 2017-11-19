
using System;
using System.Linq;
using System.Text;

namespace RxMqtt.Shared.Messages
{
    internal class SubscribeMsg : MqttMessage
    {
        private const byte MsgSubscribeFlagBits = 0x02;

        internal string[] Topics { get; set; }

        private byte[] QoSLevels
        {
            get { return Topics.Select(t => (byte) 0x01).ToArray(); }
        }

        internal SubscribeMsg()
        {
            MsgType = MsgType.Subscribe;
        }

        internal SubscribeMsg(string[] topics)
        {
            MsgType = MsgType.Subscribe;
            Topics = topics;
        }

        internal override byte[] GetBytes()
        {
            var fixedHeaderSize = 1;
            var headerSize = (int)MsgSize.MessageId;
            var payloadSize = 0;
            var remainingLength = 0;
            var index = 0;

            if (Topics == null || Topics.Length == 0)
                throw new Exception("Topics empty?");

            if (QoSLevels == null || QoSLevels.Length == 0)
                throw new Exception("Qos levels empty?");

            if (Topics.Length != QoSLevels.Length)
                throw new Exception("Qos levels do not match");

            int topicIdx;
            var topicsUtf8 = new byte[Topics.Length][];

            for (topicIdx = 0; topicIdx < Topics.Length; topicIdx++)
            {
                if (Topics[topicIdx] == null)
                    continue;

                if (Topics[topicIdx].Length < 1 || Topics[topicIdx].Length > ushort.MaxValue)
                    throw new Exception("Topic too long!");

                topicsUtf8[topicIdx] = Encoding.UTF8.GetBytes(Topics[topicIdx]);
                payloadSize += 2; // topic size (MSB, LSB)
                payloadSize += topicsUtf8[topicIdx].Length;
                payloadSize++; // byte for QoS
            }

            remainingLength += (headerSize + payloadSize);

            fixedHeaderSize = AdjustHeaderSizeForRemainingLength(remainingLength, fixedHeaderSize);

            var buffer = new byte[fixedHeaderSize + headerSize + payloadSize];

            buffer[index++] = ((byte)MsgType.Subscribe << (byte)MsgOffset.Type) | MsgSubscribeFlagBits;
            
            index = GetRemainingLength(remainingLength, buffer, index);

            if (PacketId == 0)
                throw new Exception("Subscribe PacketId cannot be 0");

            buffer[index++] = (byte)((PacketId >> 8) & 0x00FF); 
            buffer[index++] = (byte)(PacketId & 0x00FF);

            for (topicIdx = 0; topicIdx < Topics.Length; topicIdx++)
            {
                if (topicsUtf8[topicIdx] == null)
                    continue;

                buffer[index++] = (byte)((topicsUtf8[topicIdx].Length >> 8) & 0x00FF); 
                buffer[index++] = (byte)(topicsUtf8[topicIdx].Length & 0x00FF);

                Array.Copy(topicsUtf8[topicIdx], 0, buffer, index, topicsUtf8[topicIdx].Length);

                index += topicsUtf8[topicIdx].Length;

                buffer[index++] = QoSLevels[topicIdx];
            }
            
            return buffer;
        }

    }
}
