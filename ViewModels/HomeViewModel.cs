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
            CanStop = true;
            IsRunning = true;

            UpdateCanRun();

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
        // Selector meaning in this project:
        //
        // DataStore.IsManualMode == true  => AUTO mode
        // DataStore.IsManualMode == false => MANUAL mode
        //
        // RUN is allowed only when:
        // - AUTO mode
        // - PLC process is not running
        // - no run session is active

        CanRun =
            DataStore.IsManualMode &&
            !DataStore.IsProcessRunning &&
            !_runSession.IsActive;

        TraceState("UpdateCanRun");
    }

    private void SyncRunState()
    {
        TraceState("SyncRunState start");

        var isAutoMode = DataStore.IsManualMode;
        var running = DataStore.IsProcessRunning;

        IsRunning = running;

        if (!isAutoMode)
        {
            // Existing project behavior:
            // MANUAL mode => RUN disabled
            // STOP enabled
            CanRun = false;
            CanStop = true;

            TraceState("SyncRunState end - MANUAL mode");

            return;
        }

        // AUTO mode.
        //
        // STOP is enabled only while PLC says process is running.
        CanStop = running;

        // RUN is enabled only when:
        // - PLC is not running
        // - RunSessionService is not active
        CanRun =
            !running &&
            !_runSession.IsActive;

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

        var expectedRunEnabled =
            isAutoMode &&
            !isProcessRunning &&
            !runSessionIsActive;

        var expectedStopEnabled =
            isAutoMode &&
            isProcessRunning;

        var expectation =
            expectedRunEnabled
                ? "Expected UI: RUN enabled, STOP disabled"
                : expectedStopEnabled
                    ? "Expected UI: RUN disabled, STOP enabled"
                    : "Expected UI: RUN disabled, STOP disabled";

        var message =
            $"{source} | " +
            $"IsManualMode(10024/W1.8)={isAutoMode}, " +
            $"IsProcessRunning(11760/W110.0)={isProcessRunning}, " +
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