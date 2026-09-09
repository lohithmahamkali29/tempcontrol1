using System.Windows;
using TempControl.Views;

namespace TempControl.Services;

public sealed class AuthorizationService
{
    // Change these credentials here when the authorized account changes.
    private const string AuthorizedUsername = "admin";
    private const string AuthorizedPassword = "admin123";

    public bool ValidateCredentials(string username, string password)
    {
        return string.Equals(username, AuthorizedUsername, StringComparison.Ordinal)
            && string.Equals(password, AuthorizedPassword, StringComparison.Ordinal);
    }

    public bool RequestAuthorization(string title = "Authorization Required")
    {
        var dialog = new AuthorizationDialog(this)
        {
            Title = title,
            Owner = Application.Current?.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        return dialog.ShowDialog() == true;
    }
}
