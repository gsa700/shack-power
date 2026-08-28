namespace ShackPower.Core;

/// <summary>
/// A fake SmartShunt for demos and UI work without hardware — essential here because the real
/// port is held by whichever monitor is live. Emits merged <see cref="PowerReading"/>s at 1 Hz
/// through the same events as <see cref="SerialReader"/> (reading-level synthesis, like
/// <c>W2SimReader</c>: the framer/parser have their own byte-level tests, the sim exists to
/// drive the App pipeline).
///
/// Scenario: a ~13.5 V battery floating with noise; a load that steps between an idle draw
/// (~−0.4 A) and periodic 18–22 A "transmit" bursts to exercise the chart's dynamic range; an
/// occasional charger phase (+6 A); and, every few minutes, a sagging-voltage episode that
/// crosses the warn/alarm thresholds and raises the low-voltage alarm bit. Unlike the real
/// station shunt (DC-meter mode) the sim reports battery-monitor mode (<c>MON 0</c>) with live
/// SOC/CE/TTG, so the UI rows that real hardware leaves blank get exercised somewhere.
/// </summary>
public sealed class VeDirectSimReader : IReadingSource
{
    private const int TickMs = 1000;

    private readonly Random _rnd = new();
    private Thread? _thread;
    private volatile bool _running;

    public event Action<PowerReading>? ReadingReceived;
    public event Action<string, bool>? StatusChanged;

    public bool IsRunning => _running;

    public void Start(string portName, Func<string?>? resolvePort = null)
    {
        Stop();
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "VeDirect-SIM" };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        try { _thread?.Join(1500); } catch { /* ignore */ }
        _thread = null;
    }

    private void Loop()
    {
        StatusChanged?.Invoke("Simulated SmartShunt (demo)", false);

        var soc = 87.0;                 // percent
        var consumedAh = -12.4;
        var vmin = double.MaxValue;
        var vmax = double.MinValue;
        var kwhDrawn = 2.25;            // seed near the real shunt's odometer for familiarity
        var kwhCharged = 0.4;
        var burstLeft = 0;              // seconds remaining of the current TX burst
        var untilBurst = 8;             // seconds until the next one
        var chargeLeft = 0;             // seconds remaining of the charger phase
        var sagLeft = 0;                // seconds remaining of the low-voltage episode
        var untilSag = 150;

        while (_running)
        {
            // Advance the load state machine one second per tick.
            if (burstLeft > 0) burstLeft--;
            else if (--untilBurst <= 0) { burstLeft = _rnd.Next(5, 16); untilBurst = _rnd.Next(20, 61); }
            if (chargeLeft > 0) chargeLeft--;
            else if (_rnd.NextDouble() < 0.005) chargeLeft = 30;
            if (sagLeft > 0) sagLeft--;
            else if (--untilSag <= 0) { sagLeft = 20; untilSag = _rnd.Next(120, 241); }

            var amps = burstLeft > 0 ? -(18.0 + _rnd.NextDouble() * 4.0)
                     : chargeLeft > 0 ? 6.0 + _rnd.NextDouble() * 0.5
                     : -(0.35 + _rnd.NextDouble() * 0.1);

            // Base voltage sags under load and during the scripted brown-out episode.
            var volts = 13.5
                + (chargeLeft > 0 ? 0.4 : 0.0)
                + amps * 0.012                              // IR drop: ~0.25 V at a 20 A burst
                - (sagLeft > 0 ? 2.2 : 0.0)
                + (_rnd.NextDouble() - 0.5) * 0.02;

            var watts = volts * amps;
            var hours = TickMs / 3600000.0;
            consumedAh += amps * hours;
            soc = Math.Clamp(soc + amps * hours / 2.0 * 100.0, 5.0, 100.0);   // ~200 Ah bank
            if (amps < 0) kwhDrawn += -watts * hours / 1000.0;
            else kwhCharged += watts * hours / 1000.0;
            vmin = Math.Min(vmin, volts);
            vmax = Math.Max(vmax, volts);

            var lowVoltage = volts < 11.5;
            ReadingReceived?.Invoke(new PowerReading
            {
                Volts = Math.Round(volts, 3),
                Amps = Math.Round(amps, 3),
                Watts = Math.Round(watts, 0),
                Soc = Math.Round(soc, 1),
                ConsumedAh = Math.Round(consumedAh, 1),
                TtgMinutes = amps < -0.05 ? Math.Round(soc / 100.0 * 200.0 / -amps * 60.0, 0) : -1,
                AlarmOn = lowVoltage,
                AlarmReasons = lowVoltage ? 1 : 0,
                DeviceName = "SmartShunt 300A",
                Firmware = "0419",
                MonitorMode = 0,
                VminHistory = Math.Round(vmin, 3),
                VmaxHistory = Math.Round(vmax, 3),
                TotalKwhDrawn = Math.Round(kwhDrawn, 2),
                TotalKwhCharged = Math.Round(kwhCharged, 2),
            });

            Thread.Sleep(TickMs);
        }

        StatusChanged?.Invoke("Disconnected", false);
    }

    public void Dispose() => Stop();
}
