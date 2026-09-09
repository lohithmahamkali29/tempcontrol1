using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TempControl.Models;
using TempControl.Services;

namespace TempControl.ViewModels;

public partial class AlarmHistoryViewModel : ObservableObject
{
    private readonly AlarmHistoryService _alarmHistoryService;

    public ObservableCollection<AlarmHistoryItem> AlarmItems => _alarmHistoryService.AlarmItems;

    [ObservableProperty] private string _statusMessage = "Live alarm tracking is active.";

    public AlarmHistoryViewModel(AlarmHistoryService alarmHistoryService)
    {
        _alarmHistoryService = alarmHistoryService;
    }

    [RelayCommand]
    private void AckAll()
    {
        _alarmHistoryService.AcknowledgeAll();
        StatusMessage = "All alarms acknowledged.";
    }

    [RelayCommand]
    private void AckAlarm(AlarmHistoryItem? alarm)
    {
        if (alarm is null)
            return;

        _alarmHistoryService.Acknowledge(alarm);
        StatusMessage = $"Acknowledged: {alarm.AlarmDescription}";
    }

    [RelayCommand]
    private void ResetAll()
    {
        _alarmHistoryService.ResetAll();
        StatusMessage = "Alarm history reset.";
    }
}
