using System.Windows;
using TempControl.ViewModels;

namespace TempControl;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Closing += (_, _) => (DataContext as MainViewModel)?.Dispose();
    }
}