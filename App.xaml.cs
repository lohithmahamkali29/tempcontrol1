using System.Windows;
using TempControl.Services;

namespace TempControl
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public AuthorizationService AuthorizationService { get; } = new();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var mainWindow = new MainWindow(AuthorizationService);
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
