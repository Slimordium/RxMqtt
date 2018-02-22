using System;
using System.Net.Sockets;

namespace RxMqtt.Shared
{
    internal class StreamState : IDisposable
    {
        internal StreamState(int bufferLength)
        {
            Buffer = new byte[bufferLength];
        }

        internal byte[] Buffer { get; private set; }

        internal NetworkStream NetworkStream { get; set; }

        private void Dispose(bool disposing)
        {
            if (!disposing) return;

            Buffer = null;
            NetworkStream = null;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}