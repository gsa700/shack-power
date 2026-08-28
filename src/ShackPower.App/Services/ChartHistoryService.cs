using ShackPower.Core;

namespace ShackPower.App.Services;

/// <summary>
/// Feeds the chart. Two sources, never mixed with the live file: the in-memory
/// <see cref="Ring"/> collects every reading as it arrives (so "now" needs no disk), and past
/// data comes from one-shot day-file loads on a worker thread — <b>the chart never reads the
/// file currently being appended</b>. Runs for the whole app lifetime so the tail exists before
/// the Chart window is first opened.
/// </summary>
public sealed class ChartHistoryService : IDisposable
{
    private readonly MeterService _meter;

    public ChartHistoryService(MeterService meter, string logDirectory)
    {
        _meter = meter;
        LogDirectory = logDirectory;
        _meter.ReadingReceived += OnReading;
    }

    public string LogDirectory { get; }

    /// <summary>A full day of 1 Hz tail (~4 MB) — enough that "today" can render from memory
    /// alone no matter how long the app has been up.</summary>
    public ChartRing Ring { get; } = new(86400);

    /// <summary>Fires on the UI thread once per reading — the chart's live repaint tick.</summary>
    public event Action? ReadingTick;

    private void OnReading(PowerReading r)
    {
        Ring.Add(new PowerLogEntry
        {
            Timestamp = DateTime.Now,
            Volts = r.Volts,
            Amps = r.Amps,
            Watts = r.Watts,
        });
        ReadingTick?.Invoke();
    }

    /// <summary>Read one day's CSV off-thread (a full day parses in tens of ms, but never on the
    /// UI thread — the LP-100A rule about hitches).</summary>
    public Task<IReadOnlyList<PowerLogEntry>> LoadDayAsync(DateOnly day) =>
        Task.Run(() => PowerLogReader.ReadDay(LogDirectory, day));

    public IReadOnlyList<DateOnly> ListDays() => PowerLogReader.ListDays(LogDirectory);

    public void Dispose() => _meter.ReadingReceived -= OnReading;
}
