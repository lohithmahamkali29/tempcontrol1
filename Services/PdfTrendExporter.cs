using System.Globalization;
using SkiaSharp;

namespace TempControl.Services;

public sealed record PdfTrendRecord(
    DateTime Timestamp,
    double Zone1Temperature,
    double Zone2Temperature,
    double Zone1Setpoint);

public sealed record PdfTrendPoint(DateTime Timestamp, double Value);

public static class PdfTrendExporter
{
    private static readonly SKColor[] SeriesColors =
    [
        SKColors.Red,
        SKColors.ForestGreen,
        SKColors.DodgerBlue
    ];

    private const double PageWidth = 842;
    private const double PageHeight = 595;
    private static readonly TimeSpan MaximumPageRange = TimeSpan.FromHours(6);
    private static readonly TimeSpan BucketSize = TimeSpan.FromMinutes(1);

    public static void Export(
        string filePath,
        IReadOnlyList<PdfTrendRecord> records,
        DateTime from,
        DateTime to)
    {
        using var document = SKDocument.CreatePdf(filePath);

        var pageStart = from;
        do
        {
            var pageEnd = to > pageStart + MaximumPageRange
                ? pageStart + MaximumPageRange
                : to;
            var pageRecords = records
                .Where(record => record.Timestamp >= pageStart && record.Timestamp <= pageEnd)
                .ToList();

            using var canvas = document.BeginPage((float)PageWidth, (float)PageHeight);
            DrawPage(canvas, pageRecords, pageStart, pageEnd, from, to);
            document.EndPage();

            if (pageEnd >= to)
                break;

            pageStart = pageEnd;
        } while (true);

        document.Close();
    }

    public static IReadOnlyList<IReadOnlyList<PdfTrendPoint>> Reduce(
        IReadOnlyList<PdfTrendRecord> records,
        DateTime from,
        DateTime to)
    {
        var reduced = new List<IReadOnlyList<PdfTrendPoint>>();
        if (records.Count == 0)
        {
            reduced.Add([]);
            reduced.Add([]);
            reduced.Add([]);
            return reduced;
        }

        var zone1Points = new List<PdfTrendPoint>();
        var zone2Points = new List<PdfTrendPoint>();
        var setpointPoints = new List<PdfTrendPoint>();
        var bucketStart = from;
        while (bucketStart <= to)
        {
            var bucketEnd = bucketStart + BucketSize;
            var bucket = records
                .Where(record => record.Timestamp >= bucketStart && record.Timestamp < bucketEnd)
                .ToList();

            AddSeriesPoints(zone1Points, bucket, record => record.Zone1Temperature);
            AddSeriesPoints(zone2Points, bucket, record => record.Zone2Temperature);
            AddSeriesPoints(setpointPoints, bucket, record => record.Zone1Setpoint);

            bucketStart = bucketEnd;
        }

        reduced.Add(zone1Points);
        reduced.Add(zone2Points);
        reduced.Add(setpointPoints);
        return reduced;
    }

    private static void AddSeriesPoints(
        ICollection<PdfTrendPoint> destination,
        IReadOnlyList<PdfTrendRecord> bucket,
        Func<PdfTrendRecord, double> selector)
    {
        if (bucket.Count == 0)
            return;

        var average = bucket.Average(selector);
        var minimum = bucket.MinBy(selector)!;
        var maximum = bucket.MaxBy(selector)!;
        var representative = bucket.MinBy(record => Math.Abs(selector(record) - average))!;

        var candidates = new[]
        {
            new PdfTrendPoint(representative.Timestamp, average),
            new PdfTrendPoint(minimum.Timestamp, selector(minimum)),
            new PdfTrendPoint(maximum.Timestamp, selector(maximum))
        };

        foreach (var point in candidates
                     .OrderBy(point => point.Timestamp)
                     .DistinctBy(point => (point.Timestamp, point.Value)))
        {
            destination.Add(point);
        }
    }

