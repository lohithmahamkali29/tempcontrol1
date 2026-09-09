using System.Windows;
using TempControl.Services;

namespace TempControl.Views;

public partial class AuthorizationDialog : Window
{
    private readonly AuthorizationService _authorizationService;

    public AuthorizationDialog(AuthorizationService authorizationService)
    {
        InitializeComponent();
        _authorizationService = authorizationService;
        Loaded += (_, _) => UsernameTextBox.Focus();
    }

    private void AuthorizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_authorizationService.ValidateCredentials(
                UsernameTextBox.Text.Trim(),
                PasswordBox.Password))
        {
            DialogResult = true;
            return;
        }

        ErrorTextBlock.Visibility = Visibility.Visible;
        PasswordBox.Clear();
        PasswordBox.Focus();
    }
}
