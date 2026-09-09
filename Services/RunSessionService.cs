using System.IO;
using System.ComponentModel;
using TempControl.Models;

namespace TempControl.Services;

/// <summary>
/// Manages a single run session:
/// - CSV logging at a configurable interval
/// - SQLite logging
/// - Writing W40.4 = 1 (run) / 0 (stop) to the PLC
/// - Automatically finishing the session when the PLC process completes
///
/// Coil writes go through the shared ModbusPollingService transport so
/// no second TCP connection to the PLC is opened.
/// </summary>
public sealed class RunSessionService : IDisposable
{
    private readonly PlcDataStore _dataStore;
    private readonly DatabaseService _dbService;
    private readonly ModbusPollingService _pollingService;

    private System.Timers.Timer? _timer;
    private string _csvPath = "";
    private int _rowNumber;
    private readonly List<string> _pendingRows = [];

    public bool IsActive { get; private set; }

    /// <summary>
    /// Raised whenever the run session changes between active/inactive.
    /// HomeViewModel uses this to refresh RUN/STOP button states.
    /// </summary>
    public event EventHandler? SessionStateChanged;

    public RunSessionService(
        PlcDataStore dataStore,
        DatabaseService dbService,
        ModbusPollingService pollingService)
    {
        _dataStore = dataStore;
        _dbService = dbService;
        _pollingService = pollingService;

        _dataStore.PropertyChanged += OnDataStorePropertyChanged;

        // If the application starts while the PLC is already running,
        // consider the session active.
        if (_dataStore.IsProcessRunning)
        {
            IsActive = true;
        }
    }

