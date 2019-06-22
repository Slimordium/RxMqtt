using System.ServiceProcess;
using System.Threading;

namespace RxMqtt.Broker.Service
{
    public partial class RxMqttBroker : ServiceBase
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private Thread _brokerThread;

        public RxMqttBroker()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            _brokerThread = new Thread(() =>
            {
                MqttBroker mqttBroker = new MqttBroker();
                mqttBroker.StartListening(_cancellationTokenSource.Token); 
            })
            { IsBackground = true};
            _brokerThread.Start();
        }

        protected override void OnStop()
        {
            _cancellationTokenSource.Cancel();
        }
    }
}
