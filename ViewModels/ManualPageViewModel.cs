using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TempControl.Services;

namespace TempControl.ViewModels;

public partial class ManualPageViewModel : ObservableObject
{
    private static readonly HashSet<string> TrackedProperties =
    [
        nameof(Zone1SetPointValue),
        nameof(Zone2SetPointValue),
        nameof(Zone1SafetyTemperatureValue),
        nameof(Zone2SafetyTemperatureValue),
        nameof(IsEditMode)
    ];

    private readonly ManualControlService _manualControlService;
    private readonly DispatcherTimer _refreshTimer;
    private bool _isSyncingFromDataStore;

    public PlcDataStore DataStore { get; }

    [ObservableProperty] private double _zone1SetPointValue;
    [ObservableProperty] private double _zone2SetPointValue;
    [ObservableProperty] private double _zone1SafetyTemperatureValue;
    [ObservableProperty] private double _zone2SafetyTemperatureValue;
    [ObservableProperty] private bool _isEditMode;

    public bool IsNotEditMode => !IsEditMode;

    partial void OnIsEditModeChanged(bool value) => OnPropertyChanged(nameof(IsNotEditMode));

    public bool Blower1Status => DataStore.Blower1ManualStatus;
    public bool Blower2Status => DataStore.Blower2ManualStatus;
    public bool Heater1Status => DataStore.Heater1ManualStatus;
    public bool Heater2Status => DataStore.Heater2ManualStatus;

    public ManualPageViewModel(PlcDataStore dataStore, ManualControlService manualControlService)
    {
        DataStore = dataStore;
        _manualControlService = manualControlService;
        PropertyChanged += OnViewModelPropertyChanged;

        SyncFromDataStore();
        DataStore.PropertyChanged += OnDataStorePropertyChanged;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _refreshTimer.Tick += (_, _) => { if (!IsEditMode) SyncFromDataStore(); };
        _refreshTimer.Start();
    }

