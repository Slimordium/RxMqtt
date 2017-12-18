using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("RxMqtt.Broker")]
[assembly: InternalsVisibleTo("RxMqtt.Client")]

namespace RxMqtt.Shared
{
    public enum Status
    {
        Initialized,
        Error,
        SocketError,
        SslError,
        Initializing
    }
}