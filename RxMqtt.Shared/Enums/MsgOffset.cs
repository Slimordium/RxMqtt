﻿using System;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RxMqtt.Broker")]
[assembly: InternalsVisibleTo("RxMqtt.Client")]

namespace RxMqtt.Shared.Enums
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