using System.Windows.Threading;
using TempControl.Models;
namespace TempControl.Services;

/// <summary>
/// Background service that:
///  1) Logs temperature data to SQLite continuously (always on, interval configurable).
///  2) Polls Modbus slave devices when a real IModbusTransport is available.
///
/// DB logging runs in its own task and is independent of Modbus connectivity.
/// Polling tasks set per-slave IsConnected / LastPollStatus.
/// Without a real transport, slaves are marked "No transport" (red).
/// </summary>
public sealed class ModbusPollingService : IDisposable
{
    private readonly PlcDataStore _dataStore;
    private readonly DatabaseService _dbService;
    private readonly Dispatcher _dispatcher;
    private CancellationTokenSource? _cts;
    private readonly List<Task> _tasks = [];
    public IModbusTransportFactory _transportFactory;

    // Shared transport reused by ManualControlService for writes — avoids opening a second connection
    private IModbusTransport? _activeTransport;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>
    /// Interval between database inserts. Always logging — the Menu page
    /// only changes this interval, it cannot stop logging.
    /// </summary>
    public TimeSpan DbLogInterval { get; set; } = TimeSpan.FromSeconds(60);

    public ModbusPollingService(PlcDataStore dataStore, DatabaseService dbService, Dispatcher dispatcher, IModbusTransportFactory transport)
    {
        _dataStore = dataStore;
        _dbService = dbService;
        _dispatcher = dispatcher;
        _transportFactory = transport;
    }

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        // DB logging — always runs, independent of Modbus connectivity
        _tasks.Add(Task.Run(() => DbLoggingLoopAsync(ct), ct));

        // Modbus polling — one task per connection group
        var groups = _dataStore.SlaveConfigs
            .GroupBy(s => s.ConnectionType switch
            {
                ModbusConnectionType.Tcp => $"TCP:{s.IpAddress}:{s.TcpPort}",
                ModbusConnectionType.OmronFinsTcp => $"OMRON:{s.IpAddress}:{s.TcpPort}",
                _ => $"RTU:{s.PortName}"
            });

        foreach (var group in groups)
        {
            var slaves = group.ToList();
            _tasks.Add(Task.Run(() => PollGroupAsync(slaves, ct), ct));
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { Task.WhenAll(_tasks).Wait(TimeSpan.FromSeconds(3)); } catch { /* shutdown */ }
        _tasks.Clear();
        _cts?.Dispose();
        _cts = null;
    }

    // ?? DB Logging (always on) ??????????????????????????????????????

