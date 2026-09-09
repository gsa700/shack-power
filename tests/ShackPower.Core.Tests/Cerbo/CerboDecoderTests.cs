using ShackPower.Core;
using ShackPower.Core.Cerbo;
using Xunit;

namespace ShackPower.Core.Tests.Cerbo;

public class CerboDecoderTests
{
    private const byte Shunt = 226;

    private static FakeCerboModbus FullShunt()
    {
        var m = new FakeCerboModbus();
        m.Set(Shunt, CerboRegisters.Battery.Power, -77);
        m.Set(Shunt, CerboRegisters.Battery.Voltage, 13.10);
        m.SetRaw(Shunt, 260, 0);                                    // the unused word inside the core block
        m.Set(Shunt, CerboRegisters.Battery.Current, -5.9);
        m.Set(Shunt, CerboRegisters.Battery.ConsumedAh, -12.4);
        m.Set(Shunt, CerboRegisters.Battery.Soc, 87.5);
        m.SetRaw(Shunt, 267, 0); m.SetRaw(Shunt, 268, 0); m.SetRaw(Shunt, 269, 0);
        m.SetRaw(Shunt, 270, 0); m.SetRaw(Shunt, 271, 0); m.SetRaw(Shunt, 272, 0);
        m.Set(Shunt, CerboRegisters.Battery.MinVoltage, 12.86);
        m.Set(Shunt, CerboRegisters.Battery.MaxVoltage, 14.28);
        m.Set(Shunt, CerboRegisters.Battery.DischargedKwh, 2.3);
        m.Set(Shunt, CerboRegisters.Battery.ChargedKwh, 0.4);
        m.SetRaw(Shunt, 303, 162);                                  // 162 / 0.01 = 16200 s = 270 min
        return m;
    }

    [Fact]
    public void RegisterSpec_scales_and_signs_per_victron_conventions()
    {
        Assert.Equal(13.20, CerboRegisters.Battery.Voltage.Decode(1320), 3);
        Assert.Equal(-5.9, CerboRegisters.Battery.Current.Decode(unchecked((ushort)(short)-59)), 3);
        Assert.Equal(87.5, CerboRegisters.Battery.Soc.Decode(875), 3);
        // ConsumedAmphours: scale −10 → a positive word becomes the negative Ah Victron reports.
        Assert.Equal(-12.4, CerboRegisters.Battery.ConsumedAh.Decode(124), 3);
        // vebus AC power: scale 0.1 → the word is in tens of watts.
        Assert.Equal(830, CerboRegisters.VeBus.AcInPower.Decode(83), 3);
        // TTG: scale 0.01 → seconds.
        Assert.Equal(16200, CerboRegisters.Battery.TimeToGoSeconds.Decode(162), 3);
        // DVCC limit −1 = no limit survives the int16 round trip.
        Assert.Equal(-1, CerboRegisters.System.DvccMaxChargeCurrent.Decode(0xFFFF), 3);
    }

    [Fact]
    public void ReadBattery_decodes_every_group()
    {
        var m = FullShunt();
        var r = CerboDecoder.ReadBattery(m.ReadHolding, Shunt)!;

        Assert.Equal(13.10, r.Volts!.Value, 3);
        Assert.Equal(-5.9, r.Amps!.Value, 3);
        Assert.Equal(-77, r.Watts!.Value, 3);
        Assert.Equal(87.5, r.Soc!.Value, 3);
        Assert.Equal(-12.4, r.ConsumedAh!.Value, 3);
        Assert.Equal(270, r.TtgMinutes!.Value, 3);
        Assert.Equal(12.86, r.VminHistory!.Value, 3);
        Assert.Equal(14.28, r.VmaxHistory!.Value, 3);
        Assert.Equal(2.3, r.TotalKwhDrawn!.Value, 3);
        Assert.Equal(0.4, r.TotalKwhCharged!.Value, 3);
        Assert.False(r.AlarmOn);
        Assert.Equal(0, r.MonitorMode);                 // a shunt on the GX is a battery monitor
        Assert.Null(r.Charger);
        Assert.Null(r.Solar);
    }

    [Fact]
    public void ReadBattery_missing_optional_groups_become_nulls_not_failures()
    {
        var m = new FakeCerboModbus();
        m.Set(Shunt, CerboRegisters.Battery.Power, 80);
        m.Set(Shunt, CerboRegisters.Battery.Voltage, 14.26);
        m.SetRaw(Shunt, 260, 0);
        m.Set(Shunt, CerboRegisters.Battery.Current, 5.6);
        // No SOC/CE (DC-meter mode), no history, no energy, no TTG published.

        var r = CerboDecoder.ReadBattery(m.ReadHolding, Shunt)!;
        Assert.Equal(14.26, r.Volts!.Value, 3);
        Assert.Null(r.Soc);
        Assert.Null(r.ConsumedAh);
        Assert.Null(r.VminHistory);
        Assert.Null(r.TotalKwhDrawn);
        Assert.Equal(-1, r.TtgMinutes);                 // VE.Direct's "infinite" convention kept
    }

