using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using TempControl.Models;
using TempControl.Services;
using TempControl.Views;

namespace TempControl.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    public PlcDataStore DataStore { get; }

    public ObservableCollection<AlarmHistoryItem> LatestAlarms { get; }

    public ObservableCollection<AlarmHistoryItem> ActiveAlarms { get; } = [];

    private readonly RunSessionService _runSession;
    private readonly AlarmHistoryService _alarmHistoryService;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _canRun = true;

    [ObservableProperty]
    private bool _canStop = false;

    [ObservableProperty]
    private bool _isAlarmSidebarOpen;

    [ObservableProperty]
    private bool _hasActiveAlarms;

    [ObservableProperty]
    private int _activeAlarmCount;

    public HomeViewModel(
        PlcDataStore dataStore,
        RunSessionService runSession,
        AlarmHistoryService alarmHistoryService)
    {
        DataStore = dataStore;
        _runSession = runSession;
        _alarmHistoryService = alarmHistoryService;

        LatestAlarms = _alarmHistoryService.AlarmItems;

        LatestAlarms.CollectionChanged +=
            OnLatestAlarmsCollectionChanged;

        foreach (var alarm in LatestAlarms)
            alarm.PropertyChanged += OnAlarmItemPropertyChanged;

        UpdateAlarmFlags();

        // Listen to PLC state changes.
        DataStore.PropertyChanged +=
            OnDataStorePropertyChanged;

        // IMPORTANT:
        // Listen to RunSessionService state changes too.
        //
        // Without this, HomeViewModel may not know that the
        // logging session has finished after the PLC cycle completes.
        _runSession.SessionStateChanged +=
            OnSessionStateChanged;

        TraceState("Constructor before SyncRunState");

        SyncRunState();
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        TraceState("RunAsync requested");

        var dialog = new RunDialogWindow
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true)
            return;

        DiagnosticLogger.Instance.Log(
            "HOME",
            $"Run session starting — folder: {dialog.FolderPath}, file: {dialog.FileName}, interval: {dialog.IntervalSeconds}s");

        var started = await _runSession.StartAsync(
            dialog.FileName,
            dialog.IntervalSeconds,
            dialog.FolderPath);

        if (started)
        {
            // OLD - kept for reference
            // CanStop = true;
            // IsRunning = true;
            //
            // UpdateCanRun();

            // MODIFIED
            // The PLC W44.0 bit remains the source of truth after the
            // session starts; do not infer Running from StartAsync().
            SyncRunState();

            TraceState("RunAsync started");
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        TraceState("StopAsync requested");

        await _runSession.StopAsync();

        SyncRunState();

        TraceState("StopAsync completed");

        DiagnosticLogger.Instance.Log(
            "HOME",
            "Stop command issued by operator");
    }

    [RelayCommand]
    private void ToggleAlarmSidebar()
    {
        IsAlarmSidebarOpen = !IsAlarmSidebarOpen;
    }

    [RelayCommand]
    private void ClearAlarms()
    {
        _alarmHistoryService.ResetAll();

        DiagnosticLogger.Instance.Log(
            "HOME",
            "Active alarms cleared from sidebar");
    }

    /// <summary>
    /// Called whenever PlcDataStore changes.
    /// </summary>
    private void OnDataStorePropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is
            nameof(PlcDataStore.IsProcessRunning) or
            nameof(PlcDataStore.IsManualMode))
        {
            TraceState(
                $"OnDataStorePropertyChanged: {e.PropertyName}");
        }

        if (e.PropertyName == nameof(PlcDataStore.IsProcessRunning))
        {
            SyncRunState();
            return;
        }

        if (e.PropertyName == nameof(PlcDataStore.IsManualMode))
        {
            SyncRunState();
        }
    }

    /// <summary>
    /// Called whenever RunSessionService changes its active state.
    ///
    /// This is the missing notification in the previous implementation.
    /// </summary>
    private void OnSessionStateChanged(
        object? sender,
        EventArgs e)
    {
        TraceState("OnSessionStateChanged");

        SyncRunState();
    }

    private void UpdateCanRun()
    {
        // OLD - kept for reference
        // Selector meaning in this project:
        //
        // DataStore.IsManualMode == true  => AUTO mode
        // DataStore.IsManualMode == false => MANUAL mode
        //
        // RUN is allowed only when:
        // - AUTO mode
        // - PLC process is not running
        // - no run session is active

        // CanRun =
        //     DataStore.IsManualMode &&
        //     !DataStore.IsProcessRunning &&
        //     !_runSession.IsActive;

        // MODIFIED
        var isAutoMode = DataStore.IsManualMode;
        var running = DataStore.IsProcessRunning; // W44.0

        CanRun = isAutoMode && !running;
        CanStop = isAutoMode;

        TraceState("UpdateCanRun");
    }

    private void SyncRunState()
    {
        TraceState("SyncRunState start");

        // OLD - kept for reference
        // var isAutoMode = DataStore.IsManualMode;
        // var running = DataStore.IsProcessRunning;
        //
        // IsRunning = running;
        //
        // if (!isAutoMode)
        // {
        //     // Existing project behavior:
        //     // MANUAL mode => RUN disabled
        //     // STOP enabled
        //     CanRun = false;
        //     CanStop = true;
        //
        //     TraceState("SyncRunState end - MANUAL mode");
        //
        //     return;
        // }
        //
        // // AUTO mode.
        // //
        // // STOP is enabled only while PLC says process is running.
        // CanStop =
        //     running ||
        //     _runSession.IsActive;
        //
        // CanRun =
        //     !running &&
        //     !_runSession.IsActive;
        //
        // TraceState("SyncRunState end");

        // MODIFIED
        var isAutoMode = DataStore.IsManualMode;
        var running = DataStore.IsProcessRunning; // W44.0

        // OLD - kept for reference
        // IsRunning = running;

        // MODIFIED
        // Keep the running animation alive while the existing run session
        // is active and W44.0 is still transitioning to ON.
        var displayedRunning = running || _runSession.IsActive;
        IsRunning = displayedRunning;

        if (!isAutoMode) // MANUAL MODE
        {
            CanRun = false;
            CanStop = false;

            TraceState("SyncRunState end - MANUAL mode");

            return;
        }

        // AUTO MODE
        // OLD - kept for reference
        // CanRun = !running;

        // MODIFIED
        // Keep the button state consistent with the Running indicator while
        // W44.0 feedback is transitioning after the run command.
        CanRun = !displayedRunning;
        CanStop = true;

        TraceState("SyncRunState end");
    }

    public void OnNavigatedTo()
    {
        TraceState(
            "OnNavigatedTo before SyncRunState");

        SyncRunState();
    }

    private void OnLatestAlarmsCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (AlarmHistoryItem alarm in e.OldItems)
            {
                alarm.PropertyChanged -=
                    OnAlarmItemPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (AlarmHistoryItem alarm in e.NewItems)
            {
                alarm.PropertyChanged +=
                    OnAlarmItemPropertyChanged;
            }
        }

        UpdateAlarmFlags();
    }

    private void OnAlarmItemPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName ==
            nameof(AlarmHistoryItem.IsActive))
        {
            UpdateAlarmFlags();
        }
    }

    private void UpdateAlarmFlags()
    {
        ActiveAlarms.Clear();

        foreach (var alarm in LatestAlarms.Where(a => a.IsActive))
        {
            ActiveAlarms.Add(alarm);
        }

        HasActiveAlarms = ActiveAlarms.Count > 0;
        ActiveAlarmCount = ActiveAlarms.Count;
    }

    private void TraceState(string source)
    {
        var isAutoMode = DataStore.IsManualMode;
        var isProcessRunning = DataStore.IsProcessRunning;
        var runSessionIsActive = _runSession.IsActive;

        // OLD - kept for reference
        // var expectedRunEnabled =
        //     isAutoMode &&
        //     !isProcessRunning &&
        //     !runSessionIsActive;

        // MODIFIED
        var expectedRunEnabled =
            isAutoMode &&
            !isProcessRunning;

        // OLD - kept for reference
        // var expectedStopEnabled =
        //     isAutoMode &&
        //     (isProcessRunning || runSessionIsActive);

        // MODIFIED
        var expectedStopEnabled =
            isAutoMode;

        var expectation =
            expectedRunEnabled
                ? "Expected UI: RUN enabled, STOP enabled"
                : expectedStopEnabled
                    ? "Expected UI: RUN disabled, STOP enabled"
                    : "Expected UI: RUN disabled, STOP disabled";

        var message =
            $"{source} | " +
            $"IsManualMode(10024/W1.8)={isAutoMode}, " +
            // OLD - kept for reference
            // $"IsProcessRunning(11760/W110.0)={isProcessRunning}, " +
            // MODIFIED
            $"IsProcessRunning(10704/W44.0)={isProcessRunning}, " +
            $"RunSessionIsActive={runSessionIsActive}, " +
            $"IsRunning={IsRunning}, " +
            $"CanRun={CanRun}, " +
            $"CanStop={CanStop} | " +
            $"{expectation}";

        Debug.WriteLine(
            $"[HomeViewModel] {message}");

        DiagnosticLogger.Instance.Log(
            "HOME-STATE",
            message);
    }
}