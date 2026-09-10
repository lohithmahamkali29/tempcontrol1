using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TempControl.ViewModels;

namespace TempControl.Views;

public partial class GraphView : UserControl
{
    public GraphView() => InitializeComponent();

    private void GraphChartHost_MouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is not GraphViewModel graphViewModel || GraphChartHost.ActualWidth <= 0)
            return;

        var position = e.GetPosition(GraphChartHost);
        var x = Math.Clamp(position.X, 0, GraphChartHost.ActualWidth);
        GraphRulerLine.X1 = x;
        GraphRulerLine.X2 = x;
        GraphRulerLine.Y2 = GraphChartHost.ActualHeight;
        GraphRulerLine.Visibility = Visibility.Visible;

        var timestamp = graphViewModel.UpdateCursorFromPosition(x, GraphChartHost.ActualWidth);
        if (timestamp is null)
        {
            HideCursorValues();
            return;
        }

        var values = graphViewModel.GetCursorValues(timestamp.Value);
        UpdateCursorValue(Zone1CursorPoint, Zone1CursorLabel, Zone1CursorText, x, values[0].Value, 0);
        UpdateCursorValue(Zone2CursorPoint, Zone2CursorLabel, Zone2CursorText, x, values[1].Value, 1);
        UpdateCursorValue(JobPvCursorPoint, JobPvCursorLabel, JobPvCursorText, x, values[2].Value, 2);
    }

    private void GraphChartHost_MouseLeave(object sender, MouseEventArgs e)
    {
        GraphRulerLine.Visibility = Visibility.Collapsed;
        HideCursorValues();
    }

    private void UpdateCursorValue(
        System.Windows.Shapes.Ellipse point,
        Border label,
        TextBlock text,
        double x,
        double value,
        int labelOffset)
    {
        var chartHeight = GraphChartHost.ActualHeight;
        var y = chartHeight - (Math.Clamp(value, 0, 250) / 250d * chartHeight);
        var labelLeft = Math.Clamp(x + 8, 4, Math.Max(4, GraphChartHost.ActualWidth - label.Width - 4));
        var labelTop = Math.Clamp(y - (label.Height / 2) + ((labelOffset - 1) * 18), 4, Math.Max(4, chartHeight - label.Height - 4));

        Canvas.SetLeft(point, x - (point.Width / 2));
        Canvas.SetTop(point, y - (point.Height / 2));
        Canvas.SetLeft(label, labelLeft);
        Canvas.SetTop(label, labelTop);
        text.Text = value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        point.Visibility = Visibility.Visible;
        label.Visibility = Visibility.Visible;
    }

    private void HideCursorValues()
    {
        Zone1CursorPoint.Visibility = Visibility.Collapsed;
        Zone2CursorPoint.Visibility = Visibility.Collapsed;
        JobPvCursorPoint.Visibility = Visibility.Collapsed;
        Zone1CursorLabel.Visibility = Visibility.Collapsed;
        Zone2CursorLabel.Visibility = Visibility.Collapsed;
        JobPvCursorLabel.Visibility = Visibility.Collapsed;
    }
}
