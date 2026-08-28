namespace ShackPower.Core;

/// <summary>
/// Decides when the serial link has gone dead so the reader can drop it and reconnect. Pure and
/// clock-free so it unit-tests deterministically — the reader feeds it one bool per poll cycle
/// and also flags a hard port fault directly. Two loss signals, weighted differently (the
/// LP-100A rule): a hard port error is acted on at once via <see cref="Fault"/>; silence gets a
/// grace window, because a healthy stream can stall briefly without the device being gone.
/// </summary>
public sealed class LinkHealth
{
    private readonly int _threshold;
    private int _consecutiveFailures;

    /// <param name="deadCycleThreshold">
    /// Consecutive no-data cycles before the link is declared lost. No default, deliberately
    /// (LP-100A's form): the caller derives it from a silence duration and its poll interval,
    /// e.g. <c>SilenceTimeoutMs / PollIntervalMs</c>, so the tolerance reads as time where it's
    /// chosen rather than as a magic cycle count.
    /// </param>
    public LinkHealth(int deadCycleThreshold)
    {
        if (deadCycleThreshold < 1) deadCycleThreshold = 1;
        _threshold = deadCycleThreshold;
    }

    /// <summary>True once the link is considered lost; stays latched until <see cref="Reset"/>.</summary>
    public bool IsLost { get; private set; }

    /// <summary>Consecutive fully-failed cycles seen so far (for diagnostics/tests).</summary>
    public int ConsecutiveFailures => _consecutiveFailures;

    /// <summary>Record one poll cycle. <paramref name="anyData"/> = at least one verified block arrived.</summary>
    public void RecordCycle(bool anyData)
    {
        if (anyData)
        {
            _consecutiveFailures = 0;
            IsLost = false;
        }
        else if (++_consecutiveFailures >= _threshold)
        {
            IsLost = true;
        }
    }

    /// <summary>A hard port error (I/O error / port closed): the link is lost immediately.</summary>
    public void Fault() => IsLost = true;

    /// <summary>Clear all state — call after a fresh (re)connect.</summary>
    public void Reset()
    {
        _consecutiveFailures = 0;
        IsLost = false;
    }
}
