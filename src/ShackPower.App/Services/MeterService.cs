using Avalonia.Threading;
using ShackPower.Core;

namespace ShackPower.App.Services;

/// <summary>
/// Single shared owner of the SmartShunt connection (LP-100A's single-meter shape). Wraps an
/// <see cref="IReadingSource"/> — the real <see cref="SerialReader"/>, or
/// <see cref="VeDirectSimReader"/> under <c>--sim</c> — marshals its background-thread events
/// onto the UI thread, and re-broadcasts them so any number of views (main readout, chart)
/// observe one serial connection.
/// </summary>
public sealed class MeterService : IDisposable
{
    /// <summary>The prototype's value: the shunt broadcasts at 1 Hz, so five missed seconds
    /// means the numbers on screen are frozen, not live. Well inside the reader's 10 s
    /// silence-reconnect threshold, per the family rule.</summary>
    private const double StaleAfterSeconds = 5.0;

    private readonly IReadingSource _reader;
    private readonly DispatcherTimer _watchdog;
    private DateTime _lastReadingUtc;

    public bool IsSimulated { get; }
    public PowerReading? Current { get; private set; }
    public bool IsConnected { get; private set; }
    public string? CurrentPort { get; private set; }
    public string Status { get; private set; } = "Disconnected";
    public bool StatusIsError { get; private set; }

    /// <summary>True when connected but no reading has arrived recently — the cable is out or
    /// the shunt has gone quiet, so the readouts are frozen, not live.</summary>
    public bool IsStale { get; private set; }

    /// <summary>Fires on the UI thread for every merged reading.</summary>
    public event Action<PowerReading>? ReadingReceived;

    /// <summary>Fires on the UI thread when connection/status changes.</summary>
    public event Action? StateChanged;

    public MeterService(bool simulated = false)
    {
        IsSimulated = simulated;
        _reader = simulated ? new VeDirectSimReader() : new SerialReader();

        _reader.ReadingReceived += r => Dispatcher.UIThread.Post(() =>
        {
            // A reading can still be queued on the UI thread when Disconnect runs; ignore it so
            // it doesn't revive Current/IsStale after we've torn the connection down.
            if (!IsConnected) return;
            _lastReadingUtc = DateTime.UtcNow;
            Current = r;
            if (IsStale) { IsStale = false; StateChanged?.Invoke(); }
            ReadingReceived?.Invoke(r);
        });

        _reader.StatusChanged += (msg, isError) => Dispatcher.UIThread.Post(() =>
        {
            Status = msg;
            StatusIsError = isError;

            // The reader supervises itself, so an error doesn't mean the session is over — it
            // usually means "dropped, reconnecting". Tie IsConnected to whether the reader thread
            // is still running rather than to the last message: clearing it on any error would
            // make the ReadingReceived guard above discard the very frames a successful reconnect
            // starts delivering, leaving the app permanently frozen on a link that had recovered.
            IsConnected = _reader.IsRunning;

            if (!IsConnected)
            {
                IsStale = false;
            }
            else if (isError)
            {
                // Reconnecting: whatever is on screen is frozen, which is exactly what stale means.
                IsStale = true;
            }
            else
            {
                // A fresh (re)connect — restart the grace period before the watchdog can flag stale.
                _lastReadingUtc = DateTime.UtcNow;
                IsStale = false;
            }

            StateChanged?.Invoke();
        });

        // Watchdog: flag a connection whose readings have stopped (without a serial error) so the
        // UI can stop implying the frozen values are live.
        _watchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _watchdog.Tick += (_, _) =>
        {
            if (!IsConnected || IsStale) return;
            if ((DateTime.UtcNow - _lastReadingUtc).TotalSeconds >= StaleAfterSeconds)
            {
                IsStale = true;
                StateChanged?.Invoke();
            }
        };
        _watchdog.Start();
    }

    public static string[] GetPortNames() => SerialReader.GetPortNames();

    /// <param name="serial">
    /// The cable's USB chip serial, when known (VE.Direct FT-X, e.g. VEAUI3T2A). Passed so each
    /// reconnect attempt re-resolves the port: a cable that comes back on a different COM number
    /// after a sleep/resume or a replug is then followed to wherever it now is, instead of the
    /// reader retrying a port that no longer exists.
    /// </param>
    public void Connect(string port, string? serial = null)
    {
        CurrentPort = port;
        Status = $"Connecting {port}…";
        StatusIsError = false;
        IsConnected = true;
        IsStale = false;
        _lastReadingUtc = DateTime.UtcNow;   // grace period before the watchdog can flag stale
        _reader.Start(port, () =>
        {
            var resolved = PortIdentity.ResolvePort(port, serial) ?? port;
            // Runs on the reader thread; hop to the UI thread to publish a port that has moved,
            // so the readouts don't keep naming a COM number the cable has left behind.
            if (!string.Equals(resolved, CurrentPort, StringComparison.OrdinalIgnoreCase))
                Dispatcher.UIThread.Post(() =>
                {
                    CurrentPort = resolved;
                    StateChanged?.Invoke();
                });
            return resolved;
        });
        StateChanged?.Invoke();
    }

    public void Disconnect()
    {
        _reader.Stop();
        IsConnected = false;
        IsStale = false;
        Current = null;
        Status = "Disconnected";
        StatusIsError = false;
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        _watchdog.Stop();
        _reader.Dispose();
    }
}
