using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Threading;
using TempControl.Models;

namespace TempControl.Services;

public sealed class AlarmHistoryService
{
    private sealed record AlarmDescriptor(int Address, string DisplayName, bool AlarmWhenOn);

    private static readonly AlarmDescriptor[] AlarmDescriptors =
    [
        new(10000,  "Single Phase Preventer", false),
        new(10001,  "Emergency Switch", false),
        new(10002,  "Door Limit Switch Close", false),
        new(10003,  "Electrical Blower Motor 1", false),
        new(10004,  "Electrical Blower Motor 2", false),
        new(10010, "Zone 1 Temp Safety", true),
        new(10011, "Zone 2 Temp Safety", true),
        new(10016, "Exhaust Blower Motor Cont ON", false),
        new(10019, "Exhaust Blower Motor", false),
        
    ];

    private readonly Dictionary<int, AlarmDescriptor> _descriptorByAddress = AlarmDescriptors.ToDictionary(x => x.Address);
    private readonly Dictionary<int, AlarmHistoryItem> _activeAlarms = [];
    private readonly DispatcherTimer _durationTimer;

    public ObservableCollection<AlarmHistoryItem> AlarmItems { get; } = [];

    public AlarmHistoryService(PlcDataStore dataStore, Dispatcher dispatcher)
    {
        foreach (var point in dataStore.IoPoints.Where(p => _descriptorByAddress.ContainsKey(p.Address)))
        {
            point.PropertyChanged += OnTrackedPointPropertyChanged;
            EvaluatePoint(point, initializeOnly: true);
        }

        _durationTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _durationTimer.Tick += (_, _) => RefreshActiveDurations();
        _durationTimer.Start();
    }

    public bool HasAlarms => AlarmItems.Count > 0;

    public void AcknowledgeAll()
    {
        foreach (var alarm in AlarmItems)
            alarm.IsAcknowledged = true;
    }

    public void Acknowledge(AlarmHistoryItem? alarm)
    {
        if (alarm is null)
            return;

        alarm.IsAcknowledged = true;
    }

    public void ResetAll()
    {
        _activeAlarms.Clear();
        AlarmItems.Clear();
    }

    private void OnTrackedPointPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PlcIoPoint.BoolValue) || sender is not PlcIoPoint point)
            return;

        EvaluatePoint(point, initializeOnly: false);
    }

    private void EvaluatePoint(PlcIoPoint point, bool initializeOnly)
    {
        var descriptor = _descriptorByAddress[point.Address];
        var isAlarmActive = descriptor.AlarmWhenOn ? point.BoolValue : !point.BoolValue;
        point.IsAlarm = isAlarmActive;

        if (isAlarmActive)
        {
            if (_activeAlarms.TryGetValue(point.Address, out var existingAlarm))
            {
                existingAlarm.Condition = point.BoolValue ? "ON" : "OFF";
                UpdateDuration(existingAlarm);
                return;
            }

            var alarm = new AlarmHistoryItem
            {
                AlarmDescription = descriptor.DisplayName,
                TimeOn = DateTime.Now,
                Condition = point.BoolValue ? "ON" : "OFF",
                IsAcknowledged = false,
                IsActive = true,
            };
            UpdateDuration(alarm);
            AlarmItems.Insert(0, alarm);
            _activeAlarms[point.Address] = alarm;
            return;
        }

        if (_activeAlarms.TryGetValue(point.Address, out var activeAlarm))
        {
            activeAlarm.Condition = point.BoolValue ? "ON" : "OFF";
            activeAlarm.IsActive = false;
            UpdateDuration(activeAlarm);
            _activeAlarms.Remove(point.Address);
        }
        else if (!initializeOnly)
        {
            point.IsAlarm = false;
        }
    }

    private void RefreshActiveDurations()
    {
        foreach (var alarm in _activeAlarms.Values)
        {
            if (!alarm.IsAcknowledged)
                UpdateDuration(alarm);
        }
    }

    private static void UpdateDuration(AlarmHistoryItem alarm)
    {
        var duration = DateTime.Now - alarm.TimeOn;
        if (duration < TimeSpan.Zero)
            duration = TimeSpan.Zero;

        alarm.Duration = duration.ToString("hh\\:mm\\:ss");
    }
}
