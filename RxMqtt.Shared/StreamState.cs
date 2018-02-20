using System;
using System.Net.Sockets;

namespace RxMqtt.Shared
{
    internal class StreamState : IDisposable
    {
        public byte[] Buffer { get; set; } = new byte[300000];

        public int BytesIn { get; set; }

        public Action<byte[]> CallBack { get; set; }

        public NetworkStream NetworkStream { get; set; }

        private void Dispose(bool disposing)
        {
            if (!disposing) return;

            Buffer = null;
            CallBack = null;
            NetworkStream = null;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}