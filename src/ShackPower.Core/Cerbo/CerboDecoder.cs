using System.Net.Sockets;

namespace ShackPower.Core.Cerbo;

/// <summary>
/// Pure register-to-reading decoding for the Cerbo path. Each <c>Read*</c> takes a reader
/// delegate so the same code serves the live socket and the tests. A group that answers a
/// Modbus exception (the GX does that for a path its device doesn't publish — no temperature
/// sensor, no time-to-go while not discharging) yields nulls for that group, never a failed
/// reading; a transport failure propagates so the session can reconnect. Reads are deliberately
/// narrow: one unavailable register fails the whole block it sits in, so optional values are
/// fetched on their own.
/// </summary>
public static class CerboDecoder
{
    public delegate ushort[] RegisterReader(byte unit, ushort start, ushort count);

    /// <summary>The SmartShunt as <c>com.victronenergy.battery</c>. Returns null only when even
    /// the core P/V/I block is unreadable — "no reading this cycle", the link-health signal.</summary>
    public static PowerReading? ReadBattery(RegisterReader read, byte unit, string? deviceName = null)
    {
        var core = Try(() => read(unit, 258, 4));     // 258 P, 259 V, 260 (unused), 261 I
        if (core is null) return null;

        var state = Try(() => read(unit, 265, 2));    // 265 CE, 266 SOC
        var alarms = Try(() => read(unit, 267, 6));   // 267 alarm, 268 lowV, 269 highV, 272 lowSOC
        var hist = Try(() => read(unit, 287, 2));     // 287 min V, 288 max V
        var energy = Try(() => read(unit, 301, 2));   // 301 discharged, 302 charged (kWh)
        var ttg = Try(() => read(unit, 303, 1));      // 303 TTG (seconds ×0.01); absent while not discharging

        return new PowerReading
        {
            Watts = CerboRegisters.Battery.Power.Decode(core[0]),
            Volts = CerboRegisters.Battery.Voltage.Decode(core[1]),
            Amps = CerboRegisters.Battery.Current.Decode(core[3]),
            ConsumedAh = state is null ? null : CerboRegisters.Battery.ConsumedAh.Decode(state[0]),
            Soc = state is null ? null : CerboRegisters.Battery.Soc.Decode(state[1]),
            AlarmOn = alarms is not null && alarms[0] != 0,
            AlarmReasons = alarms is null ? 0 : AlarmMask(alarms),
            VminHistory = hist is null ? null : CerboRegisters.Battery.MinVoltage.Decode(hist[0]),
            VmaxHistory = hist is null ? null : CerboRegisters.Battery.MaxVoltage.Decode(hist[1]),
            TotalKwhDrawn = energy is null ? null : CerboRegisters.Battery.DischargedKwh.Decode(energy[0]),
            TotalKwhCharged = energy is null ? null : CerboRegisters.Battery.ChargedKwh.Decode(energy[1]),
            // Keep VE.Direct's convention downstream: minutes, −1 = infinite (not discharging).
            TtgMinutes = ttg is null ? -1 : Math.Round(CerboRegisters.Battery.TimeToGoSeconds.Decode(ttg[0]) / 60.0),
            MonitorMode = 0,                          // a shunt on the GX is a battery monitor
            DeviceName = deviceName ?? "SmartShunt via Cerbo GX",
        };
    }

    /// <summary>Map the GX's per-alarm registers onto VE.Direct's <c>AR</c> bitmask so
    /// <see cref="PowerReading.DescribeAlarm"/> keeps working unchanged.</summary>
    private static int AlarmMask(ushort[] a)
    {
        var mask = 0;
        if (a[1] != 0) mask |= 1;   // 268 low voltage
        if (a[2] != 0) mask |= 2;   // 269 high voltage
        if (a[5] != 0) mask |= 4;   // 272 low SOC
        return mask;
    }

