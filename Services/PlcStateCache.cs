using System.IO;
using System.Text.Json;

namespace TempControl.Services;

/// <summary>
/// Persists the last-known PLC register values (process parameters and manual
/// setpoints) to a local JSON file so the UI shows the previous values
/// immediately on startup before the first Modbus poll completes.
/// </summary>
public static class PlcStateCache
{
    private static readonly string CachePath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plc_state_cache.json");

    private sealed class CacheData
    {
        // Step parameters (D301–D320)
        public double Step1Zone1Temp { get; set; }
        public double Step1Zone2Temp { get; set; }
        public double Step1SoakTime  { get; set; }
        public double Step1RampRate  { get; set; }

        public double Step2Zone1Temp { get; set; }
        public double Step2Zone2Temp { get; set; }
        public double Step2SoakTime  { get; set; }
        public double Step2RampRate  { get; set; }

        public double Step3Zone1Temp { get; set; }
        public double Step3Zone2Temp { get; set; }
        public double Step3SoakTime  { get; set; }
        public double Step3RampRate  { get; set; }

        public double Step4Zone1Temp { get; set; }
        public double Step4Zone2Temp { get; set; }
        public double Step4SoakTime  { get; set; }
        public double Step4RampRate  { get; set; }

        public double Step5Zone1Temp { get; set; }
        public double Step5Zone2Temp { get; set; }
        public double Step5SoakTime  { get; set; }
        public double Step5RampRate  { get; set; }

        // Global process settings (D321–D324)
        public double ProcessZone1Safety { get; set; }
        public double ProcessZone2Safety { get; set; }
        public double ProcessBlower1     { get; set; }
        public double ProcessBlower2     { get; set; }

        // Manual setpoints (D325–D328)
        public double Zone1Setpoint          { get; set; }
        public double Zone2Setpoint          { get; set; }
        public double Zone1SafetyTemperature { get; set; }
        public double Zone2SafetyTemperature { get; set; }
    }

    /// <summary>
    /// Reads the cache file and pre-populates the PlcDataStore fields.
    /// Safe to call even if the file doesn't exist yet.
    /// </summary>
    public static void Load(PlcDataStore store)
    {
        try
        {
            if (!File.Exists(CachePath)) return;

            var json = File.ReadAllText(CachePath);
            var data = JsonSerializer.Deserialize<CacheData>(json);
            if (data is null) return;

            store.Step1Zone1Temp = data.Step1Zone1Temp;
            store.Step1Zone2Temp = data.Step1Zone2Temp;
            store.Step1SoakTime  = data.Step1SoakTime;
            store.Step1RampRate  = data.Step1RampRate;

            store.Step2Zone1Temp = data.Step2Zone1Temp;
            store.Step2Zone2Temp = data.Step2Zone2Temp;
            store.Step2SoakTime  = data.Step2SoakTime;
            store.Step2RampRate  = data.Step2RampRate;

            store.Step3Zone1Temp = data.Step3Zone1Temp;
            store.Step3Zone2Temp = data.Step3Zone2Temp;
            store.Step3SoakTime  = data.Step3SoakTime;
            store.Step3RampRate  = data.Step3RampRate;

            store.Step4Zone1Temp = data.Step4Zone1Temp;
            store.Step4Zone2Temp = data.Step4Zone2Temp;
            store.Step4SoakTime  = data.Step4SoakTime;
            store.Step4RampRate  = data.Step4RampRate;

            store.Step5Zone1Temp = data.Step5Zone1Temp;
            store.Step5Zone2Temp = data.Step5Zone2Temp;
            store.Step5SoakTime  = data.Step5SoakTime;
            store.Step5RampRate  = data.Step5RampRate;

            store.ProcessZone1Safety = data.ProcessZone1Safety;
            store.ProcessZone2Safety = data.ProcessZone2Safety;
            store.ProcessBlower1     = data.ProcessBlower1;
            store.ProcessBlower2     = data.ProcessBlower2;

            store.Zone1Setpoint          = data.Zone1Setpoint;
            store.Zone2Setpoint          = data.Zone2Setpoint;
            store.Zone1SafetyTemperature = data.Zone1SafetyTemperature;
            store.Zone2SafetyTemperature = data.Zone2SafetyTemperature;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Instance.Log("CACHE", $"Load failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes the current PlcDataStore register values to the cache file.
    /// Call on app exit and whenever values are written back to the PLC.
    /// </summary>
    public static void Save(PlcDataStore store)
    {
        try
        {
            var data = new CacheData
            {
                Step1Zone1Temp = store.Step1Zone1Temp,
                Step1Zone2Temp = store.Step1Zone2Temp,
                Step1SoakTime  = store.Step1SoakTime,
                Step1RampRate  = store.Step1RampRate,

                Step2Zone1Temp = store.Step2Zone1Temp,
                Step2Zone2Temp = store.Step2Zone2Temp,
                Step2SoakTime  = store.Step2SoakTime,
                Step2RampRate  = store.Step2RampRate,

                Step3Zone1Temp = store.Step3Zone1Temp,
                Step3Zone2Temp = store.Step3Zone2Temp,
                Step3SoakTime  = store.Step3SoakTime,
                Step3RampRate  = store.Step3RampRate,

                Step4Zone1Temp = store.Step4Zone1Temp,
                Step4Zone2Temp = store.Step4Zone2Temp,
                Step4SoakTime  = store.Step4SoakTime,
                Step4RampRate  = store.Step4RampRate,

                Step5Zone1Temp = store.Step5Zone1Temp,
                Step5Zone2Temp = store.Step5Zone2Temp,
                Step5SoakTime  = store.Step5SoakTime,
                Step5RampRate  = store.Step5RampRate,

                ProcessZone1Safety = store.ProcessZone1Safety,
                ProcessZone2Safety = store.ProcessZone2Safety,
                ProcessBlower1     = store.ProcessBlower1,
                ProcessBlower2     = store.ProcessBlower2,

                Zone1Setpoint          = store.Zone1Setpoint,
                Zone2Setpoint          = store.Zone2Setpoint,
                Zone1SafetyTemperature = store.Zone1SafetyTemperature,
                Zone2SafetyTemperature = store.Zone2SafetyTemperature,
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(CachePath, json);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Instance.Log("CACHE", $"Save failed: {ex.Message}");
        }
    }
}
