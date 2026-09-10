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
    public ApplicationConfiguration Configuration { get; }
    private readonly AuthorizationConfiguration _configuration;

    private readonly DispatcherTimer _sessionTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime? _sessionExpiresAtUtc;

    public UserRole? CurrentRole { get; private set; }
    public bool HasActiveSession => CurrentRole is not null && _sessionExpiresAtUtc is not null && DateTime.UtcNow < _sessionExpiresAtUtc.Value;
    public bool HasExpiredSession => CurrentRole is not null && _sessionExpiresAtUtc is not null && DateTime.UtcNow >= _sessionExpiresAtUtc.Value;

    public event EventHandler? CurrentRoleChanged;

    public AuthorizationService()
    {
        Configuration = ApplicationConfiguration.Load();
        _configuration = Configuration.Authorization;
        _sessionTimer.Tick += (_, _) =>
        {
            if (HasExpiredSession)
                ClearAuthorization();
        };
        _sessionTimer.Start();
    }

    public UserRole? ValidateCredentials(string username, string password)
    {
        if (string.Equals(username, _configuration.Operator.Username, StringComparison.Ordinal)
            && string.Equals(password, _configuration.Operator.Password, StringComparison.Ordinal))
        {
            return UserRole.Operator;
        }

        if (string.Equals(username, _configuration.Supervisor.Username, StringComparison.Ordinal)
            && string.Equals(password, _configuration.Supervisor.Password, StringComparison.Ordinal))
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
        _sessionExpiresAtUtc = DateTime.UtcNow.Add(_configuration.SessionDuration);
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
