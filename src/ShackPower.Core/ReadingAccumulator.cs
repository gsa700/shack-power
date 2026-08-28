namespace ShackPower.Core;

/// <summary>
/// Merges the SmartShunt's alternating blocks into whole readings. The device broadcasts two
/// blocks per second — main (<c>V I P SOC …</c>) and history (<c>H1..H18</c>) — and neither alone
/// is a complete picture. This retains every field seen and emits one <see cref="PowerReading"/>
/// per <b>main</b> block (the one carrying both <c>V</c> and <c>I</c>), which is what keeps the
/// downstream pipeline — display, logging — at 1 Hz instead of 2 Hz half-readings. History
/// fields ride along on the next main-block emission. Pure and clock-free; create a fresh one
/// per connection session so stale fields can't survive a reconnect.
/// </summary>
public sealed class ReadingAccumulator
{
    private readonly Dictionary<string, string> _fields = new(StringComparer.Ordinal);

    /// <summary>Merge one parsed block; returns a complete reading when this was a main block.</summary>
    public PowerReading? Feed(Dictionary<string, string> blockFields)
    {
        foreach (var (key, value) in blockFields) _fields[key] = value;

        if (!blockFields.ContainsKey("V") || !blockFields.ContainsKey("I")) return null;

        return new PowerReading
        {
            Volts = VeDirectParser.Milli(Get("V")),
            Amps = VeDirectParser.Milli(Get("I")),
            Watts = VeDirectParser.Number(Get("P")),
            Soc = VeDirectParser.Permille(Get("SOC")),
            ConsumedAh = VeDirectParser.Milli(Get("CE")),
            TtgMinutes = VeDirectParser.Number(Get("TTG")),
            AlarmOn = Get("Alarm") == "ON",
            AlarmReasons = VeDirectParser.Int(Get("AR")) ?? 0,
            DeviceName = Get("BMV"),
            Firmware = Get("FW"),
            MonitorMode = VeDirectParser.Int(Get("MON")),
            VminHistory = VeDirectParser.Milli(Get("H7")),
            VmaxHistory = VeDirectParser.Milli(Get("H8")),
            TotalKwhDrawn = VeDirectParser.CentiKwh(Get("H17")),
            TotalKwhCharged = VeDirectParser.CentiKwh(Get("H18")),
        };
    }

    private string? Get(string key) => _fields.GetValueOrDefault(key);
}
