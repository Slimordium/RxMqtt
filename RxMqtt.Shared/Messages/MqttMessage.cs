
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RxMqtt.Shared.Enums;

[assembly: InternalsVisibleTo("RxMqtt.Broker")]
[assembly: InternalsVisibleTo("RxMqtt.Client")]
[assembly: InternalsVisibleTo("RxMqtt.Tests")]
[assembly: InternalsVisibleTo("RsMqtt.Broker.Console")]

namespace RxMqtt.Shared.Messages
{
    /// <summary>
    /// Packet processing code examples can be found here, along with protocol specifications
    /// https://docs.oasis-open.org/mqtt/mqtt/v3.1.1/os/mqtt-v3.1.1-os.pdf
    /// </summary>
    public abstract class MqttMessage
    {
        public QosLevel QosLevel { get; set; } = QosLevel.AtLeastOnce;

        internal bool IsDuplicate { get; set; } = false;

        internal ushort PacketId { get; set; }

        internal MsgType MsgType { get; set; } = MsgType.Publish;

        private static volatile ushort _messageIdCounter = 1;

        internal static ushort GetNextPacketId()
        {
            if (_messageIdCounter >= ushort.MaxValue - 1)
                _messageIdCounter = 1;
            else
                _messageIdCounter++;
            return _messageIdCounter;
        }

        protected MqttMessage()
        {
        }

        internal abstract byte[] GetBytes();

        protected int AdjustHeaderSizeForRemainingLength(int remainingLength, int fixedHeaderSize)
        {
            var temp = remainingLength;
            // remaining length byte can encode until 128)
            do
            {
                fixedHeaderSize++;
                temp = temp / 128;
            } while
            (
                temp > 0
            );

            return fixedHeaderSize;
        }

        internal static byte[] UshortToBytes(ushort value)
        {
            var bytes = new byte[2];

            bytes[0] = (byte) ((value >> 8) & 0x00FF);
            bytes[1] = (byte) (value & 0x00FF);

            return bytes;
        }

        internal static byte[] UshortToBytes(int value)
        {
            return UshortToBytes((ushort) value);
        }

        internal static ushort BytesToUshort(byte[] bytes)
        {
            var returnValue = (ushort) ((bytes[0] << 8) & 0xFF00);
            returnValue |= bytes[1];

            return returnValue;
        }

        //Annoying...
        internal int GetRemainingLength(int remainingLength, byte[] buffer, int index)
        {
            do
            {
                var digit = remainingLength % 128;

                remainingLength /= 128;

                if (remainingLength > 0)
                    digit = digit | 0x80;

                buffer[index++] = (byte) digit;
            } while
            (
                remainingLength > 0
            );

            return index;
        }

        internal static Tuple<int, int> DecodeValue(IReadOnlyList<byte> buffer, int startIndex = 0)
        {
            var multiplier = 1;
            var decodedValue = 0;
            var bytesUsedToStoreValue = 0;
            
            while (true)
            {
                if (buffer == null || startIndex > buffer.Count)
                    break;

                var encodedByte = buffer[startIndex];
                startIndex++;

                decodedValue += (encodedByte & 127) * multiplier;

                multiplier *= 128;

                if (multiplier > 128 * 128 * 128)
                    break;

                bytesUsedToStoreValue++;

                if ((encodedByte & 128) == 0 || bytesUsedToStoreValue >= 4 || startIndex > startIndex + 3)
                {
                    break;  
                }
            }

            return new Tuple<int, int>(decodedValue, bytesUsedToStoreValue);
        }

        internal static byte[] EncodeValue(int value)
        {
            return EncodeValue(Convert.ToUInt32(value));
        }

        internal static byte[] EncodeValue(uint value)
        {
            var buffer = new byte[4];
            var index = 0;

            do
            {
                var encodedByte = value % 128;

                value /= 128;

                if (value > 0)
                    encodedByte = encodedByte | 128;

                buffer[index] = (byte)encodedByte;

                index++;
            } while
            (
                value > 0
            );

            var indexOf = Array.FindIndex(buffer, b => b == 0x00);

            var newBuffer = new byte[indexOf];

            Buffer.BlockCopy(buffer, 0, newBuffer, 0, indexOf);

            return newBuffer;
        }
    }
}


