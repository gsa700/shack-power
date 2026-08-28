using ShackPower.Core;
using Xunit;

namespace ShackPower.Core.Tests;

/// <summary>
/// The accumulator's contract: the device alternates main and history blocks at 2 blocks/s, and
/// exactly one complete reading comes out per main block — that gate is what keeps display and
/// logging at 1 Hz instead of 2 Hz half-readings.
/// </summary>
public class ReadingAccumulatorTests
{
    private static Dictionary<string, string> MainBlock(string v = "13960", string i = "6298") => new()
    {
        ["PID"] = "0xC038", ["V"] = v, ["I"] = i, ["P"] = "88",
        ["CE"] = "---", ["SOC"] = "---", ["TTG"] = "---",
        ["Alarm"] = "OFF", ["AR"] = "0",
        ["BMV"] = "SmartShunt 300A", ["FW"] = "0419", ["MON"] = "1",
    };

    private static Dictionary<string, string> HistoryBlock() => new()
    {
        ["H7"] = "13842", ["H8"] = "13992", ["H17"] = "225", ["H18"] = "310",
    };

    [Fact]
    public void A_history_only_block_emits_nothing()
    {
        Assert.Null(new ReadingAccumulator().Feed(HistoryBlock()));
    }

    [Fact]
    public void A_main_block_emits_a_reading_with_converted_units()
    {
        var r = new ReadingAccumulator().Feed(MainBlock());
        Assert.NotNull(r);
        Assert.Equal(13.96, r!.Volts!.Value, 6);
        Assert.Equal(6.298, r.Amps!.Value, 6);
        Assert.Equal(88.0, r.Watts!.Value, 6);
        Assert.Equal("SmartShunt 300A", r.DeviceName);
        Assert.Equal("0419", r.Firmware);
        Assert.Equal(1, r.MonitorMode);
        Assert.False(r.AlarmOn);
    }

    [Fact]
    public void Dc_meter_mode_unavailable_fields_come_through_null()
    {
        var r = new ReadingAccumulator().Feed(MainBlock());
        Assert.Null(r!.Soc);
        Assert.Null(r.ConsumedAh);
        Assert.Null(r.TtgMinutes);
    }

    [Fact]
    public void History_fields_ride_along_on_the_next_main_block()
    {
        var acc = new ReadingAccumulator();
        Assert.Null(acc.Feed(HistoryBlock()));
        var r = acc.Feed(MainBlock());
        Assert.Equal(13.842, r!.VminHistory!.Value, 6);
        Assert.Equal(13.992, r.VmaxHistory!.Value, 6);
        Assert.Equal(2.25, r.TotalKwhDrawn!.Value, 6);
        Assert.Equal(3.10, r.TotalKwhCharged!.Value, 6);
    }

    [Fact]
    public void Before_any_history_block_the_history_fields_are_null()
    {
        var r = new ReadingAccumulator().Feed(MainBlock());
        Assert.Null(r!.VminHistory);
        Assert.Null(r.TotalKwhCharged);
    }

    [Fact]
    public void Later_main_blocks_refresh_the_live_values_and_keep_history()
    {
        var acc = new ReadingAccumulator();
        acc.Feed(HistoryBlock());
        acc.Feed(MainBlock());
        var r = acc.Feed(MainBlock(v: "13500", i: "-2000"));
        Assert.Equal(13.5, r!.Volts!.Value, 6);
        Assert.Equal(-2.0, r.Amps!.Value, 6);
        Assert.Equal(13.842, r.VminHistory!.Value, 6);   // history persists between its blocks
    }

    [Fact]
    public void An_active_alarm_decodes_state_and_reason()
    {
        var block = MainBlock();
        block["Alarm"] = "ON";
        block["AR"] = "1";
        var r = new ReadingAccumulator().Feed(block);
        Assert.True(r!.AlarmOn);
        Assert.Equal("low voltage", PowerReading.DescribeAlarm(r.AlarmReasons));
    }

    [Fact]
    public void Soc_and_ttg_parse_when_the_shunt_is_a_battery_monitor()
    {
        var block = MainBlock();
        block["SOC"] = "872";
        block["CE"] = "-12400";
        block["TTG"] = "-1";
        block["MON"] = "0";
        var r = new ReadingAccumulator().Feed(block);
        Assert.Equal(87.2, r!.Soc!.Value, 6);
        Assert.Equal(-12.4, r.ConsumedAh!.Value, 6);
        Assert.Equal(-1.0, r.TtgMinutes!.Value, 6);   // −1 = infinite, distinct from unknown (null)
        Assert.Equal(0, r.MonitorMode);
    }
}
