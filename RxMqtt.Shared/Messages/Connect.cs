
using System;
using System.IO;
using System.Text;

namespace RxMqtt.Shared.Messages
{
    internal class Connect : MqttMessage
    {
        #region Constants

        private const byte WillRetainFlagOffset = 0x05;
        private const byte WillQosFlagOffset = 0x03;
        private const byte WillFlagOffset = 0x02;
        private const byte CleanSessionFlagOffset = 0x01;

        #endregion

        #region Properties

        internal string ClientId { get; set; }

        internal bool WillRetain { get; set; }

        internal bool WillFlag { get; set; }

        internal string WillTopic { get; set; }

        internal string WillMessage { get; set; }

        internal static bool CleanSession { get; set; } = true;

        internal ushort KeepAlivePeriod { get; set; }

        #endregion

        public Connect(string clientId, ushort keepAlivePeriod)
        {
            ClientId = clientId;
            KeepAlivePeriod = keepAlivePeriod;
            MsgType = MsgType.Connect;
        }

        public Connect(byte[] buffer)
        {
            var index = 11;
            
            KeepAlivePeriod = (ushort)((buffer[index] << 8) & 0xFF00);
            KeepAlivePeriod |= buffer[index++];

            var clientIdLength = ((buffer[index++] << 8) & 0xFF00);
            clientIdLength |= buffer[index++];

            var clientIdBuffer = new byte[clientIdLength];
            Array.Copy(buffer, index, clientIdBuffer, 0, clientIdLength);

            ClientId = Encoding.UTF8.GetString(clientIdBuffer);
        }

        internal override byte[] GetBytes()
        {
            var fixedHeaderSize = 1;
            var payloadSize = 0;
            var remainingLength = 0;
            var clientId = Encoding.UTF8.GetBytes(ClientId);
            var willTopic = WillFlag && WillTopic != null ? Encoding.UTF8.GetBytes(WillTopic) : null;
            var willMessage = WillFlag && WillMessage != null ? Encoding.UTF8.GetBytes(WillMessage) : null;
            var headerSize = 10;

            if (WillFlag && (willTopic == null || 
                             willMessage == null ||
                             willTopic.Length == 0 ||
                             willMessage.Length == 0))
            {
                throw new Exception("Will topic and will message are missing.");
            }

            if (!WillFlag && (WillRetain || willTopic != null || willMessage != null))
            {
                throw new Exception("Will flag not set, retain must be 0 and will topic and message must not be present");
            }

            //16 bit integer always splits into 2 bytes
            payloadSize += clientId.Length + 2;
            payloadSize += willTopic?.Length + 2 ?? 0;
            payloadSize += willMessage?.Length + 2 ?? 0;

            using (var stream = new MemoryStream())
            {
                using (var binaryWriter = new BinaryWriter(stream))
                {
                    remainingLength += headerSize + payloadSize;
                    fixedHeaderSize = AdjustHeaderSizeForRemainingLength(remainingLength, fixedHeaderSize);
                    stream.Capacity = fixedHeaderSize + headerSize + payloadSize;
                    
                    binaryWriter.Write(((byte)MsgType.Connect << (byte)MsgOffset.Type) | 0x00);
                    binaryWriter.Seek(1, 0);

                    var size = remainingLength;

                    //From RFC protocol
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

                    binaryWriter.Write((byte)0x00);
                    binaryWriter.Write((byte)0x04); //Length of bytes in the next line of code MQTT length = 4
                    binaryWriter.Write(Encoding.UTF8.GetBytes("MQTT"));
                    binaryWriter.Write((byte)0x04);

                    byte connectFlags = 0x00;
                    connectFlags |= WillRetain ? (byte)(1 << WillRetainFlagOffset) : (byte)0x00;

                    if (WillFlag)
                        connectFlags |= (byte)QosLevel.AtLeastOnce << WillQosFlagOffset;

                    connectFlags |= WillFlag ? (byte)(1 << WillFlagOffset) : (byte)0x00;
                    connectFlags |= CleanSession ? (byte)(1 << CleanSessionFlagOffset) : (byte)0x00;

                    binaryWriter.Write(connectFlags);
                    binaryWriter.Write(UshortToBytes(KeepAlivePeriod));
                    binaryWriter.Write(UshortToBytes(clientId.Length));
                    binaryWriter.Write(clientId);

                    if (WillFlag && willTopic != null)
                    {
                        binaryWriter.Write(UshortToBytes(willTopic.Length));
                    }

                    if (!WillFlag || willMessage == null)
                    {
                        binaryWriter.Flush();
                        return stream.ToArray();
                    }

                    binaryWriter.Write(UshortToBytes(willMessage.Length));
                    binaryWriter.Flush();

                    return stream.ToArray();
                }
            }
        }
    }
}
