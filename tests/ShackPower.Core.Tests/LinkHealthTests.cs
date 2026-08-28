using ShackPower.Core;
using Xunit;

namespace ShackPower.Core.Tests;

public class LinkHealthTests
{
    [Fact]
    public void Starts_healthy()
    {
        var h = new LinkHealth(8);
        Assert.False(h.IsLost);
        Assert.Equal(0, h.ConsecutiveFailures);
    }

    [Fact]
    public void Good_cycles_never_lose_the_link()
    {
        var h = new LinkHealth(deadCycleThreshold: 3);
        for (var i = 0; i < 100; i++) h.RecordCycle(anyData: true);
        Assert.False(h.IsLost);
    }

    [Fact]
    public void Occasional_dropouts_below_threshold_do_not_lose_the_link()
    {
        var h = new LinkHealth(deadCycleThreshold: 8);
        // A stream that stalls for a cycle now and then, but keeps delivering, stays connected.
        for (var i = 0; i < 50; i++)
        {
            h.RecordCycle(anyData: false);   // one empty cycle…
            h.RecordCycle(anyData: true);    // …then data again resets the run
            Assert.False(h.IsLost);
        }
    }

    [Fact]
    public void Loses_the_link_after_threshold_consecutive_empty_cycles()
    {
        var h = new LinkHealth(deadCycleThreshold: 5);
        for (var i = 0; i < 4; i++) h.RecordCycle(anyData: false);
        Assert.False(h.IsLost);          // 4 < 5: still hanging on
        h.RecordCycle(anyData: false);   // 5th: gone
        Assert.True(h.IsLost);
    }

    [Fact]
    public void A_single_good_cycle_before_threshold_resets_the_run()
    {
        var h = new LinkHealth(deadCycleThreshold: 3);
        h.RecordCycle(false);
        h.RecordCycle(false);
        h.RecordCycle(true);             // reprieve
        h.RecordCycle(false);
        h.RecordCycle(false);
        Assert.False(h.IsLost);          // the run restarted, so 2 < 3
    }

    [Fact]
    public void Fault_loses_the_link_immediately()
    {
        var h = new LinkHealth(deadCycleThreshold: 100);
        h.RecordCycle(anyData: true);
        h.Fault();
        Assert.True(h.IsLost);
    }

    [Fact]
    public void Reset_clears_loss_and_counter()
    {
        var h = new LinkHealth(deadCycleThreshold: 2);
        h.RecordCycle(false);
        h.RecordCycle(false);
        Assert.True(h.IsLost);

        h.Reset();
        Assert.False(h.IsLost);
        Assert.Equal(0, h.ConsecutiveFailures);
    }

    [Fact]
    public void Threshold_is_clamped_to_at_least_one()
    {
        var h = new LinkHealth(deadCycleThreshold: 0);
        h.RecordCycle(anyData: false);
        Assert.True(h.IsLost);           // treated as threshold 1
    }
}
