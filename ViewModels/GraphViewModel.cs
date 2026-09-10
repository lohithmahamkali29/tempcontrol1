using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using TempControl.Views;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using TempControl.Services;

namespace TempControl.ViewModels;

public partial class GraphViewModel : ObservableObject
{
    public sealed record GraphStatsRow(string Description, DateTime Timestamp, double Value, double Min, double Max, double Average);
    public sealed record GraphCursorValue(double Value, DateTime Timestamp);

    public PlcDataStore DataStore { get; }
    private readonly DatabaseService _dbService;
    private readonly ObservableCollection<DateTimePoint> _zone1TemperaturePoints = [];
    private readonly ObservableCollection<DateTimePoint> _zone2TemperaturePoints = [];
    private readonly ObservableCollection<DateTimePoint> _jobPvPoints = [];
    public ISeries[] Series { get; }
    public ObservableCollection<GraphStatsRow> GraphStats { get; } = [];

    public Axis[] XAxes { get; }
    public Axis[] YAxes { get; }

    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private bool _isValuesPanelOpen;
    [ObservableProperty] private string _selectedZoom = "30m";
    [ObservableProperty] private double _scrollPosition;
    [ObservableProperty] private double _scrollMaximum = 1;
    [ObservableProperty] private double _scrollViewport = TimeSpan.FromMinutes(30).TotalSeconds;

    private string _selectedMode = "Live";
    private DateTime? _historyFromDate = DateTime.Today.AddMonths(-1);
    private string _historyFromTime = "00:00";
    private DateTime? _historyToDate = DateTime.Today;
    private string _historyToTime = DateTime.Now.ToString("HH:mm");
    private string _historyStatusMessage = string.Empty;

    public string SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (!SetProperty(ref _selectedMode, value))
                return;

            OnPropertyChanged(nameof(IsLiveMode));
            OnPropertyChanged(nameof(IsHistoryMode));

            if (value == "History")
            {
                LoadHistoryInternal();
                return;
            }

