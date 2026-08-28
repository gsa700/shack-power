using System.IO.Ports;
using System.Text;

namespace ShackPower.Core;

/// <summary>
/// Opens the VE.Direct cable (19200 8N1) and listens to the SmartShunt's unsolicited broadcast:
/// bytes → <see cref="VeDirectFramer"/> (checksum-verified blocks) → <see cref="VeDirectParser"/>
/// → <see cref="ReadingAccumulator"/> → <see cref="ReadingReceived"/> at 1 Hz. Receive-only:
/// nothing is ever written to the port. UI-agnostic — events fire on a background thread, so
/// subscribers must marshal to their UI thread.
///
/// Resilience is the W2/LP-100A supervisor, ported intact: the poll runs under a loop that
/// detects a dropped device (a hard port I/O error, or <see cref="LinkHealth"/> seeing sustained
/// silence), closes the port so the OS fd is released, then backs off and reconnects. The loop
/// wraps the try — the shape that fixed LP-100A's sleep/resume death — and if a
/// <c>resolvePort</c> delegate is supplied it is re-queried each attempt, so a USB
/// replug/renumber is followed to wherever the cable now lives. The <see cref="Guard"/> /
/// <see cref="OpenGuarded"/> machinery exists because a surprise-removed FTDI can wedge the
/// native Open()/Close() calls forever, VE.Direct cables included.
/// </summary>
public sealed class SerialReader : IReadingSource
{
    private const int BaudRate = 19200;         // VE.Direct text protocol (validated on the shunt)
    private const int PollIntervalMs = 200;     // drain cadence; the device sends 2 blocks/s
    private const int SettleMs = 120;           // settle after open before trusting the stream
    private const int ReconnectDelayMs = 3000;  // backoff between reconnect attempts (prototype's 3 s)
    private const int OpenTimeoutMs = 4000;     // cap a native Open() that wedges on a bad device
    private const int CloseTimeoutMs = 1500;    // cap a native Close() that wedges on a removed device

    /// <summary>
    /// Silence window before a reconnect is forced. The healthy stream delivers two blocks a
    /// second, so multi-second silence is abnormal — but the UI's stale indicator (5 s) must fire
    /// well before this does, keeping the family rule "stale indicator ≪ silence threshold".
    /// Shortening this buys nothing: reconnecting cannot fix a device that is quiet on purpose.
    /// </summary>
    private const int SilenceTimeoutMs = 10000;

    private readonly ManualResetEventSlim _stop = new(false);  // signalled by Stop(); also wakes backoff waits
    private readonly VeDirectFramer _framer = new();           // long-lived; Reset() per session
    private SerialPort? _port;
    private Thread? _thread;
    private volatile bool _running;
    private volatile bool _linkFaulted;   // set when a read hits a hard port error (device gone)
    private volatile bool _everConnected; // true once a session has connected since Start(): a later
                                          // open failure is a reconnect, not a first-time setup problem
    private int _disposed;                // 0/1 via Interlocked — makes Dispose() idempotent

    public event Action<PowerReading>? ReadingReceived;
    public event Action<string, bool>? StatusChanged;  // (message, isError)

    public bool IsRunning => _running;

    public static string[] GetPortNames() => SerialPort.GetPortNames();

    public void Start(string portName, Func<string?>? resolvePort = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        Stop();
        _everConnected = false;
        _stop.Reset();
        _running = true;
        _thread = new Thread(() => Supervise(portName, resolvePort))
        {
            IsBackground = true,
            Name = $"VeDirect-{portName}",
        };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        // Set() happens to tolerate a disposed event today; don't rely on that from a shutdown path.
        try { _stop.Set(); } catch (ObjectDisposedException) { /* nothing left to wake */ }
        try { _thread?.Join(3000); } catch { /* ignore */ }
        _thread = null;
        ClosePort();
    }

