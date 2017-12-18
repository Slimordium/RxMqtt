using System;
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("RxMqtt.Broker")]
[assembly: InternalsVisibleTo("RxMqtt.Client")]

namespace RxMqtt.Shared
{
    [Flags]
    internal enum MsgSize : byte
    {
        MsgType = 0x04,
        MsgFlagBits = 0x04,  
        DupFlag = 0x01,
        QosLevel = 0x02,
        RetainFlag = 0x01,
        MessageId = 0x02
    }
}