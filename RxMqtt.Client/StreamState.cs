using System;

namespace RxMqtt.Client{
    internal class StreamState : IDisposable
    {
        public byte[] Buffer { get; set; } = new byte[300000];

        public int BytesIn { get; set; }

        public Action<byte[]> CallBack { get; set; }

        ~StreamState()
        {
            Dispose(false);
        }

        private void ReleaseUnmanagedResources()
        {
            Buffer = null;
            CallBack = null;
        }

        private void Dispose(bool disposing)
        {
            ReleaseUnmanagedResources();
            if (disposing)
            {
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}