    [Fact]
    public void ReadBattery_returns_null_when_the_core_block_is_unavailable()
    {
        var m = new FakeCerboModbus();                  // nothing published at all
        Assert.Null(CerboDecoder.ReadBattery(m.ReadHolding, Shunt));
    }

    [Fact]
    public void ReadBattery_propagates_transport_failures()
    {
        var m = FullShunt();
        m.ThrowTransportOnRead = true;
        Assert.Throws<IOException>(() => CerboDecoder.ReadBattery(m.ReadHolding, Shunt));
    }

    [Fact]
    public void ReadBattery_maps_gx_alarm_registers_onto_the_vedirect_bitmask()
    {
        var m = FullShunt();
        m.SetRaw(Shunt, 267, 2);   // alarm active
        m.SetRaw(Shunt, 268, 2);   // low voltage
        m.SetRaw(Shunt, 272, 2);   // low SOC
        var r = CerboDecoder.ReadBattery(m.ReadHolding, Shunt)!;
        Assert.True(r.AlarmOn);
        Assert.Equal(1 | 4, r.AlarmReasons);
        Assert.Equal("low voltage, low SOC", PowerReading.DescribeAlarm(r.AlarmReasons));
    }

    [Fact]
    public void ReadVeBus_decodes_state_and_ac_dc_figures()
    {
        const byte Multi = 227;
        var m = new FakeCerboModbus();
        m.Set(Multi, CerboRegisters.VeBus.State, 5);            // Float
        m.Set(Multi, CerboRegisters.VeBus.AcInVoltage, 121.3);
        m.Set(Multi, CerboRegisters.VeBus.AcInCurrent, 1.4);
        m.Set(Multi, CerboRegisters.VeBus.AcInPower, 160);
        m.Set(Multi, CerboRegisters.VeBus.AcOutVoltage, 120.9);
        m.Set(Multi, CerboRegisters.VeBus.AcOutPower, 140);
        m.Set(Multi, CerboRegisters.VeBus.DcVoltage, 13.52);
        m.Set(Multi, CerboRegisters.VeBus.DcCurrent, 1.2);
        m.Set(Multi, CerboRegisters.VeBus.ActiveInput, 0);      // AC input 1 live

        var c = CerboDecoder.ReadVeBus(m.ReadHolding, Multi)!;
        Assert.Equal("Float", c.StateName);
        Assert.True(c.IsCharging);
        Assert.False(c.IsInverting);
        Assert.True(c.AcInConnected);
        Assert.Equal(121.3, c.AcInVolts!.Value, 3);
        Assert.Equal(160, c.AcInWatts!.Value, 3);
        Assert.Equal(140, c.AcOutWatts!.Value, 3);
        Assert.Equal(13.52, c.DcVolts!.Value, 3);
        Assert.Equal(1.2, c.DcAmps!.Value, 3);
    }

    [Fact]
    public void ReadVeBus_inverting_with_mains_gone()
    {
        const byte Multi = 227;
        var m = new FakeCerboModbus();
        m.Set(Multi, CerboRegisters.VeBus.State, 9);            // Inverting
        m.Set(Multi, CerboRegisters.VeBus.ActiveInput, 240);    // disconnected
        var c = CerboDecoder.ReadVeBus(m.ReadHolding, Multi)!;
        Assert.True(c.IsInverting);
        Assert.False(c.AcInConnected);
        Assert.Null(c.AcInVolts);                              // not published → null, not 0
    }

    [Fact]
    public void ReadSolar_decodes_pv_and_charger_figures()
    {
        const byte Mppt = 224;
        var m = new FakeCerboModbus();
        m.Set(Mppt, CerboRegisters.Solar.DcVoltage, 13.9);
        m.Set(Mppt, CerboRegisters.Solar.DcCurrent, 9.8);
        m.Set(Mppt, CerboRegisters.Solar.State, 3);
        m.Set(Mppt, CerboRegisters.Solar.PvVoltage, 47.6);
        m.Set(Mppt, CerboRegisters.Solar.PvPower, 142);
        m.Set(Mppt, CerboRegisters.Solar.ErrorCode, 0);
        m.Set(Mppt, CerboRegisters.Solar.YieldTodayKwh, 0.6);
        var s = CerboDecoder.ReadSolar(m.ReadHolding, Mppt)!;
        Assert.Equal("Bulk", s.StateName);
        Assert.Equal(47.6, s.PvVolts, 3);
        Assert.Equal(142, s.PvWatts!.Value, 3);
        Assert.Equal(9.8, s.DcAmps, 3);
        Assert.Equal(0.6, s.YieldTodayKwh!.Value, 3);
    }

    [Fact]
    public void ReadDvccLimit_reads_unit_100_and_keeps_the_no_limit_sentinel()
    {
        var m = new FakeCerboModbus();
        m.SetRaw(CerboRegisters.SystemUnit, 2705, 0xFFFF);
        Assert.Equal(-1, CerboDecoder.ReadDvccLimit(m.ReadHolding));
        m.SetRaw(CerboRegisters.SystemUnit, 2705, 0);
        Assert.Equal(0, CerboDecoder.ReadDvccLimit(m.ReadHolding));
        Assert.Null(CerboDecoder.ReadDvccLimit(new FakeCerboModbus().ReadHolding));
    }
}
