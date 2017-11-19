using System;

namespace RxMqtt.Shared
{
    [Flags]
    internal enum MsgOffset : byte
    {
        Type = 0x04,
        MsgFlagBits = 0x00,
        DupFlag = 0x03,
        QosLevel = 0x01,
        RetainFlag = 0x00,
    }
}