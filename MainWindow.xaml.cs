using System.ComponentModel;
using System.Windows;
using TempControl.Services;
using TempControl.ViewModels;

namespace TempControl;

public partial class MainWindow : Window
{
    private readonly AuthorizationService _authorizationService;
    private bool _disposed;

    public MainWindow(AuthorizationService authorizationService)
    {
        InitializeComponent();
        _authorizationService = authorizationService;
        DataContext = new MainViewModel(authorizationService);
        Closing += MainWindow_Closing;
    }

    /// <summary>
    /// Gates every close attempt (X button, Alt+F4, or a programmatic Close()) behind
    /// Supervisor authorization. The dialog is shown synchronously, so we decide
    /// e.Cancel once, up front, instead of cancelling then re-invoking Close() later
    /// (which WPF's close sequence doesn't reliably honor — the window stayed open
    /// until a second manual close). An Operator's own valid credentials are
    /// explicitly not sufficient.
    /// </summary>
    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_disposed)
            return;

        var role = _authorizationService.RequestAuthorization("Supervisor Authorization Required to Close");
        if (role != UserRole.Supervisor)
        {
            e.Cancel = true;

            if (role is not null)
            {
                MessageBox.Show(
                    "Only a Supervisor can close this application.",
                    "Permission Denied",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            return;
        }

        // Authorized — let this same close request proceed; no cancel, no re-close.
        _disposed = true;
        (DataContext as MainViewModel)?.Dispose();
    }
}
