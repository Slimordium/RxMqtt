using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using RxMqtt.Shared;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Client
{
    internal class TcpConnection : BaseConnection, IConnection
    {
        private readonly object _syncWrite = new object();
        private IObservable<MqttMessage> _observable;
        private Status _status = Status.Error;
        private static int _publishCount;
        private readonly int _port;

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
            _keepAliveInSeconds = (ushort) keepAliveInSeconds;
            _connectionId = connectionId;
            _pfxFileName = certFileName;
            _certPassword = pfxPw;
            _port = port;
            HostName = hostName;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Dispose(true);
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
                    BeginReadWrite();
                }
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error,e);

                _status = Status.Error;
            }

            return _status;
        }

        private void BeginReadWrite()
        {
            BeginRead(_stream);

            InitializeObservables();

            _observable = WriteSubject.AsObservable().Synchronize(_syncWrite);

            _observable.Subscribe(message =>
            {
                if (_status != Status.Initialized)
                {
                    return;
                }

                if (_publishCount >= 3)
                {
                    _logger.Log(LogLevel.Trace, $"Hung publish?");
                    ReEstablishConnection();
                    return;
                }

                _logger.Log(LogLevel.Trace, $"Out => {message.MsgType}");

                try
                {
                    var buffer = message.GetBytes();

                    if (buffer.Length > 128000)
                    {
                        _logger.Log(LogLevel.Trace, "Message size greater than maximum of 128KB?");
                        return;
                    }

                    Interlocked.Increment(ref _publishCount);

                    var asyncResult = _stream.BeginWrite(buffer, 0, buffer.Length, EndWrite, _stream);

                    asyncResult.AsyncWaitHandle.WaitOne();
                }
                catch (ObjectDisposedException)
                {
                }
                catch (Exception)
                {
                    if (_disposed)
                        return;

                    ReEstablishConnection();
                }
            });
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
                _socket = new Socket(_ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp) {UseOnlyOverlappedIO = true};

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

        protected override void DisposeStreams()
        {
            _logger.Log(LogLevel.Trace, "Disposing stream");

            if (_stream.CanWrite)
            {
                WriteSubject.OnNext(new DisconnectMsg());

                Task.Delay(2000).Wait();
            }

            try
            {
                _socket.Shutdown(SocketShutdown.Both);
                _socket.Disconnect(true);
                _socket.Dispose();
                _stream.Dispose();
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);
            }
        }

        private static void BeginRead(Stream stream)
        {
            try
            {
                var buffer = new byte[128000];

                stream.BeginRead(buffer, 0, buffer.Length, asyncResult => { EndRead(asyncResult, buffer); }, stream);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);
            }
        }

        private static void EndRead(IAsyncResult asyncResult, byte[] buffer)
        {
            try
            {
                var stream = (Stream) asyncResult.AsyncState;

                var bytesIn = stream.EndRead(asyncResult);

                if (bytesIn > 0)
                {
                    var newBuffer = new byte[bytesIn];
                    Array.Copy(buffer, newBuffer, bytesIn);

                    var msgType = (MsgType) (byte) ((newBuffer[0] & 0xf0) >> (byte) MsgOffset.Type);

                    _logger.Log(LogLevel.Trace, $"In <= {msgType}");

                    switch (msgType) {
                        case MsgType.Publish:
                            var msg = new Publish(newBuffer);
                            _publishSubject.OnNext(msg);
                            break;
                        case MsgType.ConnectAck:
                            _ackSubject.OnNext(new Tuple<MsgType, int>(msgType, 0));
                            break;
                        case MsgType.PingResponse:
                            break;
                        default:
                            _ackSubject.OnNext(new Tuple<MsgType, int>(msgType, MqttMessage.BytesToUshort(new [] { newBuffer[1], newBuffer[2] })));
                            break;
                    }

                    Interlocked.Decrement(ref _publishCount);
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception)
            {
                if (_disposed)
                    return;

                //Expected if disconnected
            }

            BeginRead(_stream);
        }

        private static Task _connectTask;

        protected override void ReEstablishConnection()
        {
            new ManualResetEventSlim(false).Wait(500);

            if (Interlocked.Exchange(ref _reconnecting, 1) != 0 || _disposed)
                return;

            try
            {
                _connectTask?.Dispose();
            }
            catch (Exception)
            {
                //may occur - expected
            }

            _connectTask = Task.Factory.StartNew(() =>
            {
                var status = Status.Initializing;

                while (status != Status.Initialized)
                {
                    _logger.Log(LogLevel.Trace, "Trying to re-establish");

                    try
                    {
                        _socket.Shutdown(SocketShutdown.Both);
                        _socket.Disconnect(true);
                        _socket.Dispose();
                    }
                    catch (Exception)
                    {
                        //This is expected
                    }

                    Task.Delay(1000).Wait();

                    status = InitializeConnection().Result;

                    if (status == Status.Initialized)
                    {
                        _publishCount = 0;

                        BeginRead(_stream);

                        WriteSubject.OnNext(new Connect(_connectionId, _keepAliveInSeconds));
                    }
                    else
                    {
                        Task.Delay(5000).Wait(); //Avoid CPU maxing retry loop
                    }
                }

                _logger.Log(LogLevel.Trace, "Exiting re-establish connection method");

                Interlocked.Exchange(ref _reconnecting, 0);

            }, TaskCreationOptions.LongRunning);
        }

        protected virtual void Dispose(bool disposing)
        {
            _disposed = true;

            if (!disposing)
                return;

            DisposeStreams();

            try
            {
                _connectTask?.Dispose();
            }
            catch (Exception)
            {
                //This is expected
            }
        }

        #region PrivateFields

        private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();
        private static Socket _socket;
        private static Stream _stream;

        private readonly string _pfxFileName;
        private readonly string _certPassword;
        private readonly string _connectionId;
        private readonly ushort _keepAliveInSeconds;

        private IPAddress _ipAddress;
        private X509Certificate2 _certificate;
        private X509CertificateCollection _x509CertificateCollection;
        private static bool _disposed;

        private static int _reconnecting;

        #endregion
    }
}