    /// <summary>The MultiPlus as <c>com.victronenergy.vebus</c>; null when unreachable.</summary>
    public static ChargerSnapshot? ReadVeBus(RegisterReader read, byte unit)
    {
        var state = Try(() => read(unit, 31, 1));
        if (state is null) return null;
        var acInV = Try(() => read(unit, 3, 1));
        var acInI = Try(() => read(unit, 6, 1));
        var acInP = Try(() => read(unit, 12, 1));
        var acOutV = Try(() => read(unit, 15, 1));
        var acOutP = Try(() => read(unit, 23, 1));
        var dc = Try(() => read(unit, 26, 2));
        var input = Try(() => read(unit, 29, 1));
        var s = state[0];
        return new ChargerSnapshot
        {
            State = s,
            StateName = CerboRegisters.VeBus.DescribeState(s),
            AcInVolts = acInV is null ? null : CerboRegisters.VeBus.AcInVoltage.Decode(acInV[0]),
            AcInAmps = acInI is null ? null : CerboRegisters.VeBus.AcInCurrent.Decode(acInI[0]),
            AcInWatts = acInP is null ? null : CerboRegisters.VeBus.AcInPower.Decode(acInP[0]),
            AcOutVolts = acOutV is null ? null : CerboRegisters.VeBus.AcOutVoltage.Decode(acOutV[0]),
            AcOutWatts = acOutP is null ? null : CerboRegisters.VeBus.AcOutPower.Decode(acOutP[0]),
            DcVolts = dc is null ? null : CerboRegisters.VeBus.DcVoltage.Decode(dc[0]),
            DcAmps = dc is null ? null : CerboRegisters.VeBus.DcCurrent.Decode(dc[1]),
            AcInConnected = input is not null && input[0] != 240,
        };
    }

    /// <summary>The MPPT as <c>com.victronenergy.solarcharger</c>; null when unreachable.</summary>
    public static SolarSnapshot? ReadSolar(RegisterReader read, byte unit)
    {
        var block = Try(() => read(unit, 771, 2));    // 771 V, 772 I
        var state = Try(() => read(unit, 775, 2));    // 775 state, 776 PV V
        if (block is null || state is null) return null;
        var pvP = Try(() => read(unit, 789, 1));
        var err = Try(() => read(unit, 788, 1));
        var yield = Try(() => read(unit, 784, 1));
        return new SolarSnapshot
        {
            State = state[0],
            StateName = CerboRegisters.Solar.DescribeState(state[0]),
            DcVolts = CerboRegisters.Solar.DcVoltage.Decode(block[0]),
            DcAmps = CerboRegisters.Solar.DcCurrent.Decode(block[1]),
            PvVolts = CerboRegisters.Solar.PvVoltage.Decode(state[1]),
            PvWatts = pvP is null ? null : CerboRegisters.Solar.PvPower.Decode(pvP[0]),
            ErrorCode = err is null ? 0 : err[0],
            YieldTodayKwh = yield is null ? null : CerboRegisters.Solar.YieldTodayKwh.Decode(yield[0]),
        };
    }

    /// <summary>DVCC "Limit charge current" from unit 100; null when unreadable, −1 = no limit.</summary>
    public static double? ReadDvccLimit(RegisterReader read)
    {
        var r = Try(() => read(CerboRegisters.SystemUnit, CerboRegisters.System.DvccMaxChargeCurrent.Address, 1));
        return r is null ? null : CerboRegisters.System.DvccMaxChargeCurrent.Decode(r[0]);
    }

    /// <summary>True for the failures that mean the TCP session is gone (vs. a Modbus
    /// exception, which means "that register isn't published right now").</summary>
    public static bool IsTransportFailure(Exception ex) =>
        ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException
        || ex.InnerException is SocketException or IOException;

    private static ushort[]? Try(Func<ushort[]> f)
    {
        try { return f(); }
        catch (Exception ex) when (IsTransportFailure(ex)) { throw; }
        catch { return null; }
    }
}

/// <summary>What the MultiPlus is doing, from the Cerbo's vebus service.</summary>
public sealed record ChargerSnapshot
{
    public int State { get; init; }
    public string StateName { get; init; } = "";
    public bool AcInConnected { get; init; }
    public double? AcInVolts { get; init; }
    public double? AcInAmps { get; init; }
    public double? AcInWatts { get; init; }
    public double? AcOutVolts { get; init; }
    public double? AcOutWatts { get; init; }
    public double? DcVolts { get; init; }
    /// <summary>DC current into the battery from the charger, + = charging.</summary>
    public double? DcAmps { get; init; }

    /// <summary>A charger state that puts current into the battery (Bulk/Absorption/Float/Storage/Equalize).</summary>
    public bool IsCharging => State is 3 or 4 or 5 or 6 or 7;

    /// <summary>Running the loads from the battery — mains is gone or the unit is in inverter mode.</summary>
    public bool IsInverting => State is 9;
}

/// <summary>What the MPPT is doing, from the Cerbo's solarcharger service (Phase 2).</summary>
public sealed record SolarSnapshot
{
    public int State { get; init; }
    public string StateName { get; init; } = "";
    public double DcVolts { get; init; }
    public double DcAmps { get; init; }
    public double PvVolts { get; init; }
    public double? PvWatts { get; init; }
    public int ErrorCode { get; init; }
    public double? YieldTodayKwh { get; init; }
}
