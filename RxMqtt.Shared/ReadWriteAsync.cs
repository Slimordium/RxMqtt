using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using NLog;
using RxMqtt.Client;
using RxMqtt.Shared;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Shared
{
    internal class ReadWriteAsync : IReadWriteStream
    {

        private readonly AutoResetEvent _readAutoResetEvent = new AutoResetEvent(false);
        private readonly AutoResetEvent _writeAutoResetEvent = new AutoResetEvent(false);

        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();
        private readonly NetworkStream _networkStream;

        internal ReadWriteAsync(ref NetworkStream networkStream)
        {
            _networkStream = networkStream;
        }

        /// <summary>
        /// Will most likely read several packets when incoming traffic is high
        /// </summary>
        /// <param name="callback"></param>
        public void Read(Action<byte[]> callback)
        {
            try
            {
                var state = new StreamState { CallBack = callback, NetworkStream = _networkStream};

                _networkStream.BeginRead(state.Buffer, 0, state.Buffer.Length, EndRead, state);

                _readAutoResetEvent.WaitOne();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);
            }
        }

        private void EndRead(IAsyncResult asyncResult)
        {
            try
            {
                byte[] newBuffer = null;

                var asyncState = (StreamState)asyncResult.AsyncState;
                var bytesIn = asyncState.NetworkStream.EndRead(asyncResult);

                if (bytesIn > 0)
                {
                    newBuffer = new byte[bytesIn];

                    Buffer.BlockCopy(asyncState.Buffer, 0, newBuffer, 0, bytesIn);
                }

                asyncState.CallBack.Invoke(newBuffer);

                asyncState.Dispose();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e.Message);
            }

            _readAutoResetEvent.Set();
        }

        public void Write(MqttMessage message)
        {
            if (message == null)
                return;

            _logger.Log(LogLevel.Trace, $"Out => {message.MsgType}");

            try
            {
                var buffer = message.GetBytes();

                if (buffer.Length > 1e+7)
                {
                    _logger.Log(LogLevel.Error, "Message size greater than maximum of 1e+7 or 10mb. Not publishing");
                    return;
                }

                var socketState = new StreamState {NetworkStream = _networkStream};

                _networkStream.BeginWrite(buffer, 0, buffer.Length, EndWrite, socketState);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e.Message);
            }

            _writeAutoResetEvent.WaitOne();
        }

        private void EndWrite(IAsyncResult asyncResult)
        {
            try
            {
                var ar = (StreamState)asyncResult.AsyncState;
                ar.NetworkStream.EndWrite(asyncResult);

                ar.Dispose();
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);
            }

            _writeAutoResetEvent.Set();
        }
    }
}