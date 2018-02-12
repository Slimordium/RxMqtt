using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using NLog;
using RxMqtt.Shared;
using RxMqtt.Shared.Messages;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("RxMqtt.Broker")]
[assembly: InternalsVisibleTo("RxMqtt.Client")]

namespace RxMqtt.Client
{
    internal abstract class BaseConnection
    {
        protected ISubject<Tuple<MsgType, int>> _ackSubject;

        protected ISubject<Publish> _publishSubject;
        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        protected string HostName;

        public IObservable<Publish> PublishObservable { get; set; }

        public IObservable<Tuple<MsgType, int>> AckObservable { get; set; }

        public ISubject<MqttMessage> WriteSubject { get; } = new BehaviorSubject<MqttMessage>(null);

        protected abstract void DisposeStreams();

        protected abstract void ReEstablishConnection();

        protected void InitializeObservables()
        {
            try
            {
                _ackSubject = new BehaviorSubject<Tuple<MsgType, int>>(null);
                _publishSubject = new BehaviorSubject<Publish>(null);

                AckObservable = _ackSubject.AsObservable();
                PublishObservable = _publishSubject.AsObservable();
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);
            }
        }

        protected void OnReceived(byte[] buffer)
        {
            var msgType = (MsgType)(byte)((buffer[0] & 0xf0) >> (byte)MsgOffset.Type);

            _logger.Log(LogLevel.Trace, $"In <= '{msgType}'");

            switch (msgType)
            {
                case MsgType.Publish:
                    var msg = new Publish(buffer);
                    _publishSubject.OnNext(msg);

                    WriteSubject.OnNext(new PublishAck(msg.PacketId));
                    break;
                case MsgType.ConnectAck:
                    _ackSubject.OnNext(new Tuple<MsgType, int>(msgType, 0));
                    break;
                case MsgType.PingResponse:
                    break;
                default:
                    var msgId = MqttMessage.BytesToUshort(new[] {buffer[2], buffer[3]});

                    _ackSubject.OnNext(new Tuple<MsgType, int>(msgType, msgId));
                    break;
            }
        }

        public async Task<bool> WaitForAck(MsgType msgType, CancellationToken cancellationToken = default(CancellationToken), int? packetId = null)
        {
            var tcs = new TaskCompletionSource<bool>(cancellationToken);

            IDisposable disposable = null;

            var onNext = new Action<Tuple<MsgType, int>>(ackMessage =>
            {
                if (ackMessage == null)
                    return;

                tcs.SetResult(true);
            });

            if (packetId == null)
                disposable = AckObservable.Where(p => p != null && p.Item1 == msgType).Subscribe(onNext);
            else
                disposable = AckObservable.Where(p => p != null && p.Item1 == msgType && p.Item2 == packetId).Subscribe(onNext);

            var waitForAck = await tcs.Task;

            disposable.Dispose();

            return waitForAck;
        }
    }
}