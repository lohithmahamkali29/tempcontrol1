using System.Windows;

namespace TempControl
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            (MainWindow?.DataContext as ViewModels.MainViewModel)?.Dispose();
            base.OnExit(e);
        }
    }

}
