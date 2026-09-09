using System.Windows;
using TempControl.Services;

namespace TempControl.Views;

public partial class AuthorizationDialog : Window
{
    private readonly AuthorizationService _authorizationService;

    public UserRole? AuthenticatedRole { get; private set; }

    public AuthorizationDialog(AuthorizationService authorizationService)
    {
        InitializeComponent();
        _authorizationService = authorizationService;
        Loaded += (_, _) => UsernameTextBox.Focus();
    }

    private void AuthorizeButton_Click(object sender, RoutedEventArgs e)
    {
        var role = _authorizationService.ValidateCredentials(
            UsernameTextBox.Text.Trim(),
            PasswordBox.Password);

        if (role is not null)
        {
            AuthenticatedRole = role;
            DialogResult = true;
            return;
        }

        ErrorTextBlock.Visibility = Visibility.Visible;
        PasswordBox.Clear();
        PasswordBox.Focus();
    }
}
