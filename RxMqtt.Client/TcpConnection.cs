using System;
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
using System.Threading.Tasks;
using NLog;
using RxMqtt.Shared;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Client
{
    internal class TcpConnection
    {
        private static Status _status = Status.Error;
        private readonly int _port;

        protected ISubject<Tuple<MsgType, int>> _ackSubject = new BehaviorSubject<Tuple<MsgType, int>>(null);

        protected ISubject<Publish> _publishSubject = new BehaviorSubject<Publish>(null);

        protected static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        protected string HostName;

        public IObservable<Publish> PublishObservable { get; set; }

        public IObservable<Tuple<MsgType, int>> AckObservable { get; set; }

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
            AckObservable = _ackSubject.AsObservable();
            PublishObservable = _publishSubject.AsObservable();

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
                    _task = Task.Factory.StartNew(BeginRead, TaskCreationOptions.LongRunning);
                }
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error,e);

                _status = Status.Error;
            }

            return _status;
        }

        internal void Write(MqttMessage message)
        {
            if (message == null)
                return;

            if (_status != Status.Initialized)
            {
                return;
            }

            _logger.Log(LogLevel.Trace, $"Out => {message.MsgType}");

            try
            {
                var buffer = message.GetBytes();

                if (buffer.Length > 1e+7)
                {
                    _logger.Log(LogLevel.Error, "Message size greater than maximum of 1e+7 or 10mb. Not publishing");
                    return;
                }

                var asyncResult = _stream.BeginWrite(buffer, 0, buffer.Length, EndWrite, _stream);

                asyncResult.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(_keepAliveInSeconds + 2));
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
                var ar = (Stream) asyncResult.AsyncState;
                ar.EndWrite(asyncResult);
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error,e);
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
                    ReceiveBufferSize = 500000,
                    SendBufferSize = 500000,
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
            try
            {
                var rs = new ReadState {Stream = _stream};

                var ar = _stream.BeginRead(rs.Buffer, 0, rs.Buffer.Length, EndRead, rs);

                ar.AsyncWaitHandle.WaitOne();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);
            }
        }

        protected void ProcessRead(byte[] buffer)
        {
            var msgType = (MsgType)(byte)((buffer[0] & 0xf0) >> (byte)MsgOffset.Type);

            _logger.Log(LogLevel.Trace, $"In <= '{msgType}'");

            switch (msgType)
            {
                case MsgType.Publish:
                    var msg = new Publish(buffer);
                    _publishSubject.OnNext(msg);

                    Write(new PublishAck(msg.PacketId));
                    break;
                case MsgType.ConnectAck:
                    _ackSubject.OnNext(new Tuple<MsgType, int>(msgType, 0));
                    break;
                case MsgType.PingResponse:
                    break;
                default:
                    var msgId = MqttMessage.BytesToUshort(new[] { buffer[2], buffer[3] });

                    _ackSubject.OnNext(new Tuple<MsgType, int>(msgType, msgId));
                    break;
            }
        }

        private void EndRead(IAsyncResult asyncResult)
        {
            try
            {
                var stream = (ReadState)asyncResult.AsyncState;

                var bytesIn = stream.Stream.EndRead(asyncResult);

                if (bytesIn > 0)
                {
                    var newBuffer = new byte[bytesIn];
                    Array.Copy(stream.Buffer, newBuffer, bytesIn);

                    ProcessRead(newBuffer);
                }
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

            BeginRead();
        }


        #region PrivateFields

        private Socket _socket;
        private static Stream _stream;
        private Task _task;
        private readonly string _pfxFileName;
        private readonly string _certPassword;
        private readonly string _connectionId;

        private IPAddress _ipAddress;
        private X509Certificate2 _certificate;
        private X509CertificateCollection _x509CertificateCollection;
        private ushort _keepAliveInSeconds;

        #endregion
    }

    internal class ReadState
    {

        public byte[] Buffer { get; set; } = new byte[500000];

        public Stream Stream { get; set; }

        public int BytesIn { get; set; }
    }
}