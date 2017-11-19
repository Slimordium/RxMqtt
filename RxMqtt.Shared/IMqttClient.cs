using System;
using System.Threading.Tasks;

namespace RxMqtt.Shared
{
    public interface IMqttClient : IDisposable
    {
        Task<Status> InitializeAsync();

        Task SubscribeAsync(Subscription subscription);

        Task UnsubscribeAsync(Subscription subscription);
    
        Task<bool> PublishAsync(string message, string topic, TimeSpan timeout = default(TimeSpan));
    }
}