            LoadLiveMode();
        }
    }

    public DateTime? HistoryFromDate
    {
        get => _historyFromDate;
        set => SetProperty(ref _historyFromDate, value);
    }

    public string HistoryFromTime
    {
        get => _historyFromTime;
        set => SetProperty(ref _historyFromTime, value);
    }

    public DateTime? HistoryToDate
    {
        get => _historyToDate;
        set => SetProperty(ref _historyToDate, value);
    }

    public string HistoryToTime
    {
        get => _historyToTime;
        set => SetProperty(ref _historyToTime, value);
    }

    public string HistoryStatusMessage
    {
        get => _historyStatusMessage;
        set => SetProperty(ref _historyStatusMessage, value);
    }

    public bool IsLiveMode => SelectedMode == "Live";
    public bool IsHistoryMode => SelectedMode == "History";

    private readonly DispatcherTimer _liveTimer;

    private TimeSpan _visibleWindow = TimeSpan.FromMinutes(30);
    private DateTime _sessionStart;
    private DateTime _sessionEnd;
    private int _tickCount;

    public GraphViewModel(PlcDataStore dataStore, DatabaseService dbService)
    {
        DataStore = dataStore;
        _dbService = dbService;

        Series =
 [
     new LineSeries<DateTimePoint>
    {
        Name           = "Zone 1 Temperature",
        Values         = _zone1TemperaturePoints,
        GeometrySize   = 0,
        LineSmoothness = 1,
        Stroke         = new SolidColorPaint(SKColors.Red, 2),
        Fill           = null
    },

    new LineSeries<DateTimePoint>
    {
        Name           = "Zone 2 Temperature",
        Values         = _zone2TemperaturePoints,
        GeometrySize   = 0,
        LineSmoothness = 1,
        Stroke         = new SolidColorPaint(SKColors.ForestGreen, 2),
        Fill           = null
    },

    new LineSeries<DateTimePoint>
    {
        Name = "Job PV",
        Values = _jobPvPoints,
        GeometrySize   = 0,
        LineSmoothness = 1,
        Stroke         = new SolidColorPaint(SKColors.DodgerBlue, 2),
        Fill           = null
    }
 ];

        XAxes = CreateTimeAxes();
        YAxes = CreateTemperatureAxes();


        _liveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        _liveTimer.Tick += OnLiveTimerTick;
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    public void OnNavigatedTo()
    {
        if (IsHistoryMode)
        {
            LoadHistoryInternal();
            return;
        }

        LoadLiveMode();
    }

    public void OnNavigatedFrom() => _liveTimer.Stop();

    // ── Zoom preset command ───────────────────────────────────────────────────

    [RelayCommand]
    private void SetZoom(string zoom)
    {
        SelectedZoom = zoom;
        _visibleWindow = zoom switch
        {
            "5m" => TimeSpan.FromMinutes(5),
            "15m" => TimeSpan.FromMinutes(15),
            "30m" => TimeSpan.FromMinutes(30),
            "1h" => TimeSpan.FromHours(1),
            _ => TimeSpan.FromHours(24)
        };

        RefreshScrollState(snapToEnd: true);
    }

    [RelayCommand]
    private void SetMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode) || SelectedMode == mode)
            return;

        SelectedMode = mode;
    }

    [RelayCommand]
    private void ToggleValuesPanel()
    {
        IsValuesPanelOpen = !IsValuesPanelOpen;
    }

    public DateTime? UpdateCursor(DateTime timestamp)
    {
        var nearestTimestamp = _zone1TemperaturePoints
            .Concat(_zone2TemperaturePoints)
            .Concat(_jobPvPoints)
            .Select(point => point.DateTime)
            .OrderBy(point => Math.Abs((point - timestamp).Ticks))
            .FirstOrDefault();

        if (nearestTimestamp == default)
            return null;

        RefreshStats(nearestTimestamp);
        return nearestTimestamp;
    }

    public DateTime? UpdateCursorFromPosition(double position, double width)
    {
        var minLimit = XAxes[0].MinLimit;
        var maxLimit = XAxes[0].MaxLimit;

        if (width <= 0 || !minLimit.HasValue || !maxLimit.HasValue
            || !double.IsFinite(minLimit.Value) || !double.IsFinite(maxLimit.Value))
            return null;

        var ratio = Math.Clamp(position / width, 0, 1);
        var ticks = minLimit.Value + ((maxLimit.Value - minLimit.Value) * ratio);
        return UpdateCursor(new DateTime(Convert.ToInt64(ticks)));
    }

    public GraphCursorValue[] GetCursorValues(DateTime timestamp)
        =>
        [
            GetNearestCursorValue(_zone1TemperaturePoints, timestamp),
            GetNearestCursorValue(_zone2TemperaturePoints, timestamp),
            GetNearestCursorValue(_jobPvPoints, timestamp)
        ];

    private static GraphCursorValue GetNearestCursorValue(
        ObservableCollection<DateTimePoint> points,
        DateTime timestamp)
    {
        var point = points
            .OrderBy(candidate => Math.Abs((candidate.DateTime - timestamp).Ticks))
            .First();

        return new GraphCursorValue(point.Value ?? 0d, point.DateTime);
    }

    [RelayCommand]
    private void LoadHistory()
    {
        if (SelectedMode != "History")
        {
            SelectedMode = "History";
            return;
        }

        LoadHistoryInternal();
    }

    [RelayCommand]
    private void ExportPdf()
    {
        DateTime from;
        DateTime to;

        if (IsHistoryMode)
        {
            if (!TryGetHistoryRange(out from, out to, out var errorMessage))
            {
                HistoryStatusMessage = errorMessage;
                return;
            }
        }
        else
        {
            to = DateTime.Now;
            from = to.AddHours(-24);
        }

        var records = _dbService.QueryRangeForPdf(from, to);
        if (records.Count == 0)
        {
            HistoryStatusMessage = "No records found for the selected date and time range.";
            return;
        }

        var headingDialog = new ExportHeadingDialog
        {
            Owner = Application.Current?.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        if (headingDialog.ShowDialog() != true)
            return;

        var reportHeading = headingDialog.ReportHeading.Trim();
        if (string.IsNullOrWhiteSpace(reportHeading))
            reportHeading = "Temperature Trend Report";

        var dialog = new SaveFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            FileName = $"OvenTrend_{from:yyyyMMdd_HHmm}_to_{to:yyyyMMdd_HHmm}.pdf",
            AddExtension = true,
            DefaultExt = ".pdf"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            PdfTrendExporter.Export(dialog.FileName, records, from, to, reportHeading);
            HistoryStatusMessage = $"PDF exported to {dialog.FileName}";
        }
        catch (Exception ex)
        {
            HistoryStatusMessage = $"PDF export failed: {ex.Message}";
        }
    }

    // ── Live timer ────────────────────────────────────────────────────────────

    private void OnLiveTimerTick(object? sender, EventArgs e)
    {
        if (!IsLiveMode)
            return;

        var now = DateTime.Now;
        _tickCount++;

        // Add a chart point every 60 seconds
        if (_tickCount % 60 == 0)
        {
            _zone1TemperaturePoints.Add(
           new DateTimePoint(now, DataStore.Zone1Temperature));

            _zone2TemperaturePoints.Add(
                new DateTimePoint(now, DataStore.Zone2Temperature));

            _jobPvPoints.Add(
               new DateTimePoint(now, DataStore.Zone1Output));
            HasData = true;
        }

        var wasAtLiveEnd = ScrollPosition >= ScrollMaximum - 2;
        _sessionEnd = now;
        RefreshScrollState(snapToEnd: wasAtLiveEnd);
    }

    // ── Scrollbar → X axis window ─────────────────────────────────────────────

    /// <summary>
    /// Called by the source generator whenever ScrollPosition changes (user drag or timer).
    /// ScrollPosition = seconds from _sessionStart to the LEFT edge of the visible window.
    /// </summary>
    partial void OnScrollPositionChanged(double value)
    {
        ApplyAxisWindow(value);
    }

    // ── Axis factories ────────────────────────────────────────────────────────

    private Axis[] CreateTimeAxes() =>
    [
        new DateTimeAxis(TimeSpan.FromMinutes(5), FormatAxisLabel)
        {
            Name                 = "Date / Time",
            NamePaint            = new SolidColorPaint(SKColors.LightSteelBlue),
            LabelsPaint          = new SolidColorPaint(SKColors.LightSteelBlue),
            SeparatorsPaint      = new SolidColorPaint(SKColors.White.WithAlpha(40)),
            SubseparatorsPaint   = new SolidColorPaint(SKColors.White.WithAlpha(15)),
            SubseparatorsCount   = 4,
        }
    ];

    private static Axis[] CreateTemperatureAxes() =>
