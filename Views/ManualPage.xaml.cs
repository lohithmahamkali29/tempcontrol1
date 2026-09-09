using System.Windows;
using System.Windows.Controls;
using TempControl.ViewModels;

namespace TempControl.Views
{
    /// <summary>
    /// Interaction logic for ManualPage.xaml
    /// </summary>
    public partial class ManualPage : UserControl
    {
        public ManualPage()
        {
            InitializeComponent();

            Loaded += (_, _) => TryNotifyNavigated();
            IsVisibleChanged += (_, e) =>
            {
                if (e.NewValue is true)
                    TryNotifyNavigated();
            };
            DataContextChanged += (_, _) => TryNotifyNavigated();
        }

        private void TryNotifyNavigated()
        {
            if (IsVisible && DataContext is ManualPageViewModel vm)
                vm.OnNavigatedTo();
        }
    }
}
