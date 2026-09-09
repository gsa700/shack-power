namespace ShackPower.Core.Cerbo;

/// <summary>
/// <see cref="IReadingSource"/> over a Cerbo GX's Modbus TCP server: the 2026-09 architecture,
/// where the GX owns every Victron cable and this app is one network client among several. Polls
/// the shunt's battery service at 1 Hz and folds the MultiPlus / MPPT services and the DVCC
/// charge limit into the same <see cref="PowerReading"/>, so the display, logging and chart
/// pipeline built for the direct VE.Direct link runs unchanged.
///
/// Resilience is the family supervisor, simplified for TCP: the poll runs under a loop that
/// connects, polls until a transport error or <see cref="LinkHealth"/> declares sustained
/// silence, disconnects, backs off and retries. No port re-resolution — the endpoint is fixed
/// by configuration (the <c>portName</c> given to <see cref="Start"/> is the <c>host[:port]</c>
/// text, resolved once per session so a renamed host is picked up on reconnect).
/// </summary>
public sealed class CerboReadingSource : IReadingSource
{
    private const int PollIntervalMs = 1000;      // the shunt itself updates at 1 Hz
    private const int ReconnectDelayMs = 3000;    // family backoff
    private const int SilenceTimeoutMs = 10000;   // consecutive empty polls before reconnecting

    private readonly Func<System.Net.IPEndPoint, ICerboModbus> _factory;
    private readonly CerboSettings _settings;
    private readonly ManualResetEventSlim _stop = new(false);
    private Thread? _thread;
    private volatile bool _running;
    private int _disposed;

    public event Action<PowerReading>? ReadingReceived;
    public event Action<string, bool>? StatusChanged;

    public bool IsRunning => _running;

    /// <param name="settings">Unit IDs and options; <see cref="CerboSettings.Host"/> is
    /// overridden by the endpoint text passed to <see cref="Start"/>.</param>
    /// <param name="factory">Builds the Modbus client for an endpoint; tests inject a fake or a
    /// loopback FluentModbus server, production uses <see cref="FluentModbusCerbo"/>.</param>
    public CerboReadingSource(CerboSettings settings, Func<System.Net.IPEndPoint, ICerboModbus>? factory = null)
    {
        _settings = settings;
        _factory = factory ?? (ep => new FluentModbusCerbo(ep));
    }

    public void Start(string portName, Func<string?>? resolvePort = null)
    {
        Stop();
        _stop.Reset();
        _running = true;
        _thread = new Thread(() => Supervise(portName)) { IsBackground = true, Name = "Cerbo-Modbus" };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _stop.Set();
        try { _thread?.Join(4000); } catch { /* ignore */ }
        _thread = null;
    }

    private void Supervise(string endpointText)
    {
        while (_running)
        {
            var endpoint = CerboSettings.ParseEndpoint(endpointText);
            if (endpoint is null)
            {
                StatusChanged?.Invoke($"Cerbo GX address '{endpointText}' is not valid. Retrying…", true);
                if (WaitForStop(ReconnectDelayMs)) break;
                continue;
            }

            RunSession(endpoint, endpointText);
            if (!_running) break;
            if (WaitForStop(ReconnectDelayMs)) break;
        }
        StatusChanged?.Invoke("Disconnected", false);
    }

    private void RunSession(System.Net.IPEndPoint endpoint, string endpointText)
    {
        ICerboModbus? modbus = null;
        try
        {
            modbus = _factory(endpoint);
            modbus.Connect();
            StatusChanged?.Invoke($"Connected to Cerbo GX at {endpointText}", false);

            var health = new LinkHealth(SilenceTimeoutMs / PollIntervalMs);
            while (_running && !health.IsLost)
            {
                var reading = Poll(modbus);
                health.RecordCycle(reading is not null);
                if (reading is not null) ReadingReceived?.Invoke(reading);
                if (WaitForStop(PollIntervalMs)) return;
            }
            if (_running && health.IsLost)
                StatusChanged?.Invoke($"No data from the shunt (unit {_settings.BatteryUnit}) via {endpointText} — reconnecting…", true);
        }
        catch (Exception ex) when (_running)
        {
            StatusChanged?.Invoke(Describe(ex, endpointText), true);
        }
        finally
        {
            try { modbus?.Dispose(); } catch { /* best effort */ }
        }
    }

    /// <summary>One cycle: battery first (it decides whether this counts as data), then the
    /// optional services. Transport failures propagate to the session; per-register Modbus
    /// exceptions are absorbed inside <see cref="CerboDecoder"/>.</summary>
    private PowerReading? Poll(ICerboModbus modbus)
    {
        CerboDecoder.RegisterReader read = modbus.ReadHolding;
        var reading = CerboDecoder.ReadBattery(read, _settings.BatteryUnit);
        if (reading is null) return null;

        var charger = _settings.VeBusUnit is { } vu ? CerboDecoder.ReadVeBus(read, vu) : null;
        var solar = _settings.SolarUnit is { } su ? CerboDecoder.ReadSolar(read, su) : null;
        var limit = _settings.ReadDvccLimit ? CerboDecoder.ReadDvccLimit(read) : null;
        return reading with { Charger = charger, Solar = solar, DvccChargeLimitAmps = limit };
    }

    private static string Describe(Exception ex, string endpointText) => ex switch
    {
        System.Net.Sockets.SocketException se when se.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionRefused
            => $"Cerbo GX at {endpointText} refused the connection — is Modbus TCP enabled (Settings → Integrations)? Retrying…",
        System.Net.Sockets.SocketException or TimeoutException or IOException
            => $"Cerbo GX at {endpointText} unreachable — reconnecting…",
        _ => $"Cerbo GX link error ({ex.GetType().Name}: {ex.Message}) — reconnecting…",
    };

    private bool WaitForStop(int ms) => _stop.Wait(ms) || !_running;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Stop();
        _stop.Dispose();
    }
}
