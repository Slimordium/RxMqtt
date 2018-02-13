using System;
using System.Threading;
using System.Threading.Tasks;

namespace RxMqtt.Shared
{
    public interface IMqttClient : IDisposable
    {
        Task<Status> InitializeAsync();

        Task SubscribeAsync(ISubscription subscription);

        Task UnsubscribeAsync(ISubscription subscription);
    
        Task<bool> PublishAsync(string message, string topic, TimeSpan timeout = default(TimeSpan));

        
    }
}