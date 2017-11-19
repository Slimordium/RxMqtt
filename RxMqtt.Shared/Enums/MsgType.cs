using System;

namespace RxMqtt.Shared
{
    [Flags]
    internal enum MsgType : byte
    {
        Connect = 0x01,
        ConnectAck = 0x02,
        Publish = 0x03,
        PublishAck = 0x04,
        PubRec = 0x05,
        PubRel = 0x06,
        PubComp = 0x07,
        Subscribe = 0x08,
        SubscribeAck = 0x09,
        Unsubscribe = 0x0A,
        UnsubscribeAck = 0x0B,
        PingRequest = 0x0C,
        PingResponse = 0x0D,
        Disconnect = 0x0E,
        Retry = 0xFF
    }
}