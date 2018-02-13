
using NLog;
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("RxMqtt.Broker")]
[assembly: InternalsVisibleTo("RxMqtt.Client")]

namespace RxMqtt.Shared.Messages
{
    /// <summary>
    /// Packet processing code examples can be found here, along with protocol specifications
    /// https://docs.oasis-open.org/mqtt/mqtt/v3.1.1/os/mqtt-v3.1.1-os.pdf
    /// </summary>
    public abstract class MqttMessage{
        internal static ILogger Logger { get; } = LogManager.GetCurrentClassLogger();

        protected QosLevel QosLevel { get; set;  } = QosLevel.AtLeastOnce;

        internal bool IsDuplicate { get; set; } = false;

        internal ushort PacketId { get; set; } = GetNextPacketId();

        internal MsgType MsgType { get; set; } = MsgType.Publish;

        private static ushort _messageIdCounter = 1;

        private static readonly object _idLock = new object();

        internal static ushort GetNextPacketId()
        {
            lock (_idLock)
            {
                if (_messageIdCounter >= ushort.MaxValue - 1)
                    _messageIdCounter = 1;
                else
                    _messageIdCounter++;
                return _messageIdCounter;
            }
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
            }
            while 
            (
                temp > 0
            );

            return fixedHeaderSize;
        }

        internal static byte[] UshortToBytes(ushort value)
        {
            var bytes = new byte[2];

            bytes[0] = (byte)((value >> 8) & 0x00FF);
            bytes[1] = (byte)(value & 0x00FF);

            return bytes;
        }

        internal static byte[] UshortToBytes(int value)
        {
            return UshortToBytes((ushort)value);
        }

        internal static ushort BytesToUshort(byte[] bytes)
        {
            var returnValue = (ushort)((bytes[0] << 8) & 0xFF00);
            returnValue |= bytes[1];

            return returnValue;
        }

        //Annoying...
        internal static ushort ToUshort(byte[] buffer)
        {
            var multiplier = 1;
            var value = 0;

            foreach (var b in buffer)
            {
                var digit = 0;
                do
                {
                    digit = b;
                    value += ((digit & 127) * multiplier);
                    multiplier *= 128;
                }
                while
                ((digit & 128) != 0);
            }

            return (ushort)value;
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

                buffer[index++] = (byte)digit;
            }
            while 
            (
                remainingLength > 0
            );

            return index;
        }
    }
}