    private static void DrawPage(
        SKCanvas canvas,
        IReadOnlyList<PdfTrendRecord> records,
        DateTime pageStart,
        DateTime pageEnd,
        DateTime reportStart,
        DateTime reportEnd)
    {
        canvas.Clear(SKColors.White);

        using var titlePaint = CreateTextPaint(SKColors.Black, 20, true);
        using var detailPaint = CreateTextPaint(SKColors.DarkSlateGray, 10, false);
        using var axisPaint = CreateTextPaint(SKColors.DarkSlateGray, 9, false);
        using var gridPaint = new SKPaint { Color = SKColors.LightGray, StrokeWidth = 1, IsAntialias = true };
        using var borderPaint = new SKPaint { Color = SKColors.DarkSlateGray, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };

        canvas.DrawText("Oven Temperature Trend", 42, 42, titlePaint);
        canvas.DrawText(
            $"Range: {reportStart:yyyy-MM-dd HH:mm} - {reportEnd:yyyy-MM-dd HH:mm}    Page: {pageStart:HH:mm} - {pageEnd:HH:mm}",
            42,
            62,
            detailPaint);

        var plot = new SKRect(72, 96, 790, 490);
        var reduced = Reduce(records, pageStart, pageEnd);
        var values = records.SelectMany(record => new[]
        {
            record.Zone1Temperature,
            record.Zone2Temperature,
            record.Zone1Setpoint
        }).ToList();

        var minimum = values.Count == 0 ? 0 : Math.Floor(values.Min() / 10) * 10;
        var maximum = values.Count == 0 ? 100 : Math.Ceiling(values.Max() / 10) * 10;
        if (maximum <= minimum)
            maximum = minimum + 10;

        var padding = Math.Max(5, (maximum - minimum) * 0.05);
        minimum -= padding;
        maximum += padding;

        for (var index = 0; index <= 5; index++)
        {
            var value = minimum + (maximum - minimum) * index / 5;
            var y = plot.Bottom - (float)((value - minimum) / (maximum - minimum) * plot.Height);
            canvas.DrawLine(plot.Left, y, plot.Right, y, gridPaint);
            canvas.DrawText(value.ToString("0", CultureInfo.InvariantCulture), 20, y + 3, axisPaint);
        }

        for (var index = 0; index <= 6; index++)
        {
            var timestamp = pageStart + TimeSpan.FromTicks((pageEnd - pageStart).Ticks * index / 6);
            var x = plot.Left + (float)index / 6 * plot.Width;
            canvas.DrawLine(x, plot.Top, x, plot.Bottom, gridPaint);
            canvas.DrawText(timestamp.ToString("HH:mm"), x - 14, plot.Bottom + 18, axisPaint);
        }

        canvas.DrawRect(plot, borderPaint);
        DrawSeries(canvas, reduced[0], 0, pageStart, pageEnd, minimum, maximum, plot);
        DrawSeries(canvas, reduced[1], 1, pageStart, pageEnd, minimum, maximum, plot);
        DrawSeries(canvas, reduced[2], 2, pageStart, pageEnd, minimum, maximum, plot);

        DrawLegend(canvas, detailPaint);
        canvas.DrawText("Temperature (°C)", 18, plot.Top - 10, axisPaint);
    }

    private static void DrawSeries(
        SKCanvas canvas,
        IReadOnlyList<PdfTrendPoint> points,
        int seriesIndex,
        DateTime from,
        DateTime to,
        double minimum,
        double maximum,
        SKRect plot)
    {
        var seriesPoints = points.OrderBy(point => point.Timestamp).ToList();
        if (seriesPoints.Count == 0)
            return;

        using var paint = new SKPaint
        {
            Color = SeriesColors[seriesIndex],
            StrokeWidth = 2,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        using var path = new SKPath();

        for (var index = 0; index < seriesPoints.Count; index++)
        {
            var point = seriesPoints[index];
            var totalSeconds = Math.Max(1, (to - from).TotalSeconds);
            var x = plot.Left + (float)((point.Timestamp - from).TotalSeconds / totalSeconds * plot.Width);
            var y = plot.Bottom - (float)((point.Value - minimum) / (maximum - minimum) * plot.Height);
            if (index == 0)
                path.MoveTo(x, y);
            else
                path.LineTo(x, y);
        }

        canvas.DrawPath(path, paint);
    }

    private static void DrawLegend(SKCanvas canvas, SKPaint textPaint)
    {
        var labels = new[] { "Zone 1 Temperature", "Zone 2 Temperature", "JobPv" };
        for (var index = 0; index < labels.Length; index++)
        {
            var x = 82 + index * 210;
            using var linePaint = new SKPaint { Color = SeriesColors[index], StrokeWidth = 3, IsAntialias = true };
            canvas.DrawLine(x, 535, x + 22, 535, linePaint);
            canvas.DrawText(labels[index], x + 30, 539, textPaint);
        }
    }

    private static SKPaint CreateTextPaint(SKColor color, float textSize, bool bold)
        => new()
        {
            Color = color,
            TextSize = textSize,
            Typeface = bold ? SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) : SKTypeface.FromFamilyName("Arial"),
            IsAntialias = true
        };
}