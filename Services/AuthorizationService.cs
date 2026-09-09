using System.Windows;
using System.Windows.Threading;
using TempControl.Views;

namespace TempControl.Services;

public enum UserRole
{
    Operator,
    Supervisor
}

public sealed class AuthorizationService
{
    private const string OperatorUsername = "operator";
    private const string OperatorPassword = "operator123";
    private const string SupervisorUsername = "supervisor";
    private const string SupervisorPassword = "supervisor123";
    private static readonly TimeSpan SessionDuration = TimeSpan.FromMinutes(2);

    private readonly DispatcherTimer _sessionTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime? _sessionExpiresAtUtc;

    public UserRole? CurrentRole { get; private set; }
    public bool HasActiveSession => CurrentRole is not null && _sessionExpiresAtUtc is not null && DateTime.UtcNow < _sessionExpiresAtUtc.Value;
    public bool HasExpiredSession => CurrentRole is not null && _sessionExpiresAtUtc is not null && DateTime.UtcNow >= _sessionExpiresAtUtc.Value;

    public event EventHandler? CurrentRoleChanged;

    public AuthorizationService()
    {
        _sessionTimer.Tick += (_, _) =>
        {
            if (HasExpiredSession)
                ClearAuthorization();
        };
        _sessionTimer.Start();
    }

    public UserRole? ValidateCredentials(string username, string password)
    {
        if (string.Equals(username, OperatorUsername, StringComparison.Ordinal)
            && string.Equals(password, OperatorPassword, StringComparison.Ordinal))
        {
            return UserRole.Operator;
        }

        if (string.Equals(username, SupervisorUsername, StringComparison.Ordinal)
            && string.Equals(password, SupervisorPassword, StringComparison.Ordinal))
        {
            return UserRole.Supervisor;
        }

        return null;
    }

    public UserRole? RequestAuthorization(string title = "Authorization Required")
    {
        var dialog = new AuthorizationDialog(this)
        {
            Title = title,
            Owner = Application.Current?.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        if (dialog.ShowDialog() != true || dialog.AuthenticatedRole is null)
            return null;

        CurrentRole = dialog.AuthenticatedRole;
        _sessionExpiresAtUtc = DateTime.UtcNow.Add(SessionDuration);
        CurrentRoleChanged?.Invoke(this, EventArgs.Empty);
        return CurrentRole;
    }

    public void ClearAuthorization()
    {
        if (CurrentRole is null && _sessionExpiresAtUtc is null)
            return;

        CurrentRole = null;
        _sessionExpiresAtUtc = null;
        CurrentRoleChanged?.Invoke(this, EventArgs.Empty);
    }
}
