using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using NLog;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Shared
{
    internal class ReadWriteAsync : IReadWriteStream
    {
        private readonly ILogger _logger;
        private readonly NetworkStream _networkStream;
        private readonly BlockingCollection<byte[]> _blockingCollection = new BlockingCollection<byte[]>();
        private readonly Action<byte[]> _callback;
        private readonly Thread _queueProcessorThread;
        private readonly CancellationTokenSource _cancellationTokenSourceSource;

        /// <summary>
        /// Complete messages will be passed to callback. If a read contains multiple messages, or partial messages, they will be assembled before the callback is invoked
        /// </summary>
        /// <param name="networkStream"></param>
        /// <param name="callback"></param>
        /// <param name="cancellationTokenSource"></param>
        /// <param name="logger"></param>
        internal ReadWriteAsync(ref NetworkStream networkStream, Action<byte[]> callback, ref CancellationTokenSource cancellationTokenSource, ref ILogger logger)
        {
            _logger = logger;
            _cancellationTokenSourceSource = cancellationTokenSource;
            _networkStream = networkStream;
            _callback = callback;

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
                var state = new StreamState { NetworkStream = _networkStream}; //Important to pass the stream in this manner

                _networkStream.BeginRead(state.Buffer, 0, state.Buffer.Length, EndRead, state);
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

            _logger.Log(LogLevel.Trace, $"Out => {message.MsgType}");

            try
            {
                var buffer = message.GetBytes();

                if (buffer.Length > 1e+7)
                {
                    _logger.Log(LogLevel.Error, "Message size greater than maximum of 1e+7 or 10mb. Not publishing");
                    return;
                }

                var socketState = new StreamState { NetworkStream = _networkStream };

                _networkStream.BeginWrite(buffer, 0, buffer.Length, EndWrite, socketState);
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
                _logger.Log(LogLevel.Error, e.Message);

                if (!_cancellationTokenSourceSource.IsCancellationRequested)
                    _cancellationTokenSourceSource.Cancel();
            }
        }

        private void ParseMessagesFromQueue()
        {
            var packetBytes = new List<byte>();
            var remaining = 0;

            foreach (var buffer in _blockingCollection.GetConsumingEnumerable())
            {
                while (!_cancellationTokenSourceSource.IsCancellationRequested)
                {
                    if (remaining > 0 && buffer.Length >= remaining)
                    {
                        packetBytes.AddRange(buffer.Take(remaining));
                        _callback.Invoke(packetBytes.ToArray());
                    }

                    if (remaining > 0 && buffer.Length - remaining == 0)
                    {
                        remaining = 0;
                        packetBytes = new List<byte>();
                        break;
                    }

                    var msg = GetSingleMessage(buffer.Skip(remaining).Take(buffer.Length - remaining).ToArray());

                    if (msg.Item2 > 0)
                    {
                        packetBytes.AddRange(msg.Item1);
                        remaining = msg.Item2;
                        break;
                    }

                    if (packetBytes.Count > 0)
                    {
                        _callback.Invoke(packetBytes.ToArray());
                    }
                    else
                        _callback.Invoke(buffer);

                    break;
                }
            }
        }

        /// <summary>
        /// Either returns a complete packet with the bool set to true, or a partial packet with the bool set to false
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        private static Tuple<byte[], int> GetSingleMessage(byte[] buffer)
        {
            var decodeValue = MqttMessage.DecodeValue(buffer, 1);
            var packetLength = decodeValue.Item1 + decodeValue.Item2 + 1;

            if (packetLength > buffer.Length)
                return new Tuple<byte[], int>(buffer, packetLength - buffer.Length);

            return new Tuple<byte[], int>(buffer, 0); ;
        }

    }
}