using ShackPower.Core.Cerbo;
using Xunit;

namespace ShackPower.Core.Tests.Cerbo;

public class ChargeInhibitTests
{
    private const byte Sys = CerboRegisters.SystemUnit;
    private const ushort Reg = 2705;

    [Fact]
    public void Engage_remembers_the_previous_limit_and_writes_zero()
    {
        var m = new FakeCerboModbus();
        m.SetRaw(Sys, Reg, 30);                                 // DVCC limit 30 A in force
        var inhibit = new ChargeInhibit(m);

        inhibit.Engage();

        Assert.True(inhibit.IsEngaged);
        Assert.Equal((short)30, inhibit.PreviousLimitAmps);
        Assert.Equal((Sys, Reg, (ushort)0), m.Writes.Single());
    }

    [Fact]
    public void Release_restores_the_remembered_limit_including_no_limit()
    {
        var m = new FakeCerboModbus();
        m.SetRaw(Sys, Reg, 0xFFFF);                             // −1 = no limit
        var inhibit = new ChargeInhibit(m);
        inhibit.Engage();
        inhibit.Release();

        Assert.False(inhibit.IsEngaged);
        Assert.Equal((ushort)0xFFFF, m.Writes.Last().Value);
        Assert.Equal(-1, CerboRegisters.System.DvccMaxChargeCurrent.Decode(m.Registers[(Sys, Reg)]));
    }

    [Fact]
    public void Engaging_over_a_stale_zero_never_restores_to_zero()
    {
        // The app crashed inhibited last time; the GX still says 0. Releasing must resume charging.
        var m = new FakeCerboModbus();
        m.SetRaw(Sys, Reg, 0);
        var inhibit = new ChargeInhibit(m);
        inhibit.Engage();
        inhibit.Release();
        Assert.Equal((ushort)0xFFFF, m.Registers[(Sys, Reg)]);
    }

    [Fact]
    public void Reassert_only_writes_while_engaged_and_engage_is_idempotent()
    {
        var m = new FakeCerboModbus();
        m.SetRaw(Sys, Reg, 20);
        var inhibit = new ChargeInhibit(m);

        inhibit.Reassert();                                     // not engaged: no write
        Assert.Empty(m.Writes);

        inhibit.Engage();
        inhibit.Engage();                                       // second engage must not re-read the (now 0) limit
        inhibit.Reassert();
        Assert.Equal((short)20, inhibit.PreviousLimitAmps);
        Assert.All(m.Writes, w => Assert.Equal((ushort)0, w.Value));
        Assert.Equal(3, m.Writes.Count);

        inhibit.Release();
        Assert.Equal((ushort)20, m.Writes.Last().Value);
    }

    [Fact]
    public void ForceNoLimit_clears_regardless_of_state()
    {
        var m = new FakeCerboModbus();
        m.SetRaw(Sys, Reg, 0);
        var inhibit = new ChargeInhibit(m);
        inhibit.ForceNoLimit();
        Assert.False(inhibit.IsEngaged);
        Assert.Equal((ushort)0xFFFF, m.Registers[(Sys, Reg)]);
    }

    [Fact]
    public void Transport_failure_surfaces_instead_of_pretending_the_charger_is_off()
    {
        var m = new FakeCerboModbus { ThrowTransportOnRead = true };
        var inhibit = new ChargeInhibit(m);
        Assert.Throws<IOException>(() => inhibit.Engage());
        Assert.False(inhibit.IsEngaged);
    }
}
