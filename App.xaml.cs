using System.Windows;
using TempControl.Services;

namespace TempControl
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public AuthorizationService AuthorizationService { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                AuthorizationService = new AuthorizationService();
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Application Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
                return;
            }

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
