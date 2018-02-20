using System;
using System.Collections.Generic;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Shared{
    internal static class Utilities{
        internal static IEnumerable<byte[]> ParseReadBuffer(byte[] buffer)
        {
            var startIndex = 0;
            var decodeValue = MqttMessage.DecodeValue(buffer, startIndex + 1);
            var packetLength = decodeValue.Item1 + decodeValue.Item2 + 1;

            if (buffer != null &&  buffer.Length > packetLength)
            {
                while (startIndex < buffer.Length)
                {
                    if (startIndex + 1 >= buffer.Length)
                    {
                        break;
                    }

                    decodeValue = MqttMessage.DecodeValue(buffer, startIndex + 1);

                    packetLength = decodeValue.Item1 + decodeValue.Item2 + 1;

                    if (startIndex + packetLength > buffer.Length)
                        break;

                    var packetBuffer = new byte[packetLength];

                    Buffer.BlockCopy(buffer, startIndex, packetBuffer, 0, packetLength);

                    yield return packetBuffer;

                    startIndex += packetLength;
                }
            }
            else
            {
                yield return buffer;
            }
        }
    }
}