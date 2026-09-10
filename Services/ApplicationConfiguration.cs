using System.IO;
using System.Text.Json;

namespace TempControl.Services;

public sealed record ApplicationConfiguration
{
    public AuthorizationConfiguration Authorization { get; init; } = new();
    public PlcRegisterConfiguration PlcRegisters { get; init; } = new();

    public static ApplicationConfiguration Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        try
        {
            if (!File.Exists(path))
                throw new InvalidOperationException($"Application configuration file was not found: {path}");

            var configuration = JsonSerializer.Deserialize<ApplicationConfiguration>(File.ReadAllText(path));
            if (configuration is null
                || !configuration.Authorization.IsValid
                || !configuration.PlcRegisters.IsValid
                || configuration.PlcRegisters.Zone1SafetyTemperature.Equals(
                    configuration.PlcRegisters.Zone2SafetyTemperature,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Application configuration is missing valid authorization or distinct PLC safety register settings.");
            }

            return configuration with
            {
                Authorization = configuration.Authorization with
                {
                    SessionDuration = TimeSpan.FromMinutes(configuration.Authorization.SessionTimeoutMinutes)
                },
                PlcRegisters = configuration.PlcRegisters with
                {
                    Zone1SafetyTemperatureAddress = ParseRegisterAddress(configuration.PlcRegisters.Zone1SafetyTemperature),
                    Zone2SafetyTemperatureAddress = ParseRegisterAddress(configuration.PlcRegisters.Zone2SafetyTemperature)
                }
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Application configuration is malformed: {path}", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"Application configuration could not be read: {path}", ex);
        }
    }

    private static int ParseRegisterAddress(string value)
    {
        if (!value.StartsWith("D", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(value[1..], out var address)
            || address < 0
            || address > ushort.MaxValue)
        {
            throw new InvalidOperationException($"Invalid PLC register address '{value}'. Expected format D<number>.");
        }

        return address;
    }
}

public sealed record AuthorizationConfiguration
{
    public CredentialConfiguration Operator { get; init; } = new();
    public CredentialConfiguration Supervisor { get; init; } = new();
    public double SessionTimeoutMinutes { get; init; }
    public TimeSpan SessionDuration { get; init; }
    public bool IsValid => Operator.IsValid && Supervisor.IsValid && SessionTimeoutMinutes > 0;
}

public sealed record CredentialConfiguration
{
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public bool IsValid => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
}

public sealed record PlcRegisterConfiguration
{
    public string Zone1SafetyTemperature { get; init; } = string.Empty;
    public string Zone2SafetyTemperature { get; init; } = string.Empty;
    public int Zone1SafetyTemperatureAddress { get; init; }
    public int Zone2SafetyTemperatureAddress { get; init; }
    public bool IsValid => !string.IsNullOrWhiteSpace(Zone1SafetyTemperature)
                           && !string.IsNullOrWhiteSpace(Zone2SafetyTemperature);
}
