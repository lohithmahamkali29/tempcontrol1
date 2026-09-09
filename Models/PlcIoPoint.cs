using CommunityToolkit.Mvvm.ComponentModel;

namespace TempControl.Models;

public enum IoType
{
    DigitalInput,
    DigitalOutput,
    AnalogInput,
    AnalogOutput
}

public partial class PlcIoPoint : ObservableObject
{
    public int SerialNumber { get; init; }
    public int SlaveId { get; init; }
    public int Address { get; init; }
    public string Name { get; init; } = string.Empty;
    public IoType Type { get; init; }
    public string Unit { get; init; } = string.Empty;

    [ObservableProperty]
    private double _value;

    [ObservableProperty]
    private bool _boolValue;

    [ObservableProperty]
    private bool _isAlarm;

    [ObservableProperty]
    private string _arrivalTime = "--:--:--";

    [ObservableProperty]
    private string _resolvedTime = "--:--:--";

    partial void OnBoolValueChanged(bool value)
    {
        if (value)
            ArrivalTime = DateTime.Now.ToString("HH:mm:ss");
        else
            ResolvedTime = DateTime.Now.ToString("HH:mm:ss");
    }

    public bool IsDigital => Type is IoType.DigitalInput or IoType.DigitalOutput;
    public bool IsAnalog => Type is IoType.AnalogInput or IoType.AnalogOutput;
    public bool IsInput => Type is IoType.DigitalInput or IoType.AnalogInput;
    public bool IsOutput => Type is IoType.DigitalOutput or IoType.AnalogOutput;
}
