using Caliburn.Micro;
using RxMqtt.Client;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;

namespace RxMqttUI.ViewModels
{
    public class ClientInstance : UserControl
    {
        public string ClientId { get; }

        public string HostName { get; }

        private MqttClient _mqttClient;

        public BindableCollection<string> Log { get; set; } = new BindableCollection<string>();

        public ClientInstance(string clientId, string hostName)
        {
            ClientId = clientId;
            HostName = hostName;
        }

        public async Task Initialize()
        {
            _mqttClient = new MqttClient(ClientId, HostName, 1883);
            var status = await _mqttClient.InitializeAsync();

            LogMsg(status.ToString());
        }

        private void LogMsg(string msg)
        {
            if (Log.Count > 500)
            {
                Log.RemoveAt(499);
            }

            Log.Insert(0, msg);
        }
    }
}