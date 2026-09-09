namespace TempControl.Models;

public record TemperatureDataPoint(DateTime Timestamp, double Value, string Series);