    private async Task DbLoggingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                double z1 = 0, z2 = 0, z1SetPv = 0, z2SetPv = 0, z1Sv = 0, z2Sv = 0, z1JobPv = 0, z2JobPv = 0;
                _dispatcher.Invoke(() =>
                {
                    z1 = _dataStore.Zone1Temperature;
                    z2 = _dataStore.Zone2Temperature;
                    z1SetPv = _dataStore.Zone1Setpoint;
                    z2SetPv = _dataStore.Zone2Setpoint;
                    z1Sv = _dataStore.Zone1Output;
                    z2Sv = _dataStore.Zone2Output;
                    z1JobPv = _dataStore.Zone1Output;
                    z2JobPv = _dataStore.Zone2Output;
                });
                _dbService.InsertRecord(DateTime.Now, z1, z2, z1SetPv, z2SetPv, z1Sv, z2Sv, z1JobPv, z2JobPv);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Instance.Log("DB", $"Insert error: {ex.Message}");
            }

            await Task.Delay(DbLogInterval, ct);
        }
    }

    // ?? Modbus Polling ??????????????????????????????????????????????

    private async Task PollGroupAsync(List<SlaveDeviceConfig> slaves, CancellationToken ct)
    {
        using var transport = _transportFactory.CreateTransport(slaves[0]);
        var group = slaves[0].ConnectionType switch
        {
            ModbusConnectionType.Tcp => $"TCP {slaves[0].IpAddress}:{slaves[0].TcpPort}",
            ModbusConnectionType.OmronFinsTcp => $"OMRON {slaves[0].IpAddress}:{slaves[0].TcpPort}",
            _ => $"RTU {slaves[0].PortName}"
        };

        DiagnosticLogger.Instance.Log("POLL", $"Group [{group}] starting — {slaves.Count} slave(s)");

        while (!ct.IsCancellationRequested)
        {
            // ?? Connect phase ????????????????????????????????????????????
            int consecutiveFailures = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    DiagnosticLogger.Instance.Log("POLL", $"[{group}] ConnectAsync...");
                    await transport.ConnectAsync(ct);
                    DiagnosticLogger.Instance.Log("POLL", $"[{group}] Connect OK");
                    consecutiveFailures = 0;
                    _activeTransport = transport;
                    _dispatcher.Invoke(() =>
                    {
                        foreach (var s in slaves)
                            s.LastPollStatus = $"Connected - {DateTime.Now:HH:mm:ss}";
                    });
                    break;
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    consecutiveFailures++;

                    // After a sudden disconnect the PLC's FINS/TCP slot stays occupied for
                    // ~60-180 s. Rapid retries don't help — back off progressively so the
                    // PLC has time to release the stale slot before the next attempt.
                    // ?5 failures ? 5 s   |   6–10 failures ? 15 s   |   >10 failures ? 30 s
                    int retryDelaySec = consecutiveFailures switch
                    {
                        <= 5 => 5,
                        <= 10 => 15,
                        _ => 30,
                    };

                    bool connectionLimitHit = ex.Message.Contains("connection limit", StringComparison.OrdinalIgnoreCase)
                                          || ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase);

                    string statusMsg = connectionLimitHit
                        ? $"PLC slot busy (attempt {consecutiveFailures}) — retrying in {retryDelaySec}s…"
                        : $"Connect failed: {ex.Message}";

                    DiagnosticLogger.Instance.Log("POLL",
                        $"[{group}] Connect FAILED (#{consecutiveFailures}): {ex.Message} — retry in {retryDelaySec}s");

                    _dispatcher.Invoke(() =>
                    {
                        foreach (var s in slaves)
                        {
                            s.IsConnected = false;
                            s.LastPollStatus = statusMsg;
                        }
                    });
                    await Task.Delay(TimeSpan.FromSeconds(retryDelaySec), ct);
                }
            }

            // ?? Poll phase ???????????????????????????????????????????????
            bool reconnectNeeded = false;
            while (!ct.IsCancellationRequested && !reconnectNeeded)
            {
                foreach (var slave in slaves)
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        await PollSlaveAsync(slave, transport, ct);
                        _dispatcher.Invoke(() =>
                        {
                            slave.IsConnected = true;
                            slave.LastPollStatus = $"OK - {DateTime.Now:HH:mm:ss}";
                        });
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        DiagnosticLogger.Instance.Log("POLL", $"Slave {slave.SlaveId} ({slave.Name}) poll error: {ex.Message}");
                        _dispatcher.Invoke(() =>
                        {
                            slave.IsConnected = false;
                            slave.LastPollStatus = $"Error - {ex.Message}";
                        });
                        reconnectNeeded = true;
                        break;
                    }
                    if (slave.ConnectionType == ModbusConnectionType.Rtu)
                        await Task.Delay(50, ct);
                }

                if (!reconnectNeeded)
                {
                    var interval = slaves.Min(s => s.PollIntervalMs);
                    await Task.Delay(interval, ct);
                }
            }

            // ?? Disconnect before retrying to free PLC connection slot ???
            if (reconnectNeeded)
            {
                DiagnosticLogger.Instance.Log("POLL", $"[{group}] Poll error — disconnecting before reconnect");
                // Null the transport under the write lock so any in-flight write either
                // completes before we disconnect, or sees null and returns false cleanly.
                // The live socket is only closed after the lock is released.
                await _writeLock.WaitAsync(ct);
                _activeTransport = null;
                _writeLock.Release();
                await transport.DisconnectAsync();
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }

    /// <summary>
    /// Reads every register and coil defined in the slave config via a real transport,
    /// then dispatches all values to PlcDataStore in a single UI-thread call.
    /// Acquires _writeLock so reads and writes are never concurrent on the same socket.
    /// </summary>
    private async Task PollSlaveAsync(SlaveDeviceConfig slave, IModbusTransport transport, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            var registerValues = new List<(int Address, ushort Value)>(slave.HoldingRegisters.Count);
            foreach (var reg in slave.HoldingRegisters)
            {
                if (slave.SlaveId == 4 && _dataStore.IsEnergyRegisterAddress(reg.Address))
                    continue;

                var result = await transport.ReadHoldingRegistersAsync(slave.SlaveId, reg.Address, 1, ct);
                registerValues.Add((reg.Address, result[0]));
            }

            var energyRegisterValues = await PollEnergyRegistersAsync(slave, transport, ct);

            var coilValues = new List<(int Address, bool Value)>(slave.Coils.Count);
            foreach (var coil in slave.Coils)
            {
                var result = await transport.ReadCoilsAsync(slave.SlaveId, coil.Address, 1, ct);
                var value = result[0];
                coilValues.Add((coil.Address, value));

                if (slave.SlaveId == 4 && (coil.Address == 11760 || coil.Address == 10024))
                {
                    var label = coil.Address == 11760 ? "W110.0 ProcessRunning" : "W1.8 Manual/Auto";
                    DiagnosticLogger.Instance.Log("POLL-COIL", $"Read coil {coil.Address} ({label}) = {value}");
                }
            }

            if (registerValues.Count > 0 || coilValues.Count > 0)
            {
                _dispatcher.Invoke(() =>
                {
                    foreach (var (address, value) in registerValues)
                        _dataStore.UpdateRegisterValue(slave.SlaveId, address, value);

                    foreach (var (address, value) in energyRegisterValues)
                        _dataStore.UpdateEnergyRegisterValue(address, value);

                    foreach (var (address, value) in coilValues)
                    {
                        if (slave.SlaveId == 4 && (address == 11760 || address == 10024))
                        {
                            var label = address == 11760 ? "W110.0 ProcessRunning" : "W1.8 Manual/Auto";
                            DiagnosticLogger.Instance.Log("POLL-COIL", $"Dispatch coil {address} ({label}) = {value}");
                        }

                        _dataStore.UpdateCoilValue(slave.SlaveId, address, value);
                    }
                });
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<List<(int Address, ushort Value)>> PollEnergyRegistersAsync(
        SlaveDeviceConfig slave,
        IModbusTransport transport,
        CancellationToken ct)
    {
        var energyRegisterValues = new List<(int Address, ushort Value)>();
        if (slave.SlaveId != 4)
            return energyRegisterValues;

        var energyAddresses = slave.HoldingRegisters
            .Select(r => r.Address)
            .Where(_dataStore.IsEnergyRegisterAddress)
            .Distinct()
            .OrderBy(a => a)
            .ToList();

        if (energyAddresses.Count == 0)
            return energyRegisterValues;

        foreach (var block in BuildRegisterBlocks(energyAddresses))
        {
            var result = await transport.ReadHoldingRegistersAsync(slave.SlaveId, block.StartAddress, block.Count, ct);
            for (int i = 0; i < result.Length; i++)
                energyRegisterValues.Add((block.StartAddress + i, result[i]));
        }

        return energyRegisterValues;
    }

    private static List<(int StartAddress, int Count)> BuildRegisterBlocks(List<int> addresses)
    {
        var blocks = new List<(int StartAddress, int Count)>();
        if (addresses.Count == 0)
            return blocks;

        int start = addresses[0];
        int previous = addresses[0];

        for (int i = 1; i < addresses.Count; i++)
        {
            int current = addresses[i];
            if (current == previous + 1)
            {
                previous = current;
                continue;
            }

            blocks.Add((start, previous - start + 1));
            start = previous = current;
        }

        blocks.Add((start, previous - start + 1));
        return blocks;
    }

    /// <summary>
    /// Writes a single holding register using the active polling transport.
    /// Returns false if not connected or write fails.
    /// </summary>
    public async Task<bool> WriteRegisterAsync(int slaveId, int address, ushort value)
    {
        await _writeLock.WaitAsync();
        try
        {
            var transport = _activeTransport;
            if (transport is null)
            {
                DiagnosticLogger.Instance.Log("WRITE", $"WriteRegister D{address} skipped — no active transport");
                return false;
            }
            await transport.WriteSingleRegisterAsync(slaveId, address, value);
            DiagnosticLogger.Instance.Log("WRITE", $"WriteRegister D{address} = {value} OK");
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Instance.Log("WRITE", $"WriteRegister D{address} FAILED: {ex.Message}");
            return false;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Writes a single coil using the active polling transport.
    /// Returns false if not connected or write fails.
    /// </summary>
    public async Task<bool> WriteCoilAsync(int slaveId, int address, bool value)
    {
        await _writeLock.WaitAsync();
        try
        {
            var transport = _activeTransport;
            if (transport is null)
            {
                DiagnosticLogger.Instance.Log("WRITE", $"WriteCoil {address} skipped — no active transport");
                return false;
            }
            await transport.WriteSingleCoilAsync(slaveId, address, value);
            DiagnosticLogger.Instance.Log("WRITE", $"WriteCoil {address} = {value} OK");
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Instance.Log("WRITE", $"WriteCoil {address} FAILED: {ex.Message}");
            return false;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
