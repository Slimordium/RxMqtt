using System;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using RxMqtt.Shared;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Client
{
    //TODO: Support Websockets - this code does not work with secure sockets. The URI format is ws://127.0.0.1 or whatever IP your broker is running on
    internal class WebsocketConnection : BaseConnection, IConnection{

        private ClientWebSocket _clientWebSocket;
        private ushort _keepAliveInSeconds;
        private string _connectionId;
        private string _pfxFileName;
        private string _certPassword;
        private int _port;
        private IObservable<MqttMessage> _observable;
        private readonly object _syncWrite = new object();

        internal WebsocketConnection
            (
                string connectionId,
                string hostName,
                int keepAliveInSeconds,
                int port,
                string certFileName = "",
                string pfxPw = ""
            )
        {
            _keepAliveInSeconds = (ushort)keepAliveInSeconds;
            _connectionId = connectionId;
            _pfxFileName = certFileName;
            _certPassword = pfxPw;
            _port = port;
            HostName = hostName;
        }

        protected override void DisposeStreams()
        {
        }

        protected override void ReEstablishConnection()
        {
        }

        public void Dispose()
        {
        }

        private Thread _thread;

        public async Task<Status> Initialize()
        {
            try
            {
                _clientWebSocket = new ClientWebSocket();


                await _clientWebSocket.ConnectAsync(new Uri("test"), CancellationToken.None);
                //await Task.WhenAll(Receive(webSocket), Send(webSocket));

                InitializeObservables();

                _thread = new Thread(async () =>
                {
                    var result = await Read();

                    OnReceived(result);
                });
                _thread.IsBackground = true;
                _thread.Start();

                _observable = WriteSubject.AsObservable().Synchronize(_syncWrite);
                _observable.Subscribe(OnNextMessageToWrite);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception: {0}", ex);
            }
            finally
            {
                _clientWebSocket?.Dispose();
            }

            return Status.Initialized;
        }

        private async void OnNextMessageToWrite(MqttMessage mqttMessage)
        {
            var arraySegment = new ArraySegment<byte>(mqttMessage.GetBytes());

            await _clientWebSocket.SendAsync(arraySegment, WebSocketMessageType.Binary, true, CancellationToken.None);
        }

        private async Task<byte[]> Read()
        {
            var arraySegment = new ArraySegment<byte>();

            var result = await _clientWebSocket.ReceiveAsync(arraySegment, new CancellationToken());

            return arraySegment.Array;
        }
    }
}