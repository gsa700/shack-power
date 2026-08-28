namespace ShackPower.Core;

/// <summary>
/// One merged 1 Hz reading from the SmartShunt. Every measurement is nullable because VE.Direct
/// reports "---" for whatever the device's configuration makes unavailable — this station's shunt
/// runs as a DC energy meter (<c>MON 1</c>), so <see cref="Soc"/>/<see cref="ConsumedAh"/>/
/// <see cref="TtgMinutes"/> are null on the real hardware, but they must survive a shunt that is
/// reconfigured as a battery monitor. Built by <see cref="ReadingAccumulator"/> from the two
/// blocks the device alternates (main + H-field history).
/// </summary>
public sealed record PowerReading
{
    /// <summary>Battery/bus voltage in volts (VE.Direct <c>V</c>, mV on the wire).</summary>
    public double? Volts { get; init; }

    /// <summary>Current in amps, negative = discharge (<c>I</c>, mA on the wire).</summary>
    public double? Amps { get; init; }

    /// <summary>Instantaneous power in watts (<c>P</c>).</summary>
    public double? Watts { get; init; }

    /// <summary>State of charge in percent (<c>SOC</c>, ‰ on the wire). Null in DC-meter mode.</summary>
    public double? Soc { get; init; }

    /// <summary>Consumed amp-hours, negative as reported (<c>CE</c>, mAh). Null in DC-meter mode.</summary>
    public double? ConsumedAh { get; init; }

    /// <summary>Time-to-go in minutes; null when unavailable, and −1 on the wire means "infinite"
    /// (not discharging) — kept as −1 so the caller can distinguish it from unknown.</summary>
    public double? TtgMinutes { get; init; }

    public bool AlarmOn { get; init; }

    /// <summary>Alarm reason bitmask (<c>AR</c>); decode with <see cref="DescribeAlarm"/>.</summary>
    public int AlarmReasons { get; init; }

    /// <summary>Model string (<c>BMV</c>), e.g. "SmartShunt 300A".</summary>
    public string? DeviceName { get; init; }

    /// <summary>Firmware version as reported (<c>FW</c>), e.g. "0419".</summary>
    public string? Firmware { get; init; }

    /// <summary>DC monitor mode (<c>MON</c>): 0 = battery monitor, other values = DC energy meter
    /// appliance types. This station reports 1.</summary>
    public int? MonitorMode { get; init; }

    /// <summary>Minimum voltage since device history reset (<c>H7</c>, mV on the wire).</summary>
    public double? VminHistory { get; init; }

    /// <summary>Maximum voltage since device history reset (<c>H8</c>, mV on the wire).</summary>
    public double? VmaxHistory { get; init; }

    /// <summary>Cumulative discharged (battery mode) / produced (DC-meter mode) energy in kWh
    /// (<c>H17</c>, 0.01 kWh on the wire — a probe once misread 225 as 22.5 kWh; it is 2.25).</summary>
    public double? TotalKwhDrawn { get; init; }

    /// <summary>Cumulative charged (battery mode) / consumed (DC-meter mode) energy in kWh
    /// (<c>H18</c>, 0.01 kWh on the wire).</summary>
    public double? TotalKwhCharged { get; init; }

    private static readonly (int Bit, string Name)[] AlarmBits =
    [
        (1, "low voltage"), (2, "high voltage"), (4, "low SOC"),
        (8, "low starter voltage"), (16, "high starter voltage"),
        (32, "low temperature"), (64, "high temperature"), (128, "mid voltage"),
    ];

    /// <summary>Human-readable alarm reasons for an <c>AR</c> bitmask; "unknown" for a nonzero
    /// mask with no recognised bits (better than silence when firmware grows a new one).</summary>
    public static string DescribeAlarm(int reasons)
    {
        var names = AlarmBits.Where(b => (reasons & b.Bit) != 0).Select(b => b.Name).ToList();
        return names.Count > 0 ? string.Join(", ", names) : "unknown";
    }
}
