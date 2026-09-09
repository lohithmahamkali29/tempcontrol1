using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TempControl.Services;

namespace TempControl.ViewModels;

public partial class MenuViewModel : ObservableObject
{
    public PlcDataStore DataStore { get; }
    private readonly DatabaseService _dbService;
    private readonly ModbusPollingService _pollingService;

    [ObservableProperty]
    private string _logFilePath = @"C:\OvenLogs";

    [ObservableProperty]
    private int _logIntervalSeconds = 60;

    [ObservableProperty]
    private DateTime _reportFromDate = DateTime.Today.AddDays(-7);

    [ObservableProperty]
    private DateTime _reportToDate = DateTime.Today;

    [ObservableProperty]
    private string _selectedReportZone = "Zone 1";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public string[] ReportZoneOptions { get; } = ["Zone 1", "Zone 2"];

    public MenuViewModel(PlcDataStore dataStore, DatabaseService dbService, ModbusPollingService pollingService)
    {
        DataStore = dataStore;
        _dbService = dbService;
        _pollingService = pollingService;

        LogIntervalSeconds = (int)_pollingService.DbLogInterval.TotalSeconds;
    }

    partial void OnLogIntervalSecondsChanged(int value)
    {
        if (value > 0)
        {
            _pollingService.DbLogInterval = TimeSpan.FromSeconds(value);
            StatusMessage = $"Log interval updated to {value}s.";
        }
    }

    [RelayCommand]
    private void DownloadReport()
    {
        try
        {
            var dir = LogFilePath;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var zoneToken = _selectedReportZone.Replace(" ", string.Empty);
            var fileName = $"OvenReport_{zoneToken}_{ReportFromDate:yyyyMMdd}_to_{ReportToDate:yyyyMMdd}.csv";
            var fullPath = Path.Combine(dir, fileName);

            var toEnd = ReportToDate.Date.AddDays(1).AddTicks(-1);
            var result = _dbService.ExportToCsv(fullPath, ReportFromDate.Date, toEnd, _selectedReportZone);
            StatusMessage = result;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }
}
