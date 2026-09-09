using System.Windows;
using TempControl.Services;
using TempControl.ViewModels;

namespace TempControl;

public partial class MainWindow : Window
{
    public MainWindow(AuthorizationService authorizationService)
    {
        InitializeComponent();
        DataContext = new MainViewModel(authorizationService);
        Closing += (_, _) => (DataContext as MainViewModel)?.Dispose();
    }
}