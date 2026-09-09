using System.IO;

namespace TempControl.Services;

/// <summary>
/// Writes timestamped diagnostic lines to a rolling daily log file
/// in a Logs\ folder next to the running exe.
/// Thread-safe — can be called from background polling tasks.
/// </summary>
public sealed class DiagnosticLogger : IDisposable
{
    private readonly string _logFolder;
    private readonly Lock _lock = new();
    private StreamWriter? _writer;
    private string _currentFile = string.Empty;

    public static readonly DiagnosticLogger Instance = new();

    private DiagnosticLogger()
    {
        _logFolder = Path.Combine(
            AppContext.BaseDirectory, "Logs");
        Directory.CreateDirectory(_logFolder);
    }

    public void Log(string category, string message)
    {
        var now = DateTime.Now;
        var fileName = Path.Combine(_logFolder, $"debug_{now:yyyyMMdd}.txt");
        var line = $"[{now:HH:mm:ss.fff}] [{category,-12}] {message}";

        lock (_lock)
        {
            try
            {
                if (fileName != _currentFile)
                {
                    _writer?.Dispose();
                    _writer = new StreamWriter(fileName, append: true) { AutoFlush = true };
                    _currentFile = fileName;
                    _writer.WriteLine($"[{now:HH:mm:ss.fff}] [LOGGER      ] ── session started ──");
                }

                _writer?.WriteLine(line);
            }
            catch { /* never crash the app over a log write */ }
        }

        System.Diagnostics.Debug.WriteLine(line);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
