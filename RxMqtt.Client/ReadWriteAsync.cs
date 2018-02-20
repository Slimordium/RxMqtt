using System;
using System.IO;
using System.Threading;
using NLog;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Client{
    internal class ReadWriteAsync{

        private static readonly ManualResetEventSlim _readEvent = new ManualResetEventSlim(true); //Not sure if this will be needed
        private static readonly ManualResetEventSlim _writeEvent = new ManualResetEventSlim(true);//Not sure if this will be needed
        private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();
        private static Stream _stream;

        internal ReadWriteAsync(ref Stream stream)
        {
            _stream = stream;
        }

        /// <summary>
        /// Will most likely read several packets when incoming traffic is high
        /// </summary>
        /// <param name="callback"></param>
        internal void Read(Action<byte[]> callback)
        {
            _readEvent.Wait();
            _readEvent.Reset();

            try
            {
                var socketState = new StreamState { CallBack = callback };

                var asyncResult = _stream.BeginRead(socketState.Buffer, 0, socketState.Buffer.Length, EndRead, socketState);

                asyncResult.AsyncWaitHandle.WaitOne();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);
            }
        }

        private static void EndRead(IAsyncResult asyncResult)
        {
            try
            {
                byte[] newBuffer = null;

                var asyncState = (StreamState)asyncResult.AsyncState;
                var bytesIn = _stream.EndRead(asyncResult);

                asyncResult.AsyncWaitHandle.WaitOne();

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

            _readEvent.Set();
        }

        internal void Write(MqttMessage message)
        {
            if (message == null)
                return;

            _writeEvent.Wait();
            _writeEvent.Reset();

            _logger.Log(LogLevel.Trace, $"Out => {message.MsgType}");

            try
            {
                var buffer = message.GetBytes();

                if (buffer.Length > 1e+7)
                {
                    _logger.Log(LogLevel.Error, "Message size greater than maximum of 1e+7 or 10mb. Not publishing");
                    return;
                }

                var socketState = new StreamState();
                var asyncResult = _stream.BeginWrite(buffer, 0, buffer.Length, EndWrite, socketState);

                asyncResult.AsyncWaitHandle.WaitOne();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e.Message);
            }
        }

        private static void EndWrite(IAsyncResult asyncResult)
        {
            try
            {
                var ar = (StreamState)asyncResult.AsyncState;
                _stream.EndWrite(asyncResult);

                asyncResult.AsyncWaitHandle.WaitOne();

                ar.Dispose();
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);
            }

            _writeEvent.Set();
        }
    }
}