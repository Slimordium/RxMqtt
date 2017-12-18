
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LogLevel = NLog.LogLevel;
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("RxMqtt.Broker")]
[assembly: InternalsVisibleTo("RxMqtt.Client")]

namespace RxMqtt.Shared.Messages
{
    internal class Subscribe : MqttMessage
    {
        private const byte MsgSubscribeFlagBits = 0x02;

        internal string[] Topics { get; set; }

        private byte[] QoSLevels
        {
            get { return Topics.Select(t => (byte) 0x01).ToArray(); }
        }

        internal Subscribe()
        {
            MsgType = MsgType.Subscribe;
        }

        internal Subscribe(string[] topics)
        {
            MsgType = MsgType.Subscribe;
            Topics = topics;
        }

        internal Subscribe(ushort packetId)
        {
            MsgType = MsgType.Subscribe;
            PacketId = packetId;
        }

        internal Subscribe(byte[] buffer)
        {
            MsgType = MsgType.Subscribe;

            var packetLength = ToUshort(new [] { buffer[1], buffer[2] });

            PacketId = ToUshort(new[] {buffer[3], buffer[4]});

            var topics = new List<string>();

            var topicLengthIndex = 5;//First topic length is at this index
            var combinedTopicLengths = 0;

            while(true)
            {
                try
                {
                    var topicLength = ToUshort(new[] { buffer[topicLengthIndex] });

                    combinedTopicLengths += topicLength;

                    var topicBuffer = new byte[topicLength];

                    Array.Copy(buffer, topicLengthIndex + 1, topicBuffer, 0, topicLength);

                    topics.Add(Encoding.UTF8.GetString(topicBuffer));

                    if (packetLength - combinedTopicLengths <= topics.Count * 3 + 5)
                        break;

                    topicLengthIndex += topicLength + 3;
                }
                catch (Exception e)
                {
                    Logger.Log(LogLevel.Error, e);
                    Logger.Log(LogLevel.Error, $"{Encoding.UTF8.GetString(buffer)}");
                    break;
                }
            }

            Topics = topics.ToArray();
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
                    throw new ArgumentOutOfRangeException(Topics[topicIdx]);

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
