using ShackPower.Core.Cerbo;
using Xunit;

namespace ShackPower.Core.Tests.Cerbo;

public class CerboDiscoveryTests
{
    [Fact]
    public void Probe_classifies_each_unit_by_its_signature_register()
    {
        var m = new FakeCerboModbus();
        m.Set(226, CerboRegisters.Battery.Voltage, 13.21);      // shunt on VE.Direct port 1
        m.Set(226, CerboRegisters.Battery.Soc, 91.0);
        m.Set(227, CerboRegisters.VeBus.State, 5);              // MultiPlus on the VE.Bus port
        m.Set(224, CerboRegisters.Solar.State, 0);              // MPPT on VE.Direct port 2
        m.Set(100, CerboRegisters.Battery.Voltage, 13.21);      // system aggregate mirrors the battery — must be skipped

        var found = CerboDiscovery.Probe(m);

        Assert.Equal(3, found.Count);
        var battery = Assert.Single(found, s => s.Kind == CerboServiceKind.Battery);
        Assert.Equal(226, battery.UnitId);
        Assert.Contains("13.21 V", battery.Summary);
        Assert.Contains("SOC 91.0 %", battery.Summary);
        var multi = Assert.Single(found, s => s.Kind == CerboServiceKind.VeBus);
        Assert.Equal(227, multi.UnitId);
        Assert.Contains("Float", multi.Summary);
        var mppt = Assert.Single(found, s => s.Kind == CerboServiceKind.Solar);
        Assert.Equal(224, mppt.UnitId);
        Assert.DoesNotContain(found, s => s.UnitId == 100);
    }

    [Fact]
    public void Probe_with_nothing_attached_finds_nothing_and_does_not_throw()
    {
        Assert.Empty(CerboDiscovery.Probe(new FakeCerboModbus()));
    }

    [Fact]
    public void Probe_propagates_a_dead_link()
    {
        var m = new FakeCerboModbus { ThrowTransportOnRead = true };
        Assert.Throws<IOException>(() => CerboDiscovery.Probe(m));
    }

    [Fact]
    public void Default_candidates_cover_the_cerbo_port_ids_once_each()
    {
        var all = CerboDiscovery.DefaultCandidates.ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        foreach (byte id in new byte[] { 223, 224, 226, 227, 239, 246, 247 })
            Assert.Contains(id, all);
    }
}
