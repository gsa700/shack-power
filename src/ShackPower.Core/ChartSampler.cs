namespace ShackPower.Core;

/// <summary>One decimated chart bucket: seconds from window start, and the value envelope seen
/// inside the bucket. Min == Max when the bucket held one sample.</summary>
public readonly record struct ChartSample(double OffsetSeconds, double Min, double Max);

/// <summary>
/// Decimates log rows into a fixed number of min/max buckets for the strip chart.
/// <b>Min/max, not averages</b> — a one-second 20 A transmit spike must survive being squeezed
/// into 800 pixels of a 24-hour day, and averaging would smooth exactly the events the chart
/// exists to show. Buckets with no samples are simply absent, which is how gaps (app down, cable
/// out) reach the control — it breaks the line rather than interpolating across fiction.
/// Pure and clock-free; runs off-thread for big files, so it must touch no shared state.
/// </summary>
public static class ChartSampler
{
    public static IReadOnlyList<ChartSample> Decimate(
        IReadOnlyList<PowerLogEntry> entries,
        Func<PowerLogEntry, double?> channel,
        DateTime windowStart,
        double windowSeconds,
        int buckets)
    {
        if (windowSeconds <= 0 || buckets < 1 || entries.Count == 0) return [];

        var mins = new double[buckets];
        var maxs = new double[buckets];
        var filled = new bool[buckets];

        foreach (var e in entries)
        {
            if (e.Timestamp is not { } t || channel(e) is not { } v) continue;
            var offset = (t - windowStart).TotalSeconds;
            if (offset < 0 || offset >= windowSeconds) continue;
            var i = (int)(offset / windowSeconds * buckets);
            if (i >= buckets) i = buckets - 1;   // offset == windowSeconds-ε edge
            if (!filled[i]) { filled[i] = true; mins[i] = maxs[i] = v; }
            else
            {
                if (v < mins[i]) mins[i] = v;
                if (v > maxs[i]) maxs[i] = v;
            }
        }

        var bucketWidth = windowSeconds / buckets;
        var samples = new List<ChartSample>();
        for (var i = 0; i < buckets; i++)
            if (filled[i])
                samples.Add(new ChartSample((i + 0.5) * bucketWidth, mins[i], maxs[i]));
        return samples;
    }
}

/// <summary>
/// Fixed-capacity ring of the newest readings — the chart's live tail. The chart never reads
/// the file being appended (the day file is loaded once, off-thread); everything since that
/// load comes from here, in memory. UI-thread only, like the service that feeds it.
/// </summary>
public sealed class ChartRing
{
    private readonly PowerLogEntry[] _buf;
    private int _next;
    private int _count;

    /// <param name="capacity">Default one hour of 1 Hz readings.</param>
    public ChartRing(int capacity = 3600)
    {
        _buf = new PowerLogEntry[Math.Max(1, capacity)];
    }

    public int Count => _count;

    public void Add(PowerLogEntry entry)
    {
        _buf[_next] = entry;
        _next = (_next + 1) % _buf.Length;
        if (_count < _buf.Length) _count++;
    }

    /// <summary>Oldest-first copy — an immutable snapshot the decimator can walk safely.</summary>
    public PowerLogEntry[] Snapshot()
    {
        var result = new PowerLogEntry[_count];
        var start = (_next - _count + _buf.Length) % _buf.Length;
        for (var i = 0; i < _count; i++)
            result[i] = _buf[(start + i) % _buf.Length];
        return result;
    }
}
