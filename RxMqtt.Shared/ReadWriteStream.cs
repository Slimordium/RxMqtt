using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using NLog;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Shared
{
    internal class ReadWriteStream : IReadWriteStream
    {
        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();
        private readonly int _bufferLength;
        private readonly NetworkStream _networkStream;
        private readonly BlockingCollection<byte[]> _blockingCollection = new BlockingCollection<byte[]>();
        private readonly Action<byte[]> _callback;
        private readonly Thread _queueProcessorThread;
        private readonly CancellationTokenSource _cancellationTokenSourceSource;
        private readonly AutoResetEvent _writeAutoResetEvent = new AutoResetEvent(true);

        /// <summary>
        /// Complete messages will be passed to callback. If a read contains multiple messages, or partial messages, they will be assembled before the callback is invoked
        /// </summary>
        /// <param name="networkStream"></param>
        /// <param name="callback"></param>
        /// <param name="bufferLength"></param>
        /// <param name="cancellationTokenSource"></param>
        internal ReadWriteStream(ref NetworkStream networkStream, Action<byte[]> callback, int bufferLength, ref CancellationTokenSource cancellationTokenSource)
        {
            _cancellationTokenSourceSource = cancellationTokenSource;
            _networkStream = networkStream;
            _callback = callback;
            _bufferLength = bufferLength;

            _queueProcessorThread = new Thread(ParseMessagesFromQueue) {IsBackground = true};
            _queueProcessorThread.Start();

            _cancellationTokenSourceSource.Token.Register(() =>
            {
                _blockingCollection.CompleteAdding();
            });

            Read();
        }

        private void Read()
        {
            try
            {
                var state = new StreamState() { NetworkStream = _networkStream}; //Important to pass the stream in this manner

                _networkStream.BeginRead(state.Buffer, 0, state.Buffer.Length, EndRead, state);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);

                _cancellationTokenSourceSource.Cancel();
            }
        }

        private void EndRead(IAsyncResult asyncResult)
        {
            try
            {
                var asyncState = (StreamState)asyncResult.AsyncState; //Important to pass the stream in this manner
                var bytesIn = asyncState.NetworkStream.EndRead(asyncResult);

                if (bytesIn > 0)
                {
                    var newBuffer = new byte[bytesIn];

                    Buffer.BlockCopy(asyncState.Buffer, 0, newBuffer, 0, bytesIn);

                    _blockingCollection.Add(newBuffer);
                }

                asyncState.Dispose();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e.Message);

                _cancellationTokenSourceSource.Cancel();
                return;
            }

            if (!_cancellationTokenSourceSource.IsCancellationRequested)
                Read();
        }

        public void Write(MqttMessage message)
        {
            if (message == null)
                return;

            _logger.Log(LogLevel.Trace, $"Out => {message.MsgType}, PacketId: {message.PacketId}");

            try
            {
                var buffer = message.GetBytes();

                if (buffer.Length > 1e+7)
                {
                    _logger.Log(LogLevel.Error, "Message size greater than maximum of 1e+7 or 10mb. Not publishing");
                    return;
                }

                foreach (var segment in ArraySplit(buffer, _bufferLength))
                {
                    _writeAutoResetEvent.WaitOne();

                    var socketState = new StreamState(_bufferLength) { NetworkStream = _networkStream };

                    _networkStream.BeginWrite(segment, 0, segment.Length, EndWrite, socketState);
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e.Message);

                if (!_cancellationTokenSourceSource.IsCancellationRequested)
                    _cancellationTokenSourceSource.Cancel();
            }
        }

        public void Write(byte[] buffer)
        {
            if (buffer == null)
                return;

            _logger.Log(LogLevel.Trace, $"Out bytes => {buffer.Length}");

            try
            {
                if (buffer.Length > 1e+7)
                {
                    _logger.Log(LogLevel.Error, "Message size greater than maximum of 1e+7 or 10mb. Not publishing");
                    return;
                }

                foreach (var segment in ArraySplit(buffer, _bufferLength))
                {
                    _writeAutoResetEvent.WaitOne();

                    var socketState = new StreamState(_bufferLength) { NetworkStream = _networkStream };

                    _networkStream.BeginWrite(segment, 0, segment.Length, EndWrite, socketState);
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e.Message);

                if (!_cancellationTokenSourceSource.IsCancellationRequested)
                    _cancellationTokenSourceSource.Cancel();
            }
        }

        private static IEnumerable<byte[]> ArraySplit(byte[] buffer, int segmentLength)
        {
            var arrayLength = buffer.Length;
            byte[] newBuffer = null;

            var i = 0;
            for (; arrayLength > (i + 1) * segmentLength; i++)
            {
                newBuffer = new byte[segmentLength];
                Buffer.BlockCopy(buffer, i * segmentLength, newBuffer, 0, segmentLength);
                yield return newBuffer;
            }

            var intBufforLeft = arrayLength - i * segmentLength;

            if (intBufforLeft <= 0)
                yield break;

            newBuffer = new byte[intBufforLeft];
            Buffer.BlockCopy(buffer, i * segmentLength, newBuffer, 0, intBufforLeft);
            yield return newBuffer;
        }

        private void EndWrite(IAsyncResult asyncResult)
        {
            try
            {
                var ar = (StreamState) asyncResult.AsyncState;
                ar.NetworkStream.EndWrite(asyncResult);

                ar.Dispose();
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e.Message);

                if (!_cancellationTokenSourceSource.IsCancellationRequested)
                    _cancellationTokenSourceSource.Cancel();
            }
            finally
            {
                _writeAutoResetEvent.Set();
            }
        }

        private long _queueLock;

        private void ParseMessagesFromQueue()
        {
            if (Interlocked.Exchange(ref _queueLock, 1) == 1)
                return;

            byte[] packetBytes = null;
            var remaining = 0;
            var offset = 0;

            foreach (var buffer in _blockingCollection.GetConsumingEnumerable())
            {
                while (!_cancellationTokenSourceSource.IsCancellationRequested)
                {
                    var skipCount = 0;

                    if (remaining > 0)
                    {
                        if (buffer.Length >= remaining)
                        {
                            Buffer.BlockCopy(buffer, 0, packetBytes, offset, remaining);

                            skipCount = buffer.Length - remaining;

                            remaining = 0;
                            offset = 0;
                        }
                        else
                        {
                            Buffer.BlockCopy(buffer, 0, packetBytes, offset, buffer.Length);

                            remaining = remaining - buffer.Length;

                            offset += buffer.Length;
                        }

                        if (remaining == 0)
                        {
                            _callback.Invoke(packetBytes);
                            packetBytes = null;
                        }

                        if (skipCount == 0)
                        {
                            break;
                        }
                    }

                    var newBuffer = new byte[buffer.Length - skipCount];

                    Buffer.BlockCopy(buffer, skipCount, newBuffer, 0, buffer.Length - skipCount);

                    remaining = GetMessageLength(ref newBuffer);

                    if (remaining - buffer.Length > 0)
                    {
                        try
                        {
                            packetBytes = new byte[remaining];

                            offset = buffer.Length;

                            remaining -= buffer.Length;

                            Buffer.BlockCopy(newBuffer, 0, packetBytes, 0, buffer.Length - skipCount);
                        }
                        catch (Exception e)
                        {
                            _logger.Log(LogLevel.Error, $"ParseMessagesFromQueue => '{e.Message}' buffer.length:{buffer.Length}, skipCount:{skipCount}, packetBytes.length:{packetBytes?.Length}, remaining:{remaining}");

                            offset = 0;
                            skipCount = 0;
                            remaining = 0;
                            packetBytes = null;

                            _callback.Invoke(newBuffer);
                        }

                        break;
                    }

                    offset = 0;
                    skipCount = 0;
                    remaining = 0;
                    packetBytes = null;

                    _callback.Invoke(newBuffer);

                    break;
                }
            }
        }

        /// <summary>
        /// Either returns a complete packet with the bool set to true, or a partial packet with the bool set to false
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        private static int GetMessageLength(ref byte[] buffer)
        {
            var decodeValue = MqttMessage.DecodeValue(buffer, 1);
            return decodeValue.Item1 + decodeValue.Item2 + 1;
        }
    }
}