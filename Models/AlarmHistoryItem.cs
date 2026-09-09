using CommunityToolkit.Mvvm.ComponentModel;

namespace TempControl.Models;

public partial class AlarmHistoryItem : ObservableObject
{
    [ObservableProperty] private string _alarmDescription = string.Empty;
    [ObservableProperty] private DateTime _timeOn;
    [ObservableProperty] private string _duration = "00:00:00";
    [ObservableProperty] private string _condition = "OFF";
    [ObservableProperty] private bool _isAcknowledged;
    [ObservableProperty] private bool _isActive;

    public string Date => TimeOn == default ? "--" : TimeOn.ToString("dd-MMM-yyyy");

    public string TimeOnDisplay => TimeOn == default ? "--" : TimeOn.ToString("HH:mm:ss");

    public string Status => IsActive ? "ACTIVE" : IsAcknowledged ? "ACK'D" : "CLEARED";

    partial void OnTimeOnChanged(DateTime value)
    {
        OnPropertyChanged(nameof(Date));
        OnPropertyChanged(nameof(TimeOnDisplay));
    }

    partial void OnIsActiveChanged(bool value) => OnPropertyChanged(nameof(Status));
    partial void OnIsAcknowledgedChanged(bool value) => OnPropertyChanged(nameof(Status));
}