[
    new Axis
    {
        Name                 = "Temperature (°C)",
        NamePaint            = new SolidColorPaint(SKColors.LightSteelBlue),
        LabelsPaint          = new SolidColorPaint(SKColors.LightSteelBlue),
        SeparatorsPaint      = new SolidColorPaint(SKColors.White.WithAlpha(40)),
        SubseparatorsPaint   = new SolidColorPaint(SKColors.White.WithAlpha(15)),
        SubseparatorsCount   = 4,
        MinStep              = 50,
        MinLimit             = 0,
        MaxLimit             = 250
    }
];

    private string FormatAxisLabel(DateTime date)
        => IsHistoryMode || _visibleWindow >= TimeSpan.FromDays(1)
            ? date.ToString("dd MMM\nHH:mm")
            : date.ToString("HH:mm");

    private void LoadLiveMode()
    {
       
        

        _liveTimer.Stop();

        HistoryStatusMessage = string.Empty;

        var to = DateTime.Now;
        var from = to.AddHours(-24);
        var history = _dbService.QueryRangeWithJobPv(from, to);

        LoadPoints(history);

        _sessionStart = history.Count > 0
            ? history[0].Timestamp
            : from;

        _sessionEnd = to;
        _tickCount = 0;

        RefreshScrollState(snapToEnd: true);

        _liveTimer.Start();
    }

    private void LoadHistoryInternal()
    {
        _liveTimer.Stop();

        if (!TryGetHistoryRange(out var from, out var to, out var errorMessage))
        {
            HistoryStatusMessage = errorMessage;
            return;
        }

        HistoryStatusMessage = string.Empty;

        var history = _dbService.QueryRangeWithJobPv(from, to);
        LoadPoints(history);

        _sessionStart = from;
        _sessionEnd = to;

        if (history.Count == 0)
            HistoryStatusMessage = "No records found for the selected date and time range.";

        RefreshScrollState(snapToEnd: true);
    }

    private void LoadPoints(
     List<(DateTime Timestamp,
           double Zone1Pv,
           double Zone2Pv,
           double JobPv)> history)
    {
        _zone1TemperaturePoints.Clear();
        _zone2TemperaturePoints.Clear();
        _jobPvPoints.Clear();
        GraphStats.Clear();

        foreach (var (
            timestamp,
            zone1Pv,
            zone2Pv,
            jobPv) in history)
        {
            _zone1TemperaturePoints.Add(
                new DateTimePoint(timestamp, zone1Pv));

            _zone2TemperaturePoints.Add(
                new DateTimePoint(timestamp, zone2Pv));

            _jobPvPoints.Add(
                new DateTimePoint(timestamp, jobPv));
        }

        HasData = history.Count > 0;
        RefreshStats();
    }

    private void RefreshStats(DateTime? selectedTimestamp = null)
    {
        GraphStats.Clear();

        AddStatsRow("Zone 1 Temperature", _zone1TemperaturePoints, selectedTimestamp);
        AddStatsRow("Zone 2 Temperature", _zone2TemperaturePoints, selectedTimestamp);
        AddStatsRow("Job PV", _jobPvPoints, selectedTimestamp);
    }

    private void AddStatsRow(
        string description,
        ObservableCollection<DateTimePoint> points,
        DateTime? selectedTimestamp = null)
    {
        if (points.Count == 0)
            return;

        var values = points
            .Select(point => point.Value ?? 0d)
            .ToList();

        var selectedPoint = selectedTimestamp is null
            ? points[^1]
            : points
                .OrderBy(point => Math.Abs((point.DateTime - selectedTimestamp.Value).Ticks))
                .First();
        var selectedValue = selectedPoint.Value ?? 0d;
        var timestamp = selectedPoint.DateTime;

        GraphStats.Add(new GraphStatsRow(
            description,
            timestamp,
            selectedValue,
            values.Min(),
            values.Max(),
            values.Average()));
    }

    private bool TryGetHistoryRange(out DateTime from, out DateTime to, out string errorMessage)
    {
        from = default;
        to = default;
        errorMessage = string.Empty;

        if (HistoryFromDate is null || HistoryToDate is null)
        {
            errorMessage = "Select both start and end dates.";
            return false;
        }

        if (!TimeOnly.TryParseExact(HistoryFromTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromTime))
        {
            errorMessage = "Start time must be in HH:mm format.";
            return false;
        }

        if (!TimeOnly.TryParseExact(HistoryToTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var toTime))
        {
            errorMessage = "End time must be in HH:mm format.";
            return false;
        }

        from = HistoryFromDate.Value.Date.Add(fromTime.ToTimeSpan());
        to = HistoryToDate.Value.Date.Add(toTime.ToTimeSpan());

        if (from > to)
        {
            errorMessage = "The start date/time must be earlier than the end date/time.";
            return false;
        }

        return true;
    }

    private void RefreshScrollState(bool snapToEnd)
    {
        var totalSeconds = Math.Max(0, (_sessionEnd - _sessionStart).TotalSeconds);

        ScrollViewport = _visibleWindow.TotalSeconds;
        ScrollMaximum = Math.Max(0, totalSeconds - _visibleWindow.TotalSeconds);
        ScrollPosition = snapToEnd ? ScrollMaximum : Math.Min(ScrollPosition, ScrollMaximum);

        ApplyAxisWindow(ScrollPosition);
    }

    private void ApplyAxisWindow(double value)
    {
        var totalRange = _sessionEnd - _sessionStart;
        DateTime windowStart;
        DateTime windowEnd;

        if (totalRange <= TimeSpan.Zero)
        {
            windowStart = _sessionStart;
            windowEnd = _sessionStart.Add(_visibleWindow);
        }
        else if (totalRange <= _visibleWindow)
        {
            windowStart = _sessionStart;
            windowEnd = _sessionEnd;
        }
        else
        {
            var maxStart = _sessionEnd - _visibleWindow;
            windowStart = _sessionStart.AddSeconds(Math.Max(0, value));
            if (windowStart > maxStart)
                windowStart = maxStart;

            windowEnd = windowStart.Add(_visibleWindow);
        }

        XAxes[0].MinLimit = windowStart.Ticks;
        XAxes[0].MaxLimit = windowEnd.Ticks;
    }
}
