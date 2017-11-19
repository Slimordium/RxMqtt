using System;
using System.Threading.Tasks;
using RxMqtt.Shared;

namespace RxMqtt.Client
{
    //TODO: Support "Websocket" conenction...
    internal class WebsocketConnection : BaseConnection, IConnection
    {
        protected override void DisposeStreams()
        {
            
        }

        protected override void ReEstablishConnection()
        {
        }

        public void Dispose()
        {
        }

        public Task<Status> Initialize()
        {
            throw new NotImplementedException();
        }
    }
}