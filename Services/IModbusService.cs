using TempControl.Models;

namespace TempControl.Services;

/// <summary>
/// Transport-agnostic Modbus communication interface.
/// Implement separately for RTU (RS485 serial) and TCP.
/// </summary>
public interface IModbusTransport : IDisposable
{
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    Task<ushort[]> ReadHoldingRegistersAsync(int slaveId, int startAddress, int count, CancellationToken ct = default);
    Task<bool[]> ReadCoilsAsync(int slaveId, int startAddress, int count, CancellationToken ct = default);
    Task WriteSingleRegisterAsync(int slaveId, int address, ushort value, CancellationToken ct = default);
    Task WriteSingleCoilAsync(int slaveId, int address, bool value, CancellationToken ct = default);
}

/// <summary>
/// Factory that creates the correct transport based on slave configuration.
/// </summary>
public interface IModbusTransportFactory
{
    IModbusTransport CreateTransport(SlaveDeviceConfig config);
}
