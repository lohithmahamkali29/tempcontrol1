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
    public  IModbusTransportFactory _transportFactory;   

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
                ModbusConnectionType.Tcp          => $"TCP:{s.IpAddress}:{s.TcpPort}",
                ModbusConnectionType.OmronFinsTcp => $"OMRON:{s.IpAddress}:{s.TcpPort}",
                _                                 => $"RTU:{s.PortName}"
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
                double z1 = 0, z2 = 0, z1Job = 0, z2Job = 0, z1Sv = 0, z2Sv = 0;
                _dispatcher.Invoke(() =>
                {
                    z1    = _dataStore.Zone1Temperature;
                    z2    = _dataStore.Zone2Temperature;
                    z1Job = _dataStore.Zone1Output;
                    z2Job = _dataStore.Zone2Output;
                    z1Sv  = _dataStore.Zone1Setpoint;
                    z2Sv  = _dataStore.Zone2Setpoint;
                });
                _dbService.InsertRecord(DateTime.Now, z1, z2, z1Job, z2Job, z1Sv, z2Sv);
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
            ModbusConnectionType.Tcp          => $"TCP {slaves[0].IpAddress}:{slaves[0].TcpPort}",
            ModbusConnectionType.OmronFinsTcp => $"OMRON {slaves[0].IpAddress}:{slaves[0].TcpPort}",
            _                                 => $"RTU {slaves[0].PortName}"
        };

        DiagnosticLogger.Instance.Log("POLL", $"Group [{group}] starting — {slaves.Count} slave(s)");

        try
        {
            // Keep trying to connect until successful or cancelled
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    DiagnosticLogger.Instance.Log("POLL", $"[{group}] ConnectAsync...");
                    await transport.ConnectAsync(ct);
                    DiagnosticLogger.Instance.Log("POLL", $"[{group}] Connect OK");
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
                    DiagnosticLogger.Instance.Log("POLL", $"[{group}] Connect FAILED: {ex.Message} — retry in 5s");
                    _dispatcher.Invoke(() =>
                    {
                        foreach (var s in slaves)
                        {
                            s.IsConnected = false;
                            s.LastPollStatus = $"Connect failed: {ex.Message}";
                        }
                    });
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }
            }

            while (!ct.IsCancellationRequested)
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
                    }
                    if (slave.ConnectionType == ModbusConnectionType.Rtu)
                        await Task.Delay(50, ct);
                }
                var interval = slaves.Min(s => s.PollIntervalMs);
                await Task.Delay(interval, ct);
            }
        }
        finally
        {
            // Ensure connection is explicitly closed before Dispose
            try
            {
                await transport.DisconnectAsync();
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Instance.Log("POLL", $"[{group}] Error during disconnect: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Reads every register and coil defined in the slave config via a real transport,
    /// then dispatches all values to PlcDataStore in a single UI-thread call.
    /// </summary>
    private async Task PollSlaveAsync(SlaveDeviceConfig slave, IModbusTransport transport, CancellationToken ct)
    {
        var registerValues = new List<(int Address, ushort Value)>(slave.HoldingRegisters.Count);
        foreach (var reg in slave.HoldingRegisters)
        {
            var result = await transport.ReadHoldingRegistersAsync(slave.SlaveId, reg.Address, 1, ct);


            registerValues.Add((reg.Address, result[0]));
        }

        var coilValues = new List<(int Address, bool Value)>(slave.Coils.Count);
        foreach (var coil in slave.Coils)
        {
            var result = await transport.ReadCoilsAsync(slave.SlaveId, coil.Address, 1, ct);
            coilValues.Add((coil.Address, result[0]));
        }

        if (registerValues.Count > 0 || coilValues.Count > 0)
        {
            _dispatcher.Invoke(() =>
            {
                foreach (var (address, value) in registerValues)
                    _dataStore.UpdateRegisterValue(slave.SlaveId, address, value);

                foreach (var (address, value) in coilValues)
                    _dataStore.UpdateCoilValue(slave.SlaveId, address, value);
            });
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
