
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using RxMqtt.Shared.Enums;

[assembly: InternalsVisibleTo("RxMqtt.Broker")]
[assembly: InternalsVisibleTo("RxMqtt.Client")]

namespace RxMqtt.Shared.Messages
{
    internal class Unsubscribe : MqttMessage
    {
        private const byte MsgUnsubscribeFlagBits = 0x02;

        internal string[] Topics { get; set; }

        internal Unsubscribe()
        {
            MsgType = MsgType.Unsubscribe;
            PacketId = GetNextPacketId();
        }

        internal Unsubscribe(string[] topics)
        {
            MsgType = MsgType.Unsubscribe;
            Topics = topics;
            PacketId = GetNextPacketId();
        }

        internal Unsubscribe(IReadOnlyList<byte> buffer)
        {
            MsgType = MsgType.Unsubscribe;

            var packetLength = DecodeValue(buffer, 1);

            PacketId = BytesToUshort(new[] { buffer[packetLength.Item2 + 1], buffer[packetLength.Item2 + 2] });

            var topics = new List<string>();

            var topicLengthIndex = 5;//First topic length is at this index
            var combinedTopicLengths = 0;

            while (true)
            {
                try
                {
                    var topicLength = DecodeValue(buffer, topicLengthIndex).Item1;

                    combinedTopicLengths += topicLength;

                    var topicBuffer = new byte[topicLength];

                    Buffer.BlockCopy(buffer.ToArray(), topicLengthIndex + 1, topicBuffer, 0, topicLength);

                    topics.Add(Encoding.UTF8.GetString(topicBuffer));

                    if (packetLength.Item1 - combinedTopicLengths <= topics.Count * 3 + 5)
                        break;

                    topicLengthIndex += topicLength + 3;
                }
                catch (Exception)
                {
                    //Logger.Log(LogLevel.Error, e);
                    //Logger.Log(LogLevel.Error, $"{Encoding.UTF8.GetString(buffer)}");
                    break;
                }
            }

            Topics = topics.ToArray();
        }
        
        internal override byte[] GetBytes()
        {
            var payloadSize = 2;
            var remainingLength = 0;

            if (Topics == null || Topics.Length == 0)
                throw new Exception("Topic list is empty!");

            using (var stream = new MemoryStream())
            {
                using (var binaryWriter = new BinaryWriter(stream))
                {

                    var topics = new List<Tuple<ushort, byte[]>>();

                    foreach (var topic in Topics)
                    {
                        if (topic.Length < 1 || topic.Length > ushort.MaxValue)
                            throw new Exception("Invalid topic length");

                        var topicBytes = Encoding.UTF8.GetBytes(topic);

                        topics.Add(new Tuple<ushort, byte[]>((ushort)topicBytes.Length, topicBytes));

                        payloadSize += topicBytes.Length;
                    }

                    remainingLength += 2 + payloadSize;

                    binaryWriter.Write(((byte)MsgType.Unsubscribe << (byte)MsgOffset.Type) | MsgUnsubscribeFlagBits);

                    binaryWriter.Seek(1, 0);

                    var size = remainingLength;

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

                    binaryWriter.Write(UshortToBytes(PacketId));

                    foreach (var topic in topics)
                    {
                        binaryWriter.Write(UshortToBytes(topic.Item1));
                        binaryWriter.Write(topic.Item2);
                    }

                    binaryWriter.Flush();

                    return stream.ToArray();
                }
            }
        }
    }
}
