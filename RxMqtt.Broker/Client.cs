using System;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Text;
using NLog;
using RxMqtt.Shared;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Broker
{
    internal class Client
    {
        internal string ClientId { get; set; }
        internal byte[] Buffer { get; set; } = new byte[128000];
        internal Socket Socket { get; set; }
        private static ILogger _logger = LogManager.GetCurrentClassLogger();

        internal void IncomingMessage(Publish mqttMessage)
        {
            BeginSend( mqttMessage.GetBytes());
        }

        internal void Start()
        {
            Socket.BeginReceive(Buffer, 0, 128000, 0, ReceiveCallback, null);
        }

        private void ReceiveCallback(IAsyncResult asyncResult)
        {
            //var socket = (Socket)asyncResult.AsyncState;
            var bytesIn = 0;

            try
            {
                bytesIn = Socket.EndReceive(asyncResult);
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);
                return;
            }

            if (bytesIn <= 0)
                return;

            var newBuffer = new byte[bytesIn];

            Array.Copy(Buffer, 0, newBuffer, 0, bytesIn);

            Buffer = new byte[128000];

            var msgType = (MsgType)(byte)((newBuffer[0] & 0xf0) >> (byte)MsgOffset.Type);

            _logger.Log(LogLevel.Trace, $"In <= {msgType}");

            switch (msgType)
            {
                case MsgType.Publish:
                    var publishMsg = new Publish(newBuffer);

                    BeginSend(new PublishAck(publishMsg.PacketId).GetBytes());

                    MqttBroker.MqttMessagesSubject.OnNext(publishMsg);
                    break;
                case MsgType.Connect:
                    var connectMsg = new Connect(newBuffer);

                    _logger.Log(LogLevel.Trace, $"Client '{connectMsg.ClientId}' connected. Sending ConnectAck");

                    ClientId = connectMsg.ClientId;

                    _logger = LogManager.GetLogger(ClientId);

                    BeginSend(new ConnectAck().GetBytes());
                        
                    break;
                case MsgType.PingRequest:
                    _logger.Log(LogLevel.Trace, $"Sending ping response '{ClientId}'");
                    BeginSend(new PingResponse().GetBytes());
                    break;
                case MsgType.Subscribe:
                    var subscribeMsg = new Subscribe(newBuffer);

                    foreach (var topic in subscribeMsg.Topics)
                    {
                        MqttBroker.ObservableMqttMsgs.Where(m => m.MsgType == MsgType.Publish && m.Topic.Equals(topic, StringComparison.InvariantCultureIgnoreCase)).Subscribe(IncomingMessage);
                    }

                    BeginSend(new SubscribeAck(subscribeMsg.PacketId).GetBytes());
                    break;
                case MsgType.PublishAck:
                    break;
                default:
                    _logger.Log(LogLevel.Warn, $"Ignoring message: '{Encoding.UTF8.GetString(newBuffer)}'");
                    break;
            }

            Socket.BeginReceive(Buffer, 0, 128000, 0, ReceiveCallback, Socket);
        }

        private void BeginSend(byte[] buffer)
        {
            if (!Socket.Connected)
                return;

            Socket.BeginSend(buffer, 0, buffer.Length, 0, SendCallback, null);
        }

        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                var bytesSent = Socket.EndSend(ar);

                _logger.Log(LogLevel.Trace, $"Sent '{bytesSent}' bytes");
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);
            }
        }
    }
}