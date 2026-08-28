using ShackPower.Core;

namespace ShackPower.App.Services;

/// <summary>
/// Bridges readings to the daily CSV log (LP-100A's TxLoggingService shape). Subscribes to
/// <see cref="MeterService.ReadingReceived"/> — which fires on the UI thread, so there is no
/// locking and the writer stays single-threaded. IO errors surface as <see cref="LastError"/>
/// for Setup to show; they never kill the display (the prototype's rule, made visible).
/// </summary>
public sealed class PowerLoggingService : IDisposable
{
    private readonly MeterService _meter;
    private readonly PowerLogWriter _writer;

    public PowerLoggingService(MeterService meter, string logDirectory, bool enabled)
    {
        _meter = meter;
        _writer = new PowerLogWriter(logDirectory);
        Enabled = enabled;
        _meter.ReadingReceived += OnReading;
    }

    public bool Enabled { get; set; }
    public string LogDirectory => _writer.Directory;

    /// <summary>Rows written by this run (not the file's total — the prototype wrote there too).</summary>
    public int LoggedCount { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>Fires on the UI thread when the count or error state moves.</summary>
    public event Action? Changed;

    private void OnReading(PowerReading r)
    {
        if (!Enabled) return;
        try
        {
            _writer.Append(new PowerLogRecord
            {
                Timestamp = DateTime.Now,
                Volts = r.Volts,
                Amps = r.Amps,
                Watts = r.Watts,
            });
            LoggedCount++;
            LastError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = ex.Message;
        }
        Changed?.Invoke();
    }

    public void Dispose() => _meter.ReadingReceived -= OnReading;
}
