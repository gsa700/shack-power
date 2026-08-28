using ShackPower.Core;
using Xunit;

namespace ShackPower.Core.Tests;

public class ChartSamplerTests
{
    private static readonly DateTime T0 = new(2026, 8, 28, 0, 0, 0);

    private static PowerLogEntry Row(int seconds, double? volts) =>
        new() { Timestamp = T0.AddSeconds(seconds), Volts = volts };

    private static IReadOnlyList<ChartSample> Volts(IReadOnlyList<PowerLogEntry> rows,
        double windowSeconds = 3600, int buckets = 60) =>
        ChartSampler.Decimate(rows, e => e.Volts, T0, windowSeconds, buckets);

    [Fact]
    public void A_one_second_spike_survives_decimation()
    {
        // 1000 idle rows with one transmit spike; 50 buckets means ~20 rows share its bucket —
        // the whole point of min/max over averaging.
        var rows = new List<PowerLogEntry>();
        for (var s = 0; s < 1000; s++) rows.Add(Row(s, s == 500 ? 11.2 : 13.5));
        var samples = ChartSampler.Decimate(rows, e => e.Volts, T0, 1000, 50);
        Assert.Equal(11.2, samples.Min(x => x.Min), 6);
        Assert.Equal(13.5, samples.Max(x => x.Max), 6);
    }

    [Fact]
    public void Buckets_without_samples_are_absent_so_gaps_stay_gaps()
    {
        var rows = new[] { Row(10, 13.5), Row(70, 13.5), Row(3000, 13.4) };   // app was down between
        var samples = Volts(rows);
        Assert.Equal(3, samples.Count);   // 60 s buckets: buckets 0 and 1, then one late bucket
        Assert.True(samples[2].OffsetSeconds - samples[1].OffsetSeconds > 2000);
    }

    [Fact]
    public void Rows_outside_the_window_are_ignored()
    {
        var rows = new[] { Row(-5, 10.0), Row(30, 13.5), Row(4000, 20.0) };
        var samples = Volts(rows);
        var s = Assert.Single(samples);
        Assert.Equal(13.5, s.Min, 6);
    }

    [Fact]
    public void Null_values_and_null_timestamps_are_skipped()
    {
        var rows = new[] { Row(10, null), new PowerLogEntry { Volts = 13.5 }, Row(20, 13.0) };
        var s = Assert.Single(Volts(rows));
        Assert.Equal(13.0, s.Min, 6);
    }

    [Fact]
    public void Bucket_offsets_are_bucket_centers()
    {
        var s = Assert.Single(Volts(new[] { Row(30, 13.5) }));   // 60 s buckets → first bucket
        Assert.Equal(30.0, s.OffsetSeconds, 6);                  // centered at 30 s
    }

    [Fact]
    public void The_last_instant_of_the_window_lands_in_the_last_bucket()
    {
        var rows = new[] { Row(3599, 13.5) };
        var s = Assert.Single(Volts(rows));
        Assert.Equal(3570.0, s.OffsetSeconds, 6);   // center of bucket 59
    }

    [Fact]
    public void Empty_input_gives_empty_output()
    {
        Assert.Empty(Volts(Array.Empty<PowerLogEntry>()));
        Assert.Empty(ChartSampler.Decimate(new[] { Row(1, 13.5) }, e => e.Volts, T0, 0, 60));
    }
}

public class ChartRingTests
{
    private static PowerLogEntry E(int n) => new() { Volts = n };

    [Fact]
    public void Snapshot_is_oldest_first()
    {
        var ring = new ChartRing(10);
        for (var i = 0; i < 5; i++) ring.Add(E(i));
        Assert.Equal([0.0, 1.0, 2.0, 3.0, 4.0], ring.Snapshot().Select(e => e.Volts!.Value));
    }

    [Fact]
    public void Wrapping_drops_the_oldest_and_keeps_order()
    {
        var ring = new ChartRing(3);
        for (var i = 0; i < 5; i++) ring.Add(E(i));
        Assert.Equal(3, ring.Count);
        Assert.Equal([2.0, 3.0, 4.0], ring.Snapshot().Select(e => e.Volts!.Value));
    }

    [Fact]
    public void An_empty_ring_snapshots_empty() => Assert.Empty(new ChartRing(4).Snapshot());
}