    [RelayCommand]
    private void Edit()
    {
        Trace("Edit requested");

        // Selector meaning in this project:
        // DataStore.IsManualMode == true  => AUTO mode
        // DataStore.IsManualMode == false => MANUAL mode
        if (DataStore.IsManualMode)
        {
            MessageBox.Show(
                "System is in AUTO MODE. Switch to MANUAL MODE to edit values.",
                "Manual Edit Blocked",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // Snapshot current PLC values into editable fields
        _isSyncingFromDataStore = true;
        Zone1SetPointValue = DataStore.Zone1SetPointValueManual;
        Zone2SetPointValue = DataStore.Zone2Setpoint;
        Zone1SafetyTemperatureValue = DataStore.Zone1SafetyTemperature;
        Zone2SafetyTemperatureValue = DataStore.Zone2SafetyTemperature;
        _isSyncingFromDataStore = false;
        Trace($"Edit snapshot loaded from DataStore: Z1SP={Zone1SetPointValue}, Z2SP={Zone2SetPointValue}, Z1Safe={Zone1SafetyTemperatureValue}, Z2Safe={Zone2SafetyTemperatureValue}");

        IsEditMode = true;
    }

    [RelayCommand]
    private async Task UpdateAsync()
    {
        Trace($"UpdateAsync starting: Z1SP={Zone1SetPointValue}, Z2SP={Zone2SetPointValue}, Z1Safe={Zone1SafetyTemperatureValue}, Z2Safe={Zone2SafetyTemperatureValue}");

        var writes = new (int Address, double Value, string Label)[]
        {
            (325, Zone1SetPointValue,                "Zone 1 Setpoint"),
            (326, Zone2SetPointValue,                "Zone 2 Setpoint"),
            (327, Zone1SafetyTemperatureValue,       "Zone 1 Safety Temp"),
            (328, Zone2SafetyTemperatureValue,       "Zone 2 Safety Temp"),
        };

        var failed = new List<string>();
        foreach (var (address, value, label) in writes)
        {
            Trace($"Writing {label}: D{address} = {value}");
            if (!await _manualControlService.WriteRegisterAsync(address, value))
                failed.Add(label);
        }

        if (failed.Count > 0)
            MessageBox.Show(
                $"Failed to write: {string.Join(", ", failed)}. Check PLC connection.",
                "Write Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            PlcStateCache.Save(DataStore);

        Trace(failed.Count > 0
            ? $"UpdateAsync finished with failures: {string.Join(", ", failed)}"
            : "UpdateAsync finished successfully");

        IsEditMode = false;
    }

    [RelayCommand]
    private Task Blower1OnAsync() => WriteCoilDirectAsync(10640, true);

    [RelayCommand]
    private Task Blower1OffAsync() => WriteCoilDirectAsync(10640, false);

    [RelayCommand]
    private Task Blower2OnAsync() => WriteCoilDirectAsync(10641, true);

    [RelayCommand]
    private Task Blower2OffAsync() => WriteCoilDirectAsync(10641, false);

    [RelayCommand]
    private Task Heater1OnAsync() => WriteCoilDirectAsync(10642, true);

    [RelayCommand]
    private Task Heater1OffAsync() => WriteCoilDirectAsync(10642, false);

    [RelayCommand]
    private Task Heater2OnAsync() => WriteCoilDirectAsync(10643, true);

    [RelayCommand]
    private Task Heater2OffAsync() => WriteCoilDirectAsync(10643, false);

    private async Task WriteRegisterAsync(int address, double value, string propertyName)
    {
        var ok = await _manualControlService.WriteRegisterAsync(address, value);
        if (!ok)
        {
            RevertValue(propertyName);
            MessageBox.Show(
                $"Unable to write D{address}. Check PLC connection.",
                "Manual Write Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task WriteCoilDirectAsync(int address, bool value)
    {
        var ok = await _manualControlService.WriteCoilDirectAsync(address, value);
        if (!ok)
        {
            MessageBox.Show(
                "Unable to write manual command bit. Check PLC connection.",
                "Manual Command Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnDataStorePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlcDataStore.Zone1SetPointValueManual)
            or nameof(PlcDataStore.Zone2Setpoint)
            or nameof(PlcDataStore.Zone1SafetyTemperature)
            or nameof(PlcDataStore.Zone2SafetyTemperature))
        {
            Trace($"DataStore changed: {e.PropertyName} | Z1SP={DataStore.Zone1SetPointValueManual}, Z2SP={DataStore.Zone2Setpoint}, Z1Safe={DataStore.Zone1SafetyTemperature}, Z2Safe={DataStore.Zone2SafetyTemperature}");
        }

        switch (e.PropertyName)
        {
            case nameof(PlcDataStore.Zone1SetPointValueManual):
            case nameof(PlcDataStore.Zone2Setpoint):
            case nameof(PlcDataStore.Zone1SafetyTemperature):
            case nameof(PlcDataStore.Zone2SafetyTemperature):
                if (!IsEditMode)
                    SyncFromDataStore();
                break;
            case nameof(PlcDataStore.Blower1ManualStatus):
                OnPropertyChanged(nameof(Blower1Status));
                break;
            case nameof(PlcDataStore.Blower2ManualStatus):
                OnPropertyChanged(nameof(Blower2Status));
                break;
            case nameof(PlcDataStore.Heater1ManualStatus):
                OnPropertyChanged(nameof(Heater1Status));
                break;
            case nameof(PlcDataStore.Heater2ManualStatus):
                OnPropertyChanged(nameof(Heater2Status));
                break;
        }
    }

    /// <summary>
    /// Called each time the Manual page becomes visible so the fields
    /// always show the latest PLC values on entry, regardless of whether
    /// the polling service fired a PropertyChanged since last visit.
    /// </summary>
    public void OnNavigatedTo()
    {
        Trace("OnNavigatedTo called");
        SyncFromDataStore();
    }

    private void SyncFromDataStore()
    {
        Trace($"SyncFromDataStore called: Z1SP={DataStore.Zone1SetPointValueManual}, Z2SP={DataStore.Zone2Setpoint}, Z1Safe={DataStore.Zone1SafetyTemperature}, Z2Safe={DataStore.Zone2SafetyTemperature}");
        _isSyncingFromDataStore = true;
        Zone1SetPointValue = DataStore.Zone1SetPointValueManual;
        Zone2SetPointValue = DataStore.Zone2Setpoint;
        Zone1SafetyTemperatureValue = DataStore.Zone1SafetyTemperature;
        Zone2SafetyTemperatureValue = DataStore.Zone2SafetyTemperature;
        _isSyncingFromDataStore = false;
        Trace($"SyncFromDataStore finished: Z1SP={Zone1SetPointValue}, Z2SP={Zone2SetPointValue}, Z1Safe={Zone1SafetyTemperatureValue}, Z2Safe={Zone2SafetyTemperatureValue}");
    }

    private void RevertValue(string propertyName)
    {
        _isSyncingFromDataStore = true;

        switch (propertyName)
        {
            case nameof(Zone1SetPointValue):
                Zone1SetPointValue = DataStore.Zone1SetPointValueManual;
                break;
            case nameof(Zone2SetPointValue):
                Zone2SetPointValue = DataStore.Zone2Setpoint;
                break;
            case nameof(Zone1SafetyTemperatureValue):
                Zone1SafetyTemperatureValue = DataStore.Zone1SafetyTemperature;
                break;
            case nameof(Zone2SafetyTemperatureValue):
                Zone2SafetyTemperatureValue = DataStore.Zone2SafetyTemperature;
                break;
        }

        _isSyncingFromDataStore = false;
        Trace($"RevertValue executed for {propertyName}: Z1SP={Zone1SetPointValue}, Z2SP={Zone2SetPointValue}, Z1Safe={Zone1SafetyTemperatureValue}, Z2Safe={Zone2SafetyTemperatureValue}");
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null || !TrackedProperties.Contains(e.PropertyName))
            return;

        Trace($"ViewModel property changed: {e.PropertyName} | Z1SP={Zone1SetPointValue}, Z2SP={Zone2SetPointValue}, Z1Safe={Zone1SafetyTemperatureValue}, Z2Safe={Zone2SafetyTemperatureValue}, IsEditMode={IsEditMode}");
    }

    private static void Trace(string message)
    {
        Debug.WriteLine($"[ManualPageVM] {message}");
        DiagnosticLogger.Instance.Log("MANUAL-VM", message);
    }
}