    private async void OnDataStorePropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PlcDataStore.IsProcessRunning))
            return;

        // PLC process has started/running.
        if (_dataStore.IsProcessRunning)
        {
            if (!IsActive)
            {
                IsActive = true;
                SessionStateChanged?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        // PLC process has stopped/completed.
        //
        // If we have an active logging session, this means the PLC cycle
        // has finished without the operator pressing STOP.
        if (IsActive)
        {
            await CompleteSessionAsync();
        }
    }

    public async Task<bool> StartAsync(
        string fileName,
        int intervalSeconds,
        string folderPath)
    {
        if (IsActive)
            return true;

        // Validate / create the save folder up-front so we fail early.
        var dir = string.IsNullOrWhiteSpace(folderPath)
            ? @"C:\ovencycle_runs"
            : folderPath;

        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Instance.Log(
                "RUN",
                $"Failed to create save folder: {ex.Message}");

            return false;
        }

        // Store the target CSV path.
        // File is written only when cycle stops/completes.
        var safeName = fileName.EndsWith(
            ".csv",
            StringComparison.OrdinalIgnoreCase)
            ? fileName
            : fileName + ".csv";

        _csvPath = Path.Combine(dir, safeName);
        _rowNumber = 0;
        _pendingRows.Clear();

        // Send run pulse: W40.4 = 1.
        // Log warning if PLC is offline but continue session logging.
        var ok = await _pollingService.WriteCoilAsync(
            4,
            10644,
            true);

        if (!ok)
        {
            DiagnosticLogger.Instance.Log(
                "RUN",
                "W40.4 write on start failed — PLC not connected, session logging will continue");
        }
        else
        {
            DiagnosticLogger.Instance.Log(
                "RUN",
                "W40.4 = 1 — run started");
        }

        // Start logging timer.
        _timer = new System.Timers.Timer(intervalSeconds * 1000.0)
        {
            AutoReset = true
        };

        _timer.Elapsed += OnTimerElapsed;
        _timer.Start();

        IsActive = true;

        // Notify HomeViewModel immediately.
        SessionStateChanged?.Invoke(this, EventArgs.Empty);

        DiagnosticLogger.Instance.Log(
            "RUN",
            $"Session started — target file: {_csvPath}, interval: {intervalSeconds}s");

        // Take the first reading immediately so data exists even if
        // STOP is pressed before the first timer tick.
        OnTimerElapsed(null, null!);

        return true;
    }

    private void OnTimerElapsed(
        object? sender,
        System.Timers.ElapsedEventArgs e)
    {
        // Don't log new data after the session has completed.
        if (!IsActive)
            return;

        var ts = DateTime.Now;

        var z1 = _dataStore.Zone1Temperature;
        var z2 = _dataStore.Zone2Temperature;

        var z1SetPv = _dataStore.Zone1Setpoint;
        var z2SetPv = _dataStore.Zone2Setpoint;

        var z1JobPv = _dataStore.Zone1Output;
        var z2JobPv = _dataStore.Zone2Output;

        // Write to SQLite immediately so data is never lost mid-cycle.
        try
        {
            _dbService.InsertRecord(
                ts,
                z1,
                z2,
                z1SetPv,
                z2SetPv,
                0,
                0,
                z1JobPv,
                z2JobPv);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Instance.Log(
                "RUN",
                $"DB write error: {ex.Message}");
        }

        // Accumulate row in memory.
        // CSV is written at the end of the cycle.
        _pendingRows.Add(
            $"{++_rowNumber}," +
            $"{ts:yyyy-MM-dd HH:mm:ss}," +
            $"{z1:F1}," +
            $"{z2:F1}," +
            $"{z1SetPv:F1}," +
            $"{z1JobPv:F1}," +
            $"{z2JobPv:F1}");

        DiagnosticLogger.Instance.Log(
            "RUN",
            $"Logged — Z1={z1:F1}°C  Z2={z2:F1}°C");
    }

    /// <summary>
    /// Called when the operator presses STOP.
    /// Sends W40.4 = 0 and then closes the session.
    /// </summary>
    public async Task StopAsync()
    {
        if (!IsActive)
            return;

        // Stop logging immediately.
        StopTimer();

        // Send stop pulse: W40.4 = 0.
        var ok = await _pollingService.WriteCoilAsync(
            4,
            10644,
            false);

        if (ok)
        {
            DiagnosticLogger.Instance.Log(
                "RUN",
                "W40.4 = 0 — run stopped");
        }
        else
        {
            DiagnosticLogger.Instance.Log(
                "RUN",
                "W40.4 write on stop failed — PLC may be disconnected");
        }

        // Finish the session and write CSV.
        await FinishSessionAsync("Cycle stopped by operator");
    }

    /// <summary>
    /// Called automatically when PlcDataStore reports that
    /// IsProcessRunning changed from true to false.
    ///
    /// The PLC has already stopped, so we don't send another W40.4 = 0.
    /// </summary>
    private async Task CompleteSessionAsync()
    {
        if (!IsActive)
            return;

        StopTimer();

        await FinishSessionAsync("Cycle completed automatically");
    }

    /// <summary>
    /// Finalizes the current session:
    /// - marks session inactive
    /// - notifies HomeViewModel
    /// - writes the complete CSV report
    /// - clears pending rows
    /// </summary>
    private async Task FinishSessionAsync(string reason)
    {
        // Prevent duplicate completion.
        if (!IsActive)
            return;

        IsActive = false;

        // This is the important notification.
        // HomeViewModel will recalculate:
        // RUN  = enabled
        // STOP = disabled
        SessionStateChanged?.Invoke(this, EventArgs.Empty);

        DiagnosticLogger.Instance.Log(
            "RUN",
            reason);

        // Write complete cycle report CSV.
        if (_pendingRows.Count > 0)
        {
            try
            {
                var lines = new System.Text.StringBuilder();

                lines.AppendLine(
                    "Sr.No,Timestamp,Zone1Temp (C),Zone2Temp (C),SetPv,Zone1JobPv (C),Zone2JobPv (C)");

                foreach (var row in _pendingRows)
                    lines.AppendLine(row);

                await File.WriteAllTextAsync(
                    _csvPath,
                    lines.ToString());

                DiagnosticLogger.Instance.Log(
                    "RUN",
                    $"Cycle report saved — {_pendingRows.Count} records → {_csvPath}");
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Instance.Log(
                    "RUN",
                    $"Failed to write cycle report: {ex.Message}");
            }
        }
        else
        {
            DiagnosticLogger.Instance.Log(
                "RUN",
                "Cycle completed — no data recorded, CSV not created");
        }

        _pendingRows.Clear();
    }

    private void StopTimer()
    {
        if (_timer is null)
            return;

        _timer.Stop();
        _timer.Dispose();
        _timer = null;
    }

    public void Dispose()
    {
        _dataStore.PropertyChanged -= OnDataStorePropertyChanged;

        StopTimer();
    }
}