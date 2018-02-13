using System;
using System.Text;
using System.Threading.Tasks;
using NLog;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Shared
{
    public class Subscription : ISubscription
    {
        public string Topic { get; }

        public IDisposable SubscriptionDisposable { get; private set; }

        private readonly ILogger _logger;

        private readonly Func<string, Task> _subscriptionCallback;

        public Subscription(Func<string, Task> callback, string topic)
        {
            _logger = LogManager.GetLogger($"Subscription-{topic}");

            _subscriptionCallback = callback;

            Topic = topic;
        }

        public void Subscribe(IObservable<Publish> observable)
        {
            SubscriptionDisposable = observable.Subscribe(PublishReceived);
        }

        private void PublishReceived(Publish msg)
        {
            if (msg == null)
                return;

            try
            {
                if (!msg.Topic.StartsWith(Topic, StringComparison.InvariantCultureIgnoreCase))
                    return;

                var handler = _subscriptionCallback;

                if (handler == null)
                    return;

                var invocationList = handler.GetInvocationList();

                Parallel.ForEach(invocationList, async func => { await handler(Encoding.UTF8.GetString(msg.Message)); });
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e);
            }
        }
    }
}
