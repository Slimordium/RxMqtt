using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NLog;
using RxMqtt.Shared.Messages;

namespace RxMqtt.Shared
{
    public class Subscription
    {
        internal string Topic { get; }

        internal IDisposable Disposable { get; set; }

        internal ILogger Logger = LogManager.GetCurrentClassLogger();

        public event Func<object, string, Task> SubscriptionEvent;

        public Subscription(Func<object, string, Task> handler, string topic)
        {
            SubscriptionEvent = handler;
            Topic = topic;
        }

        ~Subscription()
        {
            Disposable?.Dispose();
        }

        internal void PublishReceived(IList<Publish> publishes)
        {
            var msg = publishes[0];

            try
            {
                if (!msg.Topic.Equals(Topic, StringComparison.InvariantCultureIgnoreCase))
                    return;

                var handler = SubscriptionEvent;

                if (handler == null)
                    return;

                var invocationList = handler.GetInvocationList();

                Parallel.ForEach(invocationList, func => { handler(this, Encoding.UTF8.GetString(msg.Message)); });
            }
            catch (Exception e)
            {
                Logger.Log(LogLevel.Error, e);
            }
        }
    }
}
