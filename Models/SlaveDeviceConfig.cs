using CommunityToolkit.Mvvm.ComponentModel;

namespace TempControl.Models;

public enum ModbusConnectionType
{
    Rtu,
    Tcp,
    OmronFinsTcp
}

public partial class SlaveDeviceConfig : ObservableObject
{
    public int SlaveId { get; init; }
    public string Name { get; init; } = string.Empty;
    public ModbusConnectionType ConnectionType { get; init; } = ModbusConnectionType.Rtu;

    // RS485 RTU properties
    public string PortName { get; init; } = "COM1";
    public int BaudRate { get; init; } = 9600;
    public int DataBits { get; init; } = 8;
    public int StopBits { get; init; } = 1;
    public string Parity { get; init; } = "None";

    // TCP properties
    public string IpAddress { get; init; } = "192.168.1.1";
    public int TcpPort { get; init; } = 502;

    // Polling
    public int PollIntervalMs { get; init; } = 1000;

    // Explicit register and coil definitions — each address listed individually
    public List<ModbusRegisterDef> HoldingRegisters { get; init; } = [];
    public List<ModbusRegisterDef> Coils { get; init; } = [];

    // ?? Runtime status (updated by polling service on UI thread) ??
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _lastPollStatus = "Not polled";

    // ?? Energy register polling throttle (prevents connection exhaustion) ??
    internal int LastEnergyPollTick { get; set; }

    /// <summary>Connection detail string for display (computed from RTU or TCP props).</summary>
    public string ConnectionDetail => ConnectionType switch
    {
        ModbusConnectionType.OmronFinsTcp => $"{IpAddress}:{TcpPort} (FINS)",
        ModbusConnectionType.Tcp          => $"{IpAddress}:{TcpPort}",
        _                                 => $"{PortName} @ {BaudRate}"
    };
}
