using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Shared
{
    public class SessionSubscription : ISubscription
    {
        public string Topic { get; }

        public IDisposable SubscriptionDisposable { get; private set; }

        private readonly ILogger _logger;

        private event Func<string, Task> _subscriptionCallBackTask;

        private readonly SortedDictionary<int, string> _messagePieces = new SortedDictionary<int, string>();

        private readonly object _lock = new object();

        private long _multipartReceiveCount;

        private long _totalParts;

        /// <summary>
        /// This will invoke the callback ONLY when all pieces that belong to a session are received
        /// </summary>
        /// <param name="handler"></param>
        /// <param name="topic"></param>
        public SessionSubscription(Func<string, Task> handler, string topic)
        {
            _logger = LogManager.GetLogger($"SessionSubscription-{topic}");

            _subscriptionCallBackTask = handler;
            Topic = topic;
        }

        public void Subscribe(IObservable<Publish> observable)
        {
            SubscriptionDisposable = observable.Subscribe(PublishReceived);
        }

        /// <summary>
        /// Topic format should be: "myTopic/identifier/numberOfMessagesInSession/index"
        /// </summary>
        /// <param name="msg"></param>
        private void PublishReceived(Publish msg)
        {
            if (msg == null)
                return;

            try
            {
                if (!msg.Topic.Equals(Topic, StringComparison.InvariantCultureIgnoreCase))
                    return;

                var splitTopic = msg.Topic.Split('/');

                Interlocked.Increment(ref _multipartReceiveCount);

                if (Interlocked.Read(ref _totalParts) == 0)
                {
                    var count = Convert.ToInt32(splitTopic[2]);

                    Interlocked.Exchange(ref _totalParts, count);

                    _logger.Log(LogLevel.Info, $"Expecting {count} messages in this session");
                }

                lock (_lock)
                {
                    _messagePieces.Add(Convert.ToInt32(splitTopic[3]), Encoding.UTF8.GetString(msg.Message));

                    if (_messagePieces.Count < _multipartReceiveCount)
                    {
                        _logger.Log(LogLevel.Info, $"Message '{_messagePieces.Count}' of '{Interlocked.Read(ref _multipartReceiveCount)}'");

                        return;
                    }
                }

                _logger.Log(LogLevel.Info, "Building final message...");

                var handler = _subscriptionCallBackTask;

                if (handler == null)
                    return;

                var returnMsgBuilder = new StringBuilder();

                foreach (var part in _messagePieces) //Sorted dictionary
                {
                    returnMsgBuilder.Append(part);
                }

                var invocationList = handler.GetInvocationList();

                Parallel.ForEach(invocationList, async func => { await handler(returnMsgBuilder.ToString()); });

                _logger.Log(LogLevel.Info, "Session completed");
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);
            }
        }
    }
}
