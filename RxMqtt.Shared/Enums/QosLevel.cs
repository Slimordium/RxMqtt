﻿using System;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RxMqtt.Broker")]
[assembly: InternalsVisibleTo("RxMqtt.Client")]

namespace RxMqtt.Shared.Enums
{
    [Flags]
    public enum QosLevel : byte
    {
        AtMostOnce = 0x00,
        AtLeastOnce = 0x01,
        //ExactlyOnce = 0x02
    }
}