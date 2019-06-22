using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;
using Caliburn.Micro;
using RxMqttUI.Views;

namespace RxMqttUI.ViewModels
{
    
    public class ShellViewModel : Conductor<object>
    {
        public BindableCollection<ClientInstance> Clients { get; set; } = new BindableCollection<ClientInstance>();

        public string SelectedClient { get; set; }

        public string ClientId
        {
            get; set;

        }


        public ShellViewModel()
        {
            //ClientIds.Add(new ClientViewModel() { ClientId = "test1" });
            //ClientIds.Add(new ClientViewModel() { ClientId = "test2" });
        }

        public void AddClient()
        {
            //var screen = IoC.Get<ClientViewModel>();

            //ActivateItem(screen);s

            //var vm = new ClientViewModel() {ClientId = ClientId};

            //Clients.Add(vm);

            //ActivateItem(vm);

            //SelectedClient = vm;

            //NotifyOfPropertyChange(nameof(Clients));
            //NotifyOfPropertyChange(nameof(SelectedClientId));

            Clients.Add(new ClientInstance(ClientId, "127.0.0.1"));

            NotifyOfPropertyChange(nameof(Clients));
        }

        public void GetClient()
        {
            //var vm = ClientIds.Where(c => c.ClientId.Equals(SelectedClientId));

            //if (SelectedClient == null)
            //    return;

            //ActivateItem(SelectedClient);
        }

    }
}
