using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HslCommunication.Core.IMessage;
using TempControl.Services;

namespace TempControl.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    public PlcDataStore DataStore { get; }
    public DatabaseService DbService { get; }
    public ModbusPollingService PollingService { get; }
    private readonly RunSessionService _runSession;
    private readonly AlarmHistoryService _alarmHistoryService;

    public HomeViewModel HomeVm { get; }
    public ManualPageViewModel ManualVm { get; }
    public SettingsViewModel SettingsVm { get; }
    public PlcIoViewModel PlcIoVm { get; }
    public GraphViewModel GraphVm { get; }
    public AlarmHistoryViewModel AlarmHistoryVm { get; }
    public MenuViewModel MenuVm { get; }
    public EnergyReadingsViewModel EnergyReadingsVm { get; }

    [ObservableProperty]
    private string _currentDate = DateTime.Now.ToString("dd-MMM-yyyy");

    [ObservableProperty]
    private string _currentTime = DateTime.Now.ToString("HH:mm:ss");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomeSelected))]
    [NotifyPropertyChangedFor(nameof(IsManualSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsSelected))]
    [NotifyPropertyChangedFor(nameof(IsPlcIoSelected))]
    [NotifyPropertyChangedFor(nameof(IsGraphSelected))]
    [NotifyPropertyChangedFor(nameof(IsAlarmHistorySelected))]
    [NotifyPropertyChangedFor(nameof(IsMenuSelected))]
    [NotifyPropertyChangedFor(nameof(IsEnergyReadingsSelected))]
    private ObservableObject _currentViewModel;

    public bool IsHomeSelected => CurrentViewModel == HomeVm;
    public bool IsManualSelected => CurrentViewModel == ManualVm;
    public bool IsSettingsSelected => CurrentViewModel == SettingsVm;
    public bool IsPlcIoSelected => CurrentViewModel == PlcIoVm;
    public bool IsGraphSelected => CurrentViewModel == GraphVm;
    public bool IsAlarmHistorySelected => CurrentViewModel == AlarmHistoryVm;
    public bool IsMenuSelected => CurrentViewModel == MenuVm;
    public bool IsEnergyReadingsSelected => CurrentViewModel == EnergyReadingsVm;
    public bool IsPlcConnected => DataStore.SlaveConfigs.FirstOrDefault(s => s.SlaveId == 4)?.IsConnected == true;
    public string PlcConnectionStatusText => IsPlcConnected ? "PLC Connected" : "PLC Disconnected";

    public MainViewModel()
    {
        DataStore = new PlcDataStore();

        // Restore last-known register values before any VM snapshots from the store
        PlcStateCache.Load(DataStore);

        foreach (var slaveConfig in DataStore.SlaveConfigs)
        {
            slaveConfig.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(Models.SlaveDeviceConfig.IsConnected))
                {
                    OnPropertyChanged(nameof(IsPlcConnected));
                    OnPropertyChanged(nameof(PlcConnectionStatusText));
                }
            };
        }

        // Initialize SQLite database
        DbService = new DatabaseService();
        DbService.Initialize();

        // Create transport factory and start background Modbus polling.
        // Set OfflineMode = true to run without a physical PLC (all reads return 0 / false).
        // Set OfflineMode = false when the PLC is reachable on the network.
        const bool OfflineMode = false;
        IModbusTransportFactory factory = OfflineMode
            ? new NullTransportFactory()
            : new OmronTransportFactory();
        DiagnosticLogger.Instance.Log("STARTUP", $"App started — transport: {(OfflineMode ? "OFFLINE (NullTransport)" : "OmronTransportFactory")}");
        PollingService = new ModbusPollingService(DataStore, DbService, Dispatcher.CurrentDispatcher, factory);
        PollingService.Start();
        _alarmHistoryService = new AlarmHistoryService(DataStore, Dispatcher.CurrentDispatcher);

        // Create view models (MenuVm needs PollingService for logging control)
        var runSession = new RunSessionService(DataStore, DbService, PollingService);
        var manualControlService = new ManualControlService(DataStore, PollingService);
        _runSession = runSession;
        HomeVm = new HomeViewModel(DataStore, runSession, _alarmHistoryService);
        ManualVm = new ManualPageViewModel(DataStore, manualControlService);
        SettingsVm = new SettingsViewModel(DataStore, manualControlService, DbService);
        PlcIoVm = new PlcIoViewModel(DataStore);
        GraphVm = new GraphViewModel(DataStore, DbService);
        AlarmHistoryVm = new AlarmHistoryViewModel(_alarmHistoryService);
        MenuVm = new MenuViewModel(DataStore, DbService, PollingService);
        EnergyReadingsVm = new EnergyReadingsViewModel(DataStore);
        _currentViewModel = HomeVm;

        // UI clock timer
        var clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        clockTimer.Tick += (_, _) =>
        {
            CurrentDate = DateTime.Now.ToString("dd-MMM-yyyy");
            CurrentTime = DateTime.Now.ToString("HH:mm:ss");
        };
        clockTimer.Start();
    }

    [RelayCommand]
    private void NavigateHome()
    {
        if (CurrentViewModel == GraphVm) GraphVm.OnNavigatedFrom();
        CurrentViewModel = HomeVm;
        HomeVm.OnNavigatedTo();
    }

    [RelayCommand]
    private void NavigateManual()
    {
        if (CurrentViewModel == GraphVm) GraphVm.OnNavigatedFrom();
        CurrentViewModel = ManualVm;
        ManualVm.IsEditMode = false;
        ManualVm.OnNavigatedTo();
    }

    [RelayCommand]
    private void NavigateSettings()
    {
        if (CurrentViewModel == GraphVm) GraphVm.OnNavigatedFrom();
        CurrentViewModel = SettingsVm;
        SettingsVm.IsEditMode = false;
        SettingsVm.OnNavigatedTo();
    }

    [RelayCommand]
    private void NavigatePlcIo()
    {
        if (CurrentViewModel == GraphVm) GraphVm.OnNavigatedFrom();
        CurrentViewModel = PlcIoVm;
    }

    [RelayCommand]
    private void NavigateGraph()
    {
        CurrentViewModel = GraphVm;
        GraphVm.OnNavigatedTo();
    }

    [RelayCommand]
    private void NavigateAlarmHistory()
    {
        if (CurrentViewModel == GraphVm) GraphVm.OnNavigatedFrom();
        CurrentViewModel = AlarmHistoryVm;
    }

    [RelayCommand]
    private void NavigateMenu()
    {
        if (CurrentViewModel == GraphVm) GraphVm.OnNavigatedFrom();
        CurrentViewModel = MenuVm;
    }

    [RelayCommand]
    private void NavigateEnergyReadings()
    {
        if (CurrentViewModel == GraphVm) GraphVm.OnNavigatedFrom();
        CurrentViewModel = EnergyReadingsVm;
    }

    public void Dispose()
    {
        PlcStateCache.Save(DataStore);
        _runSession.Dispose();
        PollingService.Dispose();
        DbService.Dispose();
    }
}
