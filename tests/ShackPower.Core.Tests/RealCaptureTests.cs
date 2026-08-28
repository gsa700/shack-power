using ShackPower.Core;
using Xunit;

namespace ShackPower.Core.Tests;

/// <summary>
/// Runs a real raw capture from the station's SmartShunt 300A through the whole receive
/// pipeline. Captured at cutover on 2026-08-28 with <c>tools/Capture-VeDirect.ps1</c> (10 s of
/// stream, DC-energy-meter mode, ~13.9 V float) — this is the file that pins the framer and
/// parser to the wire truth the constructed fixtures only approximate.
/// </summary>
public class RealCaptureTests
{
    private static byte[] Capture() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "vedirect-capture.bin"));

    [Fact]
    public void Every_completed_block_in_the_real_stream_parses()
    {
        var framer = new VeDirectFramer();
        var accumulator = new ReadingAccumulator();
        var blocks = 0;
        var readings = new List<PowerReading>();

        foreach (var body in framer.Feed(Capture()))
        {
            Assert.True(VeDirectParser.TryParseBlock(body, out var fields));
            blocks++;
            if (accumulator.Feed(fields) is { } reading) readings.Add(reading);
        }

        // 10 s of 2 blocks/s, minus the mid-stream fragment the checksum rightly rejects.
        Assert.InRange(blocks, 12, 30);
        Assert.InRange(readings.Count, 6, 15);
    }

    [Fact]
    public void The_real_readings_look_like_this_stations_shunt()
    {
        var framer = new VeDirectFramer();
        var accumulator = new ReadingAccumulator();
        var readings = new List<PowerReading>();
        foreach (var body in framer.Feed(Capture()))
            if (VeDirectParser.TryParseBlock(body, out var fields)
                && accumulator.Feed(fields) is { } reading)
                readings.Add(reading);

        var r = readings.Last();   // last one has merged both block kinds
        Assert.Equal("SmartShunt 300A", r.DeviceName);
        Assert.Equal(1, r.MonitorMode);          // DC energy meter mode
        Assert.Null(r.Soc);                      // "---" in this mode
        Assert.InRange(r.Volts!.Value, 10, 16);  // a healthy 12 V system
        Assert.NotNull(r.VminHistory);           // history block merged in
        Assert.NotNull(r.TotalKwhCharged);
    }
}