    /// <summary>
    /// Run <paramref name="action"/> on a throwaway background thread and wait up to
    /// <paramref name="timeoutMs"/>. Returns whether it finished and any exception it threw. This
    /// is the guard around <c>SerialPort.Open()/Close()</c>, which on Linux can block forever when
    /// the FTDI is surprise-removed — if it wedges we abandon that thread (it unblocks once the USB
    /// stack finishes tearing the device down) and let the supervisor get on with reconnecting.
    /// </summary>
    private static (bool completed, Exception? error) Guard(Action action, int timeoutMs)
    {
        Exception? error = null;
        var done = new ManualResetEventSlim(false);
        new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        })
        { IsBackground = true, Name = "VeDirect-io" }.Start();
        return (done.Wait(timeoutMs), error);
    }

    /// <summary>
    /// Outer loop: (re-)resolve the port, run one connected session, and — unless we were asked to
    /// stop — back off and try again. Every session closes its port in a finally, so a dropped
    /// device never leaks an fd, and a replug is picked up by re-querying <paramref name="resolvePort"/>.
    /// The loop wraps the try, not the other way round — the first exception must lead to a
    /// reconnect, never to a silently dead thread.
    /// </summary>
    private void Supervise(string portName, Func<string?>? resolvePort)
    {
        try
        {
            while (_running)
            {
                var port = SafeResolve(resolvePort) ?? portName;
                RunSession(port);
                if (!_running) break;
                if (WaitForStop(ReconnectDelayMs)) break;   // Stop() during backoff → exit
            }
        }
        catch (Exception ex)
        {
            // Nothing may escape this thread: an unhandled exception on a background thread tears
            // down the whole process. The realistic trigger is Stop()'s join timing out on a wedged
            // session, after which Dispose() disposes _stop while this loop is still going — but a
            // throwing StatusChanged subscriber would do it too.
            Report($"{portName} reader stopped unexpectedly: {ex.Message}", true);
        }
        finally
        {
            ClosePort();
            if (!_running) Report("Disconnected", false);
        }
    }

    private static string? SafeResolve(Func<string?>? resolvePort)
    {
        try { return resolvePort?.Invoke(); } catch { return null; }
    }

    /// <summary>
    /// Raise <see cref="StatusChanged"/> without letting a subscriber's exception escape the reader
    /// thread — see the catch in <see cref="Supervise"/> for why that matters.
    /// </summary>
    private void Report(string message, bool isError)
    {
        try { StatusChanged?.Invoke(message, isError); } catch { /* subscriber's problem, not ours */ }
    }

    /// <summary>
    /// Wait on the stop signal, treating a disposed event as "stop now". <see cref="Stop"/>'s join
    /// can time out on a wedged session and <see cref="Dispose"/> then disposes <c>_stop</c>
    /// underneath this thread; without this the wait would throw and crash the process.
    /// </summary>
    private bool WaitForStop(int milliseconds)
    {
        try { return _stop.Wait(milliseconds); } catch (ObjectDisposedException) { return true; }
    }

    /// <summary>
    /// Open one port under the <see cref="Guard"/> watchdog, with an explicit ownership handoff so
    /// a slow open can't orphan the handle. If the native <c>Open()</c> outruns the timeout the
    /// caller abandons that thread — but the open may still succeed a moment later, and the
    /// resulting port would then be held by nobody: the next reconnect attempt would hit a
    /// self-inflicted "port in use". So the opener and the supervisor race for a single atomic
    /// claim, and whichever side loses it closes the port.
    /// </summary>
    private static (bool completed, Exception? error, SerialPort? port) OpenGuarded(string portName)
    {
        SerialPort? handoff = null;
        var claim = 0;   // 0 = unclaimed, 1 = opener published it, 2 = caller abandoned the open

        var (completed, error) = Guard(() =>
        {
            var port = new SerialPort(portName, BaudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                // The prototype (pyserial) asserted both lines by default and the shunt streamed
                // happily; harmless for a receive-only cable, so match it rather than find out
                // which VE.Direct cable revisions care.
                DtrEnable = true,
                RtsEnable = true,
                ReadTimeout = 500,
                Encoding = Encoding.Latin1,
            };
            try { port.Open(); }
            catch { port.Dispose(); throw; }   // failed open: nothing to hand off, don't leak the object

            // Publish before claiming: if our claim loses, the caller has already seen `handoff`
            // (its own interlocked op fences the read) and closes it; if it wins, the caller never looks.
            handoff = port;
            if (Interlocked.CompareExchange(ref claim, 1, 0) == 0) return;

            // The caller gave up on us. Nobody is watching this port, so close it here — this
            // thread is already abandoned, so blocking on a removed device's Close() costs nothing.
            handoff = null;
            CloseQuietly(port);
        }, OpenTimeoutMs);

        if (completed) return (true, error, handoff);

        // Timed out. Take the claim so a late-completing open cleans up after itself; if the opener
        // beat us to it, the port is ours and we close it — we've already blown the watchdog
        // budget, so let the supervisor back off and start a fresh session rather than use it.
        if (Interlocked.CompareExchange(ref claim, 2, 0) != 0 && handoff is { } late)
            Guard(() => CloseQuietly(late), CloseTimeoutMs);

        return (false, error, null);
    }

    /// <summary>Close and dispose a port, swallowing anything it throws. Can block if the device is gone.</summary>
    private static void CloseQuietly(SerialPort port)
    {
        try { if (port.IsOpen) port.Close(); } catch { /* ignore */ }
        try { port.Dispose(); } catch { /* ignore */ }
    }

    /// <summary>One connected session: open, drain the broadcast until the link drops or we're stopped, close.</summary>
    private void RunSession(string portName)
    {
        _linkFaulted = false;
        var health = new LinkHealth(Math.Max(1, SilenceTimeoutMs / PollIntervalMs));

        var (opened, openError, port) = OpenGuarded(portName);

        if (!opened)
        {
            if (_running) StatusChanged?.Invoke($"{portName} not responding — retrying…", true);
            return;   // abandon the wedged open thread; supervisor backs off and retries
        }
        if (openError is not null)
        {
            if (_running) StatusChanged?.Invoke(DescribeRetry(openError, portName), true);
            return;
        }
        if (port is null) return;   // completed without error but no port (shouldn't happen); retry
        _port = port;

        try
        {
            if (WaitForStop(SettleMs)) return;  // stop requested while settling
            try { port.DiscardInBuffer(); } catch { /* non-fatal */ }
            _framer.Reset();                    // a part-frame must not glue across sessions
            var accumulator = new ReadingAccumulator();   // fresh per session: no stale fields after reconnect
            _everConnected = true;
            StatusChanged?.Invoke($"Connected on {portName}", false);

            var buffer = new byte[512];
            while (_running && !_linkFaulted && !health.IsLost)
            {
                health.RecordCycle(DrainAvailable(port, buffer, accumulator));
                if (_linkFaulted) health.Fault();
                if (WaitForStop(PollIntervalMs)) break;
            }

            if (_running && (health.IsLost || _linkFaulted))
                StatusChanged?.Invoke($"{portName} lost — reconnecting…", true);
        }
        catch (Exception ex) when (_running)
        {
            StatusChanged?.Invoke(DescribeRetry(ex, portName), true);
        }
        finally
        {
            ClosePort();   // always release the fd — a dropped device must not leave a dangling handle
        }
    }

    /// <summary>
    /// Read everything currently available, feed the framer/parser/accumulator, and raise
    /// <see cref="ReadingReceived"/> for each completed merge. Returns whether any verified block
    /// arrived this cycle — the signal <see cref="LinkHealth"/> counts. A hard port error flags
    /// <see cref="_linkFaulted"/> instead of throwing, so the session winds down deliberately.
    /// </summary>
    private bool DrainAvailable(SerialPort port, byte[] buffer, ReadingAccumulator accumulator)
    {
        var any = false;
        try
        {
            var available = port.BytesToRead;
            while (available > 0)
            {
                var n = port.Read(buffer, 0, Math.Min(available, buffer.Length));
                if (n <= 0) break;
                foreach (var body in _framer.Feed(buffer.AsSpan(0, n)))
                {
                    if (!VeDirectParser.TryParseBlock(body, out var fields)) continue;
                    any = true;
                    if (accumulator.Feed(fields) is { } reading)
                        ReadingReceived?.Invoke(reading);
                }
                available = port.BytesToRead;
            }
        }
        catch (Exception ex)
        {
            // A hard port error (device unplugged / port closed) means the link is gone — flag it
            // so the session tears down and reconnects. Silence doesn't throw (BytesToRead just
            // reads 0), so anything caught here is fatal to the session.
            if (ex is IOException or ObjectDisposedException or InvalidOperationException or UnauthorizedAccessException)
                _linkFaulted = true;
            else
                throw;
        }
        return any;
    }

    /// <summary>
    /// Describe an open/session error for the status line. Once we've connected at least once this
    /// session (<see cref="_everConnected"/>), a transient access error is a mid-replug
    /// re-enumeration, so <see cref="SerialErrors.Describe"/> returns a calm "…reconnecting…" that
    /// already implies a retry — don't double the cue. Anything else keeps the explicit suffix.
    /// </summary>
    private string DescribeRetry(Exception ex, string portName)
    {
        var msg = SerialErrors.Describe(ex, portName, OperatingSystem.IsLinux(), reconnecting: _everConnected);
        var calm = _everConnected && ex is UnauthorizedAccessException;
        return calm ? msg : msg + " Retrying…";
    }

    private void ClosePort()
    {
        var port = Interlocked.Exchange(ref _port, null);   // one closer wins
        if (port is null) return;
        // Close under a watchdog: on Linux a surprise-removed FTDI can make Close()/Dispose()
        // block forever. If it wedges we abandon that thread rather than freeze reconnect or Stop().
        Guard(() => CloseQuietly(port), CloseTimeoutMs);
    }

    public void Dispose()
    {
        // Idempotent by construction — see W2's SerialReader for the history.
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Stop();
        _stop.Dispose();
    }
}
