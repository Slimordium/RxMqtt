using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;
using Caliburn.Micro;

namespace RxMqttUI.ViewModels
{
    public class ShellViewModel : Screen
    {

        public IObservableCollection<PivotItem> PivotViewItems { get; set; } = new BindableCollection<PivotItem>();

        public ShellViewModel()
        {
            PivotViewItems.Add(new PivotItem { Content = "Broker test" });
            PivotViewItems.Add(new PivotItem { Content = "Client test" });
            NotifyOfPropertyChange(nameof(PivotViewItems));

        }

        public async Task Back()
        {
            PivotViewItems = new BindableCollection<PivotItem>();
            PivotViewItems.Add(new PivotItem { Content = "Client test" });
            NotifyOfPropertyChange(nameof(PivotViewItems));
        }

        public async Task Forward()
        {
            PivotViewItems = new BindableCollection<PivotItem>();
            PivotViewItems.Add(new PivotItem { Content = "Broker test" });
            NotifyOfPropertyChange(nameof(PivotViewItems));
        }
    }
}
