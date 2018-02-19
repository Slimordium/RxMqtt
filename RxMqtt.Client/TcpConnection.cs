using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using RxMqtt.Shared;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Client{
    internal class TcpConnection{
        private Status _status = Status.Error;
        private readonly int _port;

        internal ISubject<PacketEnvelope> PacketSyncSubject { get; }

        protected ISubject<PacketEnvelope> _packetSyncSubject = new BehaviorSubject<PacketEnvelope>(null);

        protected static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        private static readonly ManualResetEventSlim _readEvent = new ManualResetEventSlim(true);
        private static readonly ManualResetEventSlim _writeEvent = new ManualResetEventSlim(true);

        private Task _readTask;

        protected string HostName;

        internal TcpConnection
        (
            string connectionId,
            string hostName,
            int keepAliveInSeconds,
            int port,
            string certFileName = "",
            string pfxPw = ""
        )
        {
            PacketSyncSubject = Subject.Synchronize(_packetSyncSubject);

            _keepAliveInSeconds = (ushort) keepAliveInSeconds;
            _connectionId = connectionId;
            _pfxFileName = certFileName;
            _certPassword = pfxPw;
            _port = port;
            HostName = hostName;
        }

        public async Task<Status> Initialize()
        {
            try
            {
                var localDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().CodeBase)?.Replace(@"file:\", "");

                if (!string.IsNullOrEmpty(_pfxFileName) && !string.IsNullOrEmpty(_certPassword))
                {
                    _logger.Log(LogLevel.Trace, $@"Certificate directory: {localDirectory}\{_pfxFileName}");

                    _certificate = new X509Certificate2($@"{localDirectory}\{_pfxFileName}", _certPassword);
                }

                // in this case the parameter remoteHostName isn't a valid IP address
                if (!IPAddress.TryParse(HostName, out _ipAddress))
                {
                    var entries = Dns.GetHostAddresses(HostName);

                    if (entries.Any())
                    {
                        _ipAddress = entries.First();
                    }
                    else
                    {
                        _logger.Log(LogLevel.Trace, $"IP Address not found for {HostName}");
                        return _status;
                    }
                }

                _status = await InitializeConnection(_port);

                if (_status == Status.Initialized)
                {
                    _readTask = Task.Factory.StartNew(BeginRead, TaskCreationOptions.LongRunning);
                }
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);

                _status = Status.Error;
            }

            return _status;
        }

        internal void BeginWrite(MqttMessage message)
        {
            if (message == null)
                return;

            if (_status != Status.Initialized)
            {
                return;
            }

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
            catch (Exception)
            {
            }
        }

        private static void EndWrite(IAsyncResult asyncResult)
        {
            try
            {
                var ar = (StreamState) asyncResult.AsyncState;
                _stream.EndWrite(asyncResult);

                asyncResult.AsyncWaitHandle.WaitOne();

                ar.Dispose();

                _writeEvent.Set();
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);
            }
        }

        private async Task<Status> InitializeConnection(int port = 8883)
        {
            var status = Status.Error;

            try
            {
                _socket = new Socket(_ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                {
                    UseOnlyOverlappedIO = true,
                    Blocking = true
                };

                await _socket.ConnectAsync(new IPEndPoint(_ipAddress, port));

                if (!_socket.Connected)
                    return Status.SocketError;

                var networkStream = new NetworkStream(_socket, true);

                if (_certificate != null)
                {
                    var sslStream = new SslStream(networkStream, false, null, null);

                    _x509CertificateCollection = new X509CertificateCollection(new X509Certificate[] {_certificate});

                    await sslStream.AuthenticateAsClientAsync(HostName, _x509CertificateCollection, SslProtocols.Tls12, true);

                    _stream = sslStream;

                    if (!sslStream.IsAuthenticated || !sslStream.IsEncrypted || !sslStream.IsMutuallyAuthenticated)
                    {
                        _logger.Log(LogLevel.Trace, "SSL Authentication failed");
                        return Status.SslError;
                    }
                }
                else
                {
                    _stream = networkStream;
                }

                status = Status.Initialized;
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);
            }

            return status;
        }

        private void BeginRead()
        {
            _readEvent.Wait();
            _readEvent.Reset();

            try
            {
                var socketState = new StreamState {CallBack = buffer => { ProcessRead(ref buffer);} };

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

        private static IEnumerable<byte[]> SplitInBuffer(byte[] buffer)
        {
            var startIndex = 0;

            var packetLength = MqttMessage.DecodeValue(buffer, startIndex + 1).Item1 + 2;

            if (buffer.Length > packetLength)
            {
                while (startIndex < buffer.Length)
                {
                    packetLength = MqttMessage.DecodeValue(buffer, startIndex + 1).Item1 + 2;

                    if (startIndex + packetLength > buffer.Length)
                        break;

                    var packetBuffer = new byte[packetLength];

                    Buffer.BlockCopy(buffer, startIndex, packetBuffer, 0, packetLength);

                    yield return packetBuffer;

                    startIndex += packetLength;
                }
            }
            else
            {
                yield return buffer;
            }
        }

        protected void ProcessRead(ref byte[] inBuffer)
        {
            foreach (var nextBuffer in SplitInBuffer(inBuffer))
            {
                var msgType = (MsgType) (byte) ((nextBuffer[0] & 0xf0) >> (byte) MsgOffset.Type);

                _logger.Log(LogLevel.Trace, $"In <= '{msgType}'");

                switch (msgType)
                {
                    case MsgType.Publish:
                        var msg = new Publish(nextBuffer);
                        PacketSyncSubject.OnNext(new PacketEnvelope {MsgType = MsgType.Publish, PacketId = msg.PacketId, Message = msg});

                        BeginWrite(new PublishAck(msg.PacketId));
                        break;
                    case MsgType.ConnectAck:
                        PacketSyncSubject.OnNext(new PacketEnvelope {MsgType = MsgType.ConnectAck});
                        break;
                    case MsgType.PingResponse:
                        break;
                    case MsgType.PublishAck:
                        var pubAck = new PublishAck(nextBuffer.ToArray());

                        PacketSyncSubject.OnNext(new PacketEnvelope {MsgType = MsgType.PublishAck, PacketId = pubAck.PacketId, Message = pubAck});
                        break;
                    default:
                        //var msgId = MqttMessage.BytesToUshort(new[] { buffer[2], buffer[3] });

                        //_ackSubject.OnNext(new Tuple<MsgType, int>(msgType, msgId));
                        break;
                }
            }

            _readEvent.Set();

            BeginRead();
        }

        private static void EndRead(IAsyncResult asyncResult)
        {
            try
            {
                byte[] newBuffer = null;

                var asyncState = (StreamState) asyncResult.AsyncState;

                asyncResult.AsyncWaitHandle.WaitOne();

                var bytesIn = _stream.EndRead(asyncResult);

                if (bytesIn > 0)
                {
                    newBuffer = new byte[bytesIn];

                    Buffer.BlockCopy(asyncState.Buffer, 0, newBuffer,0, bytesIn);
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

                Task.Delay(1000).Wait();
            }
        }

        #region PrivateFields

        private static Socket _socket;
        private static Stream _stream;
        private readonly string _pfxFileName;
        private readonly string _certPassword;

        private IPAddress _ipAddress;
        private X509Certificate2 _certificate;
        private X509CertificateCollection _x509CertificateCollection;
        private ushort _keepAliveInSeconds;
        private string _connectionId;

        #endregion
    }

    internal class PacketEnvelope{
        public MsgType MsgType { get; set; }

        public MqttMessage Message { get; set; }

        public int PacketId { get; set; }
    }

    internal class StreamState : IDisposable{
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