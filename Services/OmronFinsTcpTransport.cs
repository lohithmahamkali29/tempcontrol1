using HslCommunication.Profinet.Omron;
using TempControl.Models;

namespace TempControl.Services;

/// <summary>
/// IModbusTransport implementation backed by HslCommunication's OmronFinsNet (FINS/TCP).
///
/// Address mapping:
///   Holding registers  →  D-memory words    e.g. address 100       → "D100"
///   Input coils        →  CIO area (inputs) e.g. address 0         → "CIO0.0"
///                                                address 16        → "CIO1.0"
///   Output coils       →  CIO area word 100  e.g. address 100      → "CIO100.0"
///                                                 address 105      → "CIO100.5"
/// </summary>
internal sealed class OmronFinsTcpTransport : IModbusTransport
{
    private OmronFinsNet _net;
    private static bool _poolResetAttempted = false;
    private static readonly object _poolLock = new object();

    public bool IsConnected { get; private set; }

    public OmronFinsTcpTransport(SlaveDeviceConfig config)
    {
        _net = new OmronFinsNet(config.IpAddress, config.TcpPort);

        lock (_poolLock)
        {
            if (!_poolResetAttempted)
            {
                DiagnosticLogger.Instance.Log("FINS", "One-time connection pool reset...");
                try { _net.ConnectClose(); } catch { }
                System.Threading.Thread.Sleep(500);
                _poolResetAttempted = true;
                DiagnosticLogger.Instance.Log("FINS", "Pool reset done");
            }
        }
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        DiagnosticLogger.Instance.Log("FINS", $"Connecting to {_net.IpAddress}:{_net.Port}...");
        var result = await Task.Run(_net.ConnectServer, ct);
        if (!result.IsSuccess)
        {
            DiagnosticLogger.Instance.Log("FINS", $"Connect FAILED: {result.Message}");
            throw new InvalidOperationException($"Omron FINS connect failed: {result.Message}");
        }
        IsConnected = true;
        DiagnosticLogger.Instance.Log("FINS", "Connected OK");
    }

    public Task DisconnectAsync()
    {
        DiagnosticLogger.Instance.Log("FINS", "Disconnecting");
        try { _net.ConnectClose(); } catch { }
        IsConnected = false;
        return Task.CompletedTask;
    }

    public async Task<ushort[]> ReadHoldingRegistersAsync(
        int slaveId, int startAddress, int count, CancellationToken ct = default)
    {
        var address = $"D{startAddress}";
        DiagnosticLogger.Instance.Log("FINS-READ", $"ReadRegisters {address} ×{count}");
        var result = await Task.Run(() => _net.ReadUInt16(address, (ushort)count), ct);
        if (!result.IsSuccess)
        {
            DiagnosticLogger.Instance.Log("FINS-READ", $"FAILED {address}: {result.Message}");
            throw new InvalidOperationException($"FINS read D{startAddress}×{count} failed: {result.Message}");
        }
        DiagnosticLogger.Instance.Log("FINS-READ", $"OK {address} = [{string.Join(", ", result.Content)}]");
        return result.Content;
    }

    public async Task<bool[]> ReadCoilsAsync(
        int slaveId, int startAddress, int count, CancellationToken ct = default)
    {
        // Map coil address to Omron CIO memory area:
        //   Inputs  (address   0–99 ) → CIO{address/16}.{address%16}
        //                               e.g. address 0  → CIO0.0,  address 16 → CIO1.0
        //   Outputs (address 100–115) → CIO100.{address-100}
        //                               e.g. address 100 → CIO100.0, address 105 → CIO100.5
        //   Manual  (address 400–415) → CIO400.{address-400}
        string address;
        if (startAddress >= 10000)
        {
            var word = (startAddress - 10000) / 16;
            var bit = (startAddress - 10000) % 16;
            address = $"W{word}.{bit}";
        }
        else
        {
            var (word, bit) = startAddress switch
            {
                >= 400 and <= 415 => (400, startAddress - 400),
                >= 100 and <= 115 => (100, startAddress - 100),
                _ => (startAddress / 16, startAddress % 16)
            };
            address = $"CIO{word}.{bit}";
        }
        DiagnosticLogger.Instance.Log("FINS-READ", $"ReadCoils {address} ×{count}");
        var result = await Task.Run(() => _net.ReadBool(address, (ushort)count), ct);
        if (!result.IsSuccess)
        {
            DiagnosticLogger.Instance.Log("FINS-READ", $"FAILED {address}: {result.Message}");
            throw new InvalidOperationException($"FINS read {address}×{count} failed: {result.Message}");
        }
        DiagnosticLogger.Instance.Log("FINS-READ", $"OK {address} = [{string.Join(", ", result.Content)}]");
        return result.Content;
    }

    public async Task WriteSingleRegisterAsync(
        int slaveId, int address, ushort value, CancellationToken ct = default)
    {
        DiagnosticLogger.Instance.Log("FINS-WRITE", $"WriteRegister D{address} = {value}");
        var result = await Task.Run(() => _net.Write($"D{address}", value), ct);
        if (!result.IsSuccess)
        {
            DiagnosticLogger.Instance.Log("FINS-WRITE", $"FAILED D{address}: {result.Message}");
            throw new InvalidOperationException($"FINS write D{address} failed: {result.Message}");
        }
        DiagnosticLogger.Instance.Log("FINS-WRITE", $"OK D{address}");
    }

    public async Task WriteSingleCoilAsync(
        int slaveId, int address, bool value, CancellationToken ct = default)
    {
        string coilAddress;
        if (address >= 10000)
        {
            var word = (address - 10000) / 16;
            var bit = (address - 10000) % 16;
            coilAddress = $"W{word}.{bit}";
        }
        else
        {
            var (word, bit) = address switch
            {
                >= 400 and <= 415 => (400, address - 400),
                >= 100 and <= 115 => (100, address - 100),
                _ => (address / 16, address % 16)
            };
            coilAddress = $"CIO{word}.{bit}";
        }
        DiagnosticLogger.Instance.Log("FINS-WRITE", $"WriteCoil {coilAddress} = {value}");
        var result = await Task.Run(() => _net.Write(coilAddress, value), ct);
        if (!result.IsSuccess)
        {
            DiagnosticLogger.Instance.Log("FINS-WRITE", $"FAILED {coilAddress}: {result.Message}");
            throw new InvalidOperationException($"FINS write {coilAddress} failed: {result.Message}");
        }
        DiagnosticLogger.Instance.Log("FINS-WRITE", $"OK {coilAddress}");
    }

    public void Dispose()
    {
        DiagnosticLogger.Instance.Log("FINS", "Dispose called — closing connection");
        try { _net.ConnectClose(); } catch { }
        IsConnected = false;
    }
}

/// <summary>
/// Factory that always produces an OmronFinsTcpTransport regardless of slave config.
/// Use this factory in MainViewModel when communicating with Omron PLCs over FINS/TCP.
/// </summary>
public sealed class OmronTransportFactory : IModbusTransportFactory
{
    public IModbusTransport CreateTransport(SlaveDeviceConfig config)
        => new OmronFinsTcpTransport(config);